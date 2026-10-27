using Microsoft.Extensions.Options;
using Site_2024.Models.Domain.Parts;
using Site_2024.Web.Api.Models;
using Site_2024.Web.Api.Requests;

namespace Site_2024.Web.Api.Services
{
    // A scoped reconciler: each pass checks Shopify's actual state and the SQL
    // part, rather than trusting an in-memory stage. Restart-safe and resumable.
    // This is an intentionally DEV-store-restricted pilot, not a production cutover.
    public sealed class ShopifyShippingAsyncProcessor
    {
        private readonly ShopifyShippingJobStore _store;
        private readonly IPartService _parts;
        private readonly IShippingPoliciesService _policies;
        private readonly IShopifyAdminService _shopify;
        private readonly ShopifySettings _settings;
        private readonly ILogger<ShopifyShippingAsyncProcessor> _log;

        public ShopifyShippingAsyncProcessor(
            ShopifyShippingJobStore store, IPartService parts,
            IShippingPoliciesService policies, IShopifyAdminService shopify,
            IOptions<ShopifySettings> settings,
            ILogger<ShopifyShippingAsyncProcessor> log)
        {
            _store = store;
            _parts = parts;
            _policies = policies;
            _shopify = shopify;
            _settings = settings.Value;
            _log = log;
        }

        private static bool HasTag(ShopifyProductShippingSnapshot s, int policyId) =>
            s.ShippingClassTags.Count == 1 &&
            string.Equals(s.ShippingClassTags[0], $"ShippingClass_{policyId}",
                StringComparison.OrdinalIgnoreCase);

        private async Task AuditMappedCollectionsAsync(
            IReadOnlyCollection<ShippingPolicy> policies, string productGid)
        {
            var checkout = policies.Where(p => p.IsActive && p.AllowsOnlineCheckout).ToList();
            if (checkout.Count == 0 ||
                checkout.Any(p => string.IsNullOrWhiteSpace(p.ShopifyShippingCollectionGid)))
                throw new InvalidOperationException("Some active checkout policies are not mapped.");

            foreach (string gid in checkout.Select(p => p.ShopifyShippingCollectionGid!)
                .Distinct(StringComparer.Ordinal))
            {
                var audit = await _shopify.AuditShippingCollectionMembershipAsync(gid);
                if (audit == null || audit.CollectionGid != gid ||
                    audit.Sources.Any(s => s.SourceType != "CollectionConditionsSource" ||
                        s.MoreManualSelections || s.ManualSelectionProductGids.Contains(productGid)))
                    throw new InvalidOperationException(
                        "A Shopify shipping collection contains manual/unknown membership or cannot be completely audited.");
            }
        }

        public async Task ProcessAsync(ShopifyShippingReassignmentJob job, Guid owner,
            CancellationToken stoppingToken)
        {
            stoppingToken.ThrowIfCancellationRequested();
            bool safetyWriteStarted = false;
            Part? part = null;
            try
            {
                // This pilot must not be repurposed to run on the client's live
                // store, regardless of which Azure slot/branch hosts the build.
                if (!_settings.AsyncShippingJobsSchemaReady ||
                    !_settings.EnableMarketDrivenShippingAsyncWorker ||
                    !_settings.AllowMarketDrivenShippingPublishing ||
                    !string.Equals(_settings.ShopDomain,
                        "gr-sons-shipping-dev.myshopify.com",
                        StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("DEV shipping job worker configuration is not authorized.");

                part = _parts.GetPartById(job.PartId);
                if (part?.ShopifyProductId != job.ShopifyProductId ||
                    !part.ShopifyVariantId.HasValue || !part.ShopifyInventoryItemId.HasValue)
                    throw new InvalidOperationException("The part's Shopify IDs changed after queueing.");

                var policies = _policies.GetAll() ?? new List<ShippingPolicy>();
                var old = policies.FirstOrDefault(p => p.Id == job.FromPolicyId);
                var target = policies.FirstOrDefault(p =>
                    p.Id == job.ToPolicyId && p.IsActive && p.AllowsOnlineCheckout);
                if (old?.AllowsOnlineCheckout != true || target == null ||
                    string.IsNullOrWhiteSpace(old.ShopifyShippingCollectionGid) ||
                    string.IsNullOrWhiteSpace(target.ShopifyShippingCollectionGid) ||
                    old.ShopifyShippingCollectionGid == target.ShopifyShippingCollectionGid)
                    throw new InvalidOperationException("Shipping policy mappings changed.");

                string productGid = $"gid://shopify/Product/{job.ShopifyProductId}";
                await AuditMappedCollectionsAsync(policies, productGid);

                var source = await _shopify.GetShippingCollectionAsync(target.ShopifyShippingCollectionGid);
                bool ruleValid = source?.Sources.Any(s =>
                    s.SourceType == "CollectionConditionsSource" &&
                    s.TargetType == "PRODUCTS" && !s.HasExclusions &&
                    !s.HasExplicitSelections && s.InclusionConditionCount == 1 &&
                    s.TagConditions.Count == 1 &&
                    s.TagConditions[0].Relation == "TAGGED_WITH" &&
                    s.TagConditions[0].MatchType == "ANY" &&
                    s.TagConditions[0].Values.Count == 1 &&
                    string.Equals(s.TagConditions[0].Values[0],
                        $"ShippingClass_{target.Id}", StringComparison.OrdinalIgnoreCase)) == true;
                if (!ruleValid)
                    throw new InvalidOperationException("Target shipping tag source is no longer valid.");

                bool usesMarkets = await _shopify.UsesMarketDrivenShippingAsync();
                if (!usesMarkets)
                    throw new InvalidOperationException("Shopify no longer reports Market-driven Shipping.");

                var snapshot = await _shopify.GetProductShippingSnapshotAsync(job.ShopifyProductId);
                if (snapshot.ProductGid != productGid)
                    throw new InvalidOperationException("Shopify product identity mismatch.");

                int sqlPolicy = part.ShippingPolicy?.Id ?? 0;
                if (sqlPolicy != job.FromPolicyId && sqlPolicy != job.ToPolicyId)
                    throw new InvalidOperationException("SQL shipping policy changed outside the pending job.");

                if (sqlPolicy == job.FromPolicyId)
                {
                    // No checkout exposure is permitted while the tag and SQL
                    // disagree. A crash after unpublish/tagging can resume here.
                    if (snapshot.Status == "ACTIVE" || snapshot.PublishedOnOnlineStore)
                    {
                        if (!HasTag(snapshot, job.FromPolicyId))
                            throw new InvalidOperationException(
                                "An active product has an unexpected tag while SQL uses the source policy.");
                        stoppingToken.ThrowIfCancellationRequested();
                        safetyWriteStarted = true;
                        await _shopify.UnpublishProductForPartAsync(part);
                        snapshot = await _shopify.GetProductShippingSnapshotAsync(job.ShopifyProductId);
                    }
                    if (snapshot.Status != "DRAFT" || snapshot.PublishedOnOnlineStore)
                        throw new InvalidOperationException("Shopify draft/unpublished state did not verify.");

                    if (HasTag(snapshot, job.FromPolicyId))
                    {
                        stoppingToken.ThrowIfCancellationRequested();
                        safetyWriteStarted = true;
                        part.ShippingPolicy = target; // only in memory until membership settles
                        await _shopify.SyncProductTagsForPartAsync(part);
                        snapshot = await _shopify.GetProductShippingSnapshotAsync(job.ShopifyProductId);
                    }
                    if (!HasTag(snapshot, job.ToPolicyId) ||
                        snapshot.Status != "DRAFT" || snapshot.PublishedOnOnlineStore)
                        throw new InvalidOperationException(
                            "Shopify did not verify the intended tag while unpublished.");
                }
                else
                {
                    // SQL already committed on a previous pass before a restart.
                    // Reconcile with Shopify; never revert the policy implicitly.
                    if (!HasTag(snapshot, job.ToPolicyId))
                        throw new InvalidOperationException("SQL target policy and Shopify tag disagree.");
                }

                bool inNew = await _shopify.CollectionContainsProductAsync(
                    target.ShopifyShippingCollectionGid, productGid);
                bool inOld = await _shopify.CollectionContainsProductAsync(
                    old.ShopifyShippingCollectionGid, productGid);

                if (!inNew || inOld)
                {
                    if (sqlPolicy == job.ToPolicyId ||
                        snapshot.Status != "DRAFT" || snapshot.PublishedOnOnlineStore)
                        throw new InvalidOperationException(
                            "Shipping collections do not match while product could be published.");

                    if (DateTime.UtcNow - job.CreatedUtc > TimeSpan.FromMinutes(45))
                    {
                        await _store.UpdateAsync(job, owner, "AttentionRequired", false,
                            TimeSpan.Zero, inOld, inNew,
                            "Shopify collection membership did not converge within 45 minutes. Product kept unpublished.",
                            countCheck: true, ct: stoppingToken);
                        _log.LogError("Shipping job {JobId} requires manual review after 45 minutes.", job.JobId);
                        return;
                    }
                    await _store.UpdateAsync(job, owner, "WaitingForShopify", false,
                        TimeSpan.FromSeconds(20), inOld, inNew,
                        countCheck: true, ct: stoppingToken);
                    _log.LogInformation("Shipping job {JobId} waiting: old={Old}, target={New}.",
                        job.JobId, inOld, inNew);
                    return;
                }

                // Membership must be stable for TWO reads while unpublished.
                bool confirmedNew = await _shopify.CollectionContainsProductAsync(
                    target.ShopifyShippingCollectionGid, productGid);
                bool confirmedOld = await _shopify.CollectionContainsProductAsync(
                    old.ShopifyShippingCollectionGid, productGid);
                if (!confirmedNew || confirmedOld)
                {
                    await _store.UpdateAsync(job, owner, "WaitingForShopify", false,
                        TimeSpan.FromSeconds(20), confirmedOld, confirmedNew,
                        countCheck: true, ct: stoppingToken);
                    return;
                }

                if (sqlPolicy == job.FromPolicyId)
                {
                    // The server-side SQL update can complete even if a process
                    // crashes before the job row advances. Resume by re-reading SQL.
                    var latest = _parts.GetPartById(job.PartId);
                    var beforeSql = await _shopify.GetProductShippingSnapshotAsync(job.ShopifyProductId);
                    if (latest?.ShippingPolicy?.Id != job.FromPolicyId ||
                        latest.ShopifyProductId != job.ShopifyProductId ||
                        beforeSql.Status != "DRAFT" || beforeSql.PublishedOnOnlineStore ||
                        !HasTag(beforeSql, job.ToPolicyId))
                        throw new InvalidOperationException("Part/Shopify changed before SQL commit.");
                    stoppingToken.ThrowIfCancellationRequested();
                    safetyWriteStarted = true;
                    _parts.PatchPart(job.PartId,
                        new PartPatchRequest { ShippingPolicyId = job.ToPolicyId },
                        job.RequestedByUserId);
                    part = _parts.GetPartById(job.PartId);
                    if (part?.ShippingPolicy?.Id != job.ToPolicyId)
                        throw new InvalidOperationException("SQL shipping policy could not be verified.");
                }

                snapshot = await _shopify.GetProductShippingSnapshotAsync(job.ShopifyProductId);
                if (!HasTag(snapshot, job.ToPolicyId))
                    throw new InvalidOperationException("Shipping tag changed after SQL commit.");

                if (job.OriginalPublishedOnOnlineStore)
                {
                    if (!snapshot.PublishedOnOnlineStore || snapshot.Status != "ACTIVE")
                    {
                        // Never restore publication if the membership checks failed.
                        // All checks have passed above.
                        stoppingToken.ThrowIfCancellationRequested();
                        safetyWriteStarted = true;
                        await _shopify.PublishProductForPartAsync(part!);
                    }
                    var published = await _shopify.GetProductShippingSnapshotAsync(job.ShopifyProductId);
                    if (published.Status != "ACTIVE" || !published.PublishedOnOnlineStore ||
                        !HasTag(published, job.ToPolicyId))
                        throw new InvalidOperationException("Shopify publication failed final verification.");
                }
                else
                {
                    if (snapshot.PublishedOnOnlineStore || snapshot.Status != "DRAFT")
                        throw new InvalidOperationException("Original draft publication state was not preserved.");
                }

                if (!await _shopify.CollectionContainsProductAsync(
                        target.ShopifyShippingCollectionGid, productGid) ||
                    await _shopify.CollectionContainsProductAsync(
                        old.ShopifyShippingCollectionGid, productGid))
                    throw new InvalidOperationException("Shipping membership changed after publication.");

                if (!await _store.UpdateAsync(job, owner, "Completed", true, TimeSpan.Zero,
                        false, true, countCheck: true, ct: stoppingToken))
                    throw new InvalidOperationException("Worker lost SQL lease before recording completion.");

                _log.LogInformation("Completed asynchronous shipping job {JobId}: part {PartId} {Old}->{New}.",
                    job.JobId, job.PartId, job.FromPolicyId, job.ToPolicyId);
            }
            catch (Exception e) when (!stoppingToken.IsCancellationRequested)
            {
                _log.LogError(e, "Asynchronous shipping job {JobId} failed during reconciliation.", job.JobId);
                // A job may have changed Shopify in a previous worker pass
                // before a process restart. Even when THIS pass made no writes,
                // never assume the product is safe to remain published.
                bool draftSafe = false;
                if (part?.ShopifyProductId == job.ShopifyProductId)
                {
                    try
                    {
                        await _shopify.UnpublishProductForPartAsync(part);
                        var safety = await _shopify.GetProductShippingSnapshotAsync(job.ShopifyProductId);
                        draftSafe = safety.Status == "DRAFT" && !safety.PublishedOnOnlineStore;
                    }
                    catch (Exception safetyException)
                    {
                        _log.LogCritical(safetyException,
                            "URGENT: Unable to confirm product unpublished for shipping job {JobId}.", job.JobId);
                    }
                }
                string status = !draftSafe || job.FailureCount >= 4 ? "AttentionRequired" : "WaitingForShopify";
                string message = (draftSafe ? "" : "URGENT: publication state could not be verified. ") +
                    e.Message;
                await _store.UpdateAsync(job, owner, status, false,
                    status == "AttentionRequired" ? TimeSpan.Zero : TimeSpan.FromSeconds(45),
                    error: message, countFailure: true, ct: CancellationToken.None);
            }
        }
    }
}
