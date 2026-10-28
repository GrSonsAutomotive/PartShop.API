using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using Site_2024.Web.Api.Models;
using Site_2024.Web.Api.Services;
using System.Data;

namespace Site_2024.Web.Api.Controllers
{
    // READ ONLY. Preparing a source for retirement is NOT permission to delete it.
    // Captures SQL product/variant IDs needed for a separate recovery plan.
    [ApiController]
    [Route("api/shipping-migration/legacy")]
    public sealed class ShippingLegacyRetirementReadinessController : ControllerBase
    {
        private readonly IConfiguration _config;
        private readonly IShopifyAdminService _shopify;
        private readonly IShippingPoliciesService _policies;

        public ShippingLegacyRetirementReadinessController(
            IConfiguration config, IShopifyAdminService shopify, IShippingPoliciesService policies)
        {
            _config = config;
            _shopify = shopify;
            _policies = policies;
        }

        public sealed class PartReference
        {
            public int PartId { get; set; }
            public long ShopifyProductId { get; set; }
            public long? ShopifyVariantId { get; set; }
        }

        public sealed class ReadinessResult
        {
            public bool ReadOnly { get; set; } = true;
            public bool Ready { get; set; }
            public int PolicyId { get; set; }
            public string CollectionGid { get; set; } = "";
            public string OriginalVariantSourceGid { get; set; } = "";
            public string AutomaticProductSourceGid { get; set; } = "";
            public int SqlLinkedProducts { get; set; }
            public int CollectionProducts { get; set; }
            public int ManualSourceProducts { get; set; }
            public int ManualSelections { get; set; }
            public int AutomaticSourceProducts { get; set; }
            public List<string> Problems { get; set; } = new();
            public List<PartReference> RecoveryReference { get; set; } = new();
        }

        private async Task<List<PartReference>> ReadSqlProductsAsync(int policyId, CancellationToken ct)
        {
            var result = new List<PartReference>();
            await using var db = new SqlConnection(_config.GetConnectionString("connMSSQL")
                ?? throw new InvalidOperationException("SQL configuration is missing."));
            await db.OpenAsync(ct);
            await using var cmd = new SqlCommand(@"
SELECT Id, ShopifyProductId, ShopifyVariantId
FROM dbo.Parts
WHERE ShippingPolicyId=@policyId AND ShopifyProductId IS NOT NULL
ORDER BY Id;", db);
            cmd.Parameters.Add("@policyId", SqlDbType.Int).Value = policyId;
            await using var reader = await cmd.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
                result.Add(new PartReference
                {
                    PartId = reader.GetInt32(0),
                    ShopifyProductId = reader.GetInt64(1),
                    ShopifyVariantId = reader.IsDBNull(2) ? null : reader.GetInt64(2)
                });
            return result;
        }

        private async Task<HashSet<string>> ReadAllAsync(
            string collectionGid, string kind, string? sourceGid, CancellationToken ct)
        {
            var members = new HashSet<string>(StringComparer.Ordinal);
            var usedCursors = new HashSet<string>(StringComparer.Ordinal);
            string? cursor = null;
            for (int page = 0; page < 30; page++)
            {
                ct.ThrowIfCancellationRequested();
                ShopifyShippingMembershipPage? result = await _shopify.GetShippingMembershipPageAsync(
                    collectionGid, sourceGid, kind, cursor);
                if (result == null || !result.ReadOnly || result.CollectionGid != collectionGid
                    || result.Kind != kind || result.SourceGid != sourceGid)
                    throw new InvalidOperationException("Unexpected Shopify audit page identity.");

                foreach (string gid in result.ProductGids)
                    if (!members.Add(gid))
                        throw new InvalidOperationException("Duplicate product in Shopify membership pagination.");

                if (!result.HasNextPage)
                    return members;
                if (string.IsNullOrWhiteSpace(result.EndCursor)
                    || !usedCursors.Add(result.EndCursor))
                    throw new InvalidOperationException("Invalid Shopify pagination cursor.");
                cursor = result.EndCursor;
            }

            throw new InvalidOperationException("Shopify membership exceeded bounded audit pagination.");
        }

        [HttpGet("retirement-plan/{policyId:int}")]
        [Authorize(Policy = "PartsDelete")]
        public async Task<IActionResult> RetirementPlan(int policyId, CancellationToken ct)
        {
            ShippingPolicy? policy = (_policies.GetAll() ?? new())
                .FirstOrDefault(p => p.Id == policyId && p.IsActive && p.AllowsOnlineCheckout);
            if (policy == null || string.IsNullOrWhiteSpace(policy.ShopifyShippingCollectionGid))
                return NotFound("Active mapped checkout shipping policy not found.");

            var report = new ReadinessResult
            {
                PolicyId = policyId,
                CollectionGid = policy.ShopifyShippingCollectionGid
            };

            // No source changes, tagsAdd, SQL writes or publishing operations exist here.
            try
            {
                report.RecoveryReference = await ReadSqlProductsAsync(policyId, ct);
                report.SqlLinkedProducts = report.RecoveryReference.Count;

                var expected = report.RecoveryReference.Select(p =>
                    $"gid://shopify/Product/{p.ShopifyProductId}")
                    .ToHashSet(StringComparer.Ordinal);

                if (expected.Count != report.SqlLinkedProducts
                    || report.RecoveryReference.Any(x => !x.ShopifyVariantId.HasValue))
                    report.Problems.Add("SQL product/variant IDs are incomplete or duplicated.");

                ShopifyShippingCollectionInfo? collection =
                    await _shopify.GetShippingCollectionAsync(report.CollectionGid);
                if (collection == null)
                    return NotFound("Mapped Shopify collection not found.");

                report.CollectionProducts = collection.ProductCount;

                var manual = collection.Sources.Where(s =>
                    s.SourceType == "CollectionConditionsSource" &&
                    s.TargetType == "VARIANTS" &&
                    s.HasExplicitSelections &&
                    !s.HasExclusions &&
                    s.InclusionConditionCount == 0).ToList();
                var automated = collection.Sources.Where(s =>
                    s.SourceType == "CollectionConditionsSource" &&
                    s.TargetType == "PRODUCTS" &&
                    !s.HasExplicitSelections &&
                    !s.HasExclusions &&
                    s.InclusionConditionCount == 1 &&
                    s.TagConditions.Count == 1 &&
                    s.TagConditions[0].Relation == "TAGGED_WITH" &&
                    s.TagConditions[0].MatchType == "ANY" &&
                    s.TagConditions[0].Values.Count == 1 &&
                    string.Equals(s.TagConditions[0].Values[0],
                        $"ShippingClass_{policyId}", StringComparison.OrdinalIgnoreCase)).ToList();

                if (collection.Sources.Count != 2 || manual.Count != 1 || automated.Count != 1)
                {
                    report.Problems.Add("Collection does not have exactly one migrated variant and one automatic tag source.");
                    return Ok(report);
                }

                report.OriginalVariantSourceGid = manual[0].SourceGid;
                report.AutomaticProductSourceGid = automated[0].SourceGid;

                var collectionIds = await ReadAllAsync(report.CollectionGid, "collection", null, ct);
                var manualIds = await ReadAllAsync(report.CollectionGid, "source", report.OriginalVariantSourceGid, ct);
                var selectionIds = await ReadAllAsync(report.CollectionGid, "selections", report.OriginalVariantSourceGid, ct);
                var automaticIds = await ReadAllAsync(report.CollectionGid, "source", report.AutomaticProductSourceGid, ct);

                report.ManualSourceProducts = manualIds.Count;
                report.ManualSelections = selectionIds.Count;
                report.AutomaticSourceProducts = automaticIds.Count;

                if (report.CollectionProducts != expected.Count
                    || !collectionIds.SetEquals(expected)
                    || !manualIds.SetEquals(expected)
                    || !selectionIds.SetEquals(expected)
                    || !automaticIds.SetEquals(expected))
                    report.Problems.Add("Full Shopify product membership differs from the SQL policy.");

                // Re-read SQL and collection identities after the paginated scans.
                var sqlAfter = await ReadSqlProductsAsync(policyId, ct);
                if (sqlAfter.Count != report.RecoveryReference.Count ||
                    !sqlAfter.Zip(report.RecoveryReference).All(pair =>
                        pair.First.PartId == pair.Second.PartId &&
                        pair.First.ShopifyProductId == pair.Second.ShopifyProductId &&
                        pair.First.ShopifyVariantId == pair.Second.ShopifyVariantId))
                    report.Problems.Add("SQL product identity changed during the audit.");

                var confirm = await _shopify.GetShippingCollectionAsync(report.CollectionGid);
                if (confirm == null || confirm.ProductCount != collection.ProductCount ||
                    !confirm.Sources.Select(s => s.SourceGid).OrderBy(x => x)
                        .SequenceEqual(collection.Sources.Select(s => s.SourceGid).OrderBy(x => x)))
                    report.Problems.Add("Shopify collection changed during the audit.");

                report.Ready = report.Problems.Count == 0;
                return Ok(report);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception)
            {
                // Fail closed; do not return partial readiness or expose SQL secrets.
                return StatusCode(502,
                    "Read-only source-retirement verification failed. No changes were made.");
            }
        }
    }
}
