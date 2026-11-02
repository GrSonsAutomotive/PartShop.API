using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Site_2024.Web.Api.Models;
using Site_2024.Web.Api.Services;
using System.Text;
using System.Text.Json;

namespace Site_2024.Web.Api.Controllers
{
    // This controller is intentionally DEV-BRANCH-ONLY. Every write is constrained
    // to ONE throwaway collection in ONE verified Shopify development store.
    // Never merge this controller to master or generalize to production.
    [ApiController]
    [Route("api/shipping-migration/dev-rehearsal")]
    [Authorize(Policy = "PartsDelete")]
    public sealed class ShopifyDevLegacyRehearsalController : ControllerBase
    {
        private const string DevShop = "gr-sons-shipping-dev.myshopify.com";
        private const string DevAppPrefix = "site-2024-api-dev-";
        private const string CollectionGid = "gid://shopify/Collection/511239127260";
        private const string AutoSourceGid = "gid://shopify/CollectionConditionsSource/54605054172";
        private const string TestTitle = "[SHIPPING] Site_2024 - MIGRATION TEST";
        private const string AutomaticTag = "ShippingClass_21";
        private const string ManualTitle = "[DEV REHEARSAL] Temporary variant source";

        private static readonly SemaphoreSlim MutationGate = new(1, 1);

        private readonly IShopifyAdminService _shopifyAdmin;
        private readonly IShopifyTokenService _tokens;
        private readonly IHttpClientFactory _http;
        private readonly IOptions<ShopifySettings> _options;
        private readonly IConfiguration _config;
        private readonly ILogger<ShopifyDevLegacyRehearsalController> _logger;

        public ShopifyDevLegacyRehearsalController(
            IShopifyAdminService shopifyAdmin,
            IShopifyTokenService tokens,
            IHttpClientFactory http,
            IOptions<ShopifySettings> options,
            IConfiguration config,
            ILogger<ShopifyDevLegacyRehearsalController> logger)
        {
            _shopifyAdmin = shopifyAdmin;
            _tokens = tokens;
            _http = http;
            _options = options;
            _config = config;
            _logger = logger;
        }

        public sealed class RehearsalRequest
        {
            public string Confirmation { get; set; } = string.Empty;
            public string ExpectedProductGid { get; set; } = string.Empty;
            public string ExpectedVariantGid { get; set; } = string.Empty;
            public string ExpectedAutoSourceGid { get; set; } = string.Empty;
            public string? ExpectedManualSourceGid { get; set; }
        }

        private sealed class Snapshot
        {
            public string ProductGid { get; init; } = string.Empty;
            public string VariantGid { get; init; } = string.Empty;
            public string? ManualSourceGid { get; init; }
            public int SourceCount { get; init; }
        }

        private bool IsExactDevAppHost()
        {
            string host = HttpContext.Request.Host.Host.Trim().ToLowerInvariant();
            return host.StartsWith(DevAppPrefix, StringComparison.Ordinal)
                && host.EndsWith(".azurewebsites.net", StringComparison.Ordinal);
        }

        private async Task<JsonDocument> QueryShopifyAsync(
            string query, object? variables, CancellationToken ct)
        {
            // Gate BEFORE acquiring any access token, even for reads.
            string configured = (_options.Value.ShopDomain ?? "").Trim().ToLowerInvariant();
            if (!IsExactDevAppHost() || configured != DevShop)
                throw new InvalidOperationException(
                    "DEV rehearsal API host or configured Shopify store is not isolated.");

            using var request = new HttpRequestMessage(
                HttpMethod.Post, $"https://{DevShop}/admin/api/2026-07/graphql.json");
            request.Headers.Add("X-Shopify-Access-Token", await _tokens.GetAccessTokenAsync());
            request.Content = new StringContent(
                JsonSerializer.Serialize(new { query, variables }), Encoding.UTF8, "application/json");

            using HttpClient client = _http.CreateClient();
            using HttpResponseMessage response = await client.SendAsync(request, ct);
            if (!response.IsSuccessStatusCode)
                throw new InvalidOperationException(
                    $"DEV Shopify API returned HTTP {(int)response.StatusCode}");

            await using var stream = await response.Content.ReadAsStreamAsync(ct);
            JsonDocument doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
            if (doc.RootElement.TryGetProperty("errors", out _))
            {
                doc.Dispose();
                throw new InvalidOperationException("DEV Shopify GraphQL errors.");
            }
            return doc;
        }

        private async Task<Snapshot> GetSnapshotAsync(CancellationToken ct)
        {
            const string query = @"
query SiteDevRehearsalRead($id: ID!) {
  shop { myshopifyDomain }
  collection(id: $id) {
    id
    title
    products(first: 2) {
      nodes {
        id
        tags
        variants(first: 2) {
          nodes { id }
          pageInfo { hasNextPage }
        }
      }
      pageInfo { hasNextPage }
    }
  }
}";
            using JsonDocument doc = await QueryShopifyAsync(
                query, new { id = CollectionGid }, ct);
            JsonElement root = doc.RootElement.GetProperty("data");
            if (root.GetProperty("shop").GetProperty("myshopifyDomain").GetString()
                != DevShop)
                throw new InvalidOperationException("Authenticated Shopify store is NOT DEV.");

            JsonElement collection = root.GetProperty("collection");
            if (collection.ValueKind != JsonValueKind.Object
                || collection.GetProperty("id").GetString() != CollectionGid
                || collection.GetProperty("title").GetString() != TestTitle)
                throw new InvalidOperationException("Not the exact disposable migration test collection.");

            JsonElement products = collection.GetProperty("products");
            JsonElement productNodes = products.GetProperty("nodes");
            if (productNodes.GetArrayLength() != 1 ||
                products.GetProperty("pageInfo").GetProperty("hasNextPage").GetBoolean())
                throw new InvalidOperationException("DEV test collection must have exactly one product.");

            JsonElement product = productNodes[0];
            if (!product.GetProperty("tags").EnumerateArray().Any(t =>
                string.Equals(t.GetString(), AutomaticTag, StringComparison.Ordinal)))
                throw new InvalidOperationException("DEV test product lacks the required automatic tag.");

            JsonElement variants = product.GetProperty("variants");
            JsonElement variantNodes = variants.GetProperty("nodes");
            if (variantNodes.GetArrayLength() != 1 ||
                variants.GetProperty("pageInfo").GetProperty("hasNextPage").GetBoolean())
                throw new InvalidOperationException("DEV test product must have exactly one variant.");

            string productGid = product.GetProperty("id").GetString() ?? "";
            string variantGid = variantNodes[0].GetProperty("id").GetString() ?? "";
            if (!productGid.StartsWith("gid://shopify/Product/", StringComparison.Ordinal)
                || !variantGid.StartsWith("gid://shopify/ProductVariant/", StringComparison.Ordinal))
                throw new InvalidOperationException("Unexpected DEV test product or variant GID.");

            ShopifyShippingCollectionInfo? parsed =
                await _shopifyAdmin.GetShippingCollectionAsync(CollectionGid);
            if (parsed == null || parsed.CollectionGid != CollectionGid
                || parsed.ProductCount != 1 || parsed.Sources.Count is < 1 or > 2)
                throw new InvalidOperationException("DEV test collection topology changed.");

            var auto = parsed.Sources.Where(s =>
                s.SourceGid == AutoSourceGid &&
                s.SourceType == "CollectionConditionsSource" &&
                s.TargetType == "PRODUCTS" &&
                !s.HasExplicitSelections && !s.HasExclusions &&
                s.InclusionConditionCount == 1 && s.TagConditions.Count == 1 &&
                s.TagConditions[0].Relation == "TAGGED_WITH" &&
                s.TagConditions[0].MatchType == "ANY" &&
                s.TagConditions[0].Values.Count == 1 &&
                s.TagConditions[0].Values[0] == AutomaticTag).ToList();
            if (auto.Count != 1)
                throw new InvalidOperationException("DEV test automatic shipping source changed.");

            string? manualGid = null;
            if (parsed.Sources.Count == 2)
            {
                var manual = parsed.Sources.Single(s => s.SourceGid != AutoSourceGid);
                if (manual.SourceType != "CollectionConditionsSource" ||
                    manual.TargetType != "VARIANTS" ||
                    manual.Title != ManualTitle || !manual.HasExplicitSelections ||
                    manual.HasExclusions || manual.InclusionConditionCount != 0)
                    throw new InvalidOperationException("Unexpected additional DEV source.");

                manualGid = manual.SourceGid;
                await VerifyManualSelectionAsync(manualGid, productGid, variantGid, ct);
            }

            // Membership updates may settle asynchronously. For this one-
            // product test collection the existing audit's first-ten window
            // is exhaustive. Require BOTH sources (when present) to resolve
            // to the exact test product before any subsequent mutation.
            ShopifyShippingMembershipAudit? audit =
                await _shopifyAdmin.AuditShippingCollectionMembershipAsync(CollectionGid);
            if (audit == null || audit.CollectionGid != CollectionGid ||
                audit.ProductCount != 1 || audit.MoreCollectionProducts ||
                audit.CollectionProductGids.Count != 1 ||
                audit.CollectionProductGids[0] != productGid ||
                audit.Sources.Count != parsed.Sources.Count)
                throw new InvalidOperationException("DEV collection membership has not settled.");

            foreach (ShopifyShippingSourceMembershipAudit source in audit.Sources)
            {
                if (source.MoreSourceProducts || source.SourceProductGids.Count != 1 ||
                    source.SourceProductGids[0] != productGid)
                    throw new InvalidOperationException("DEV source membership has not settled.");
                if (source.SourceGid == manualGid &&
                    (source.MoreManualSelections ||
                     source.ManualSelectionProductGids.Count != 1 ||
                     source.ManualSelectionProductGids[0] != productGid))
                    throw new InvalidOperationException("DEV manual selections have not settled.");
            }

            return new Snapshot
            {
                ProductGid = productGid,
                VariantGid = variantGid,
                ManualSourceGid = manualGid,
                SourceCount = parsed.Sources.Count
            };
        }

        private async Task VerifyManualSelectionAsync(
            string sourceGid, string productGid, string variantGid, CancellationToken ct)
        {
            const string query = @"
query SiteDevVariantSourceSelection($id: ID!) {
  node(id: $id) {
    ... on CollectionConditionsSource {
      id
      inclusion {
        selections(first: 2) {
          nodes {
            product { id }
            variantIds
          }
          pageInfo { hasNextPage }
        }
      }
    }
  }
}";
            using JsonDocument doc = await QueryShopifyAsync(query, new { id = sourceGid }, ct);
            JsonElement node = doc.RootElement.GetProperty("data").GetProperty("node");
            if (node.ValueKind != JsonValueKind.Object ||
                node.GetProperty("id").GetString() != sourceGid)
                throw new InvalidOperationException("DEV manual source disappeared.");
            JsonElement selections = node.GetProperty("inclusion").GetProperty("selections");
            JsonElement rows = selections.GetProperty("nodes");
            if (rows.GetArrayLength() != 1 ||
                selections.GetProperty("pageInfo").GetProperty("hasNextPage").GetBoolean() ||
                rows[0].GetProperty("product").GetProperty("id").GetString() != productGid)
                throw new InvalidOperationException("DEV manual source has unexpected products.");
            JsonElement variantIds = rows[0].GetProperty("variantIds");
            if (variantIds.ValueKind != JsonValueKind.Array ||
                variantIds.GetArrayLength() != 1 ||
                variantIds[0].GetString() != variantGid)
                throw new InvalidOperationException("DEV manual source selects the wrong variant.");
        }

        [HttpGet("preflight")]
        public async Task<IActionResult> Preflight(CancellationToken ct)
        {
            try
            {
                Snapshot snapshot = await GetSnapshotAsync(ct);
                return Ok(new
                {
                    readOnly = true,
                    isolatedDevStoreVerified = true,
                    devShop = DevShop,
                    collectionGid = CollectionGid,
                    productGid = snapshot.ProductGid,
                    variantGid = snapshot.VariantGid,
                    automaticSourceGid = AutoSourceGid,
                    manualSourceGid = snapshot.ManualSourceGid,
                    sourceCount = snapshot.SourceCount,
                    devRehearsalWritesEnabled =
                        _config.GetValue<bool>("ShopifySettings:AllowDevLegacySourceRehearsal"),
                    nextStep = snapshot.ManualSourceGid == null
                        ? "Use guarded create or restore only after review."
                        : "Use guarded delete only after saving a recovery manifest."
                });
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "DEV legacy source preflight failed.");
                return StatusCode(409, new { readOnly = true, safe = false,
                    message = "DEV source rehearsal preflight failed. No changes made." });
            }
        }

        // Only three operations can mutate, and each has a different
        // confirmation; no arbitrary GIDs, titles or mutations are accepted.
        [HttpPost("{stage}")]
        public async Task<IActionResult> Mutate(
            string stage, [FromBody] RehearsalRequest request, CancellationToken ct)
        {
            const string createConfirmation = "DEV_CREATE_TEMP_VARIANT_SOURCE";
            const string deleteConfirmation = "DEV_DELETE_TEMP_VARIANT_SOURCE";
            const string restoreConfirmation = "DEV_RESTORE_TEMP_VARIANT_SOURCE";
            string expectedConfirmation = stage switch
            {
                "create" => createConfirmation,
                "delete" => deleteConfirmation,
                "restore" => restoreConfirmation,
                _ => ""
            };
            if (string.IsNullOrEmpty(expectedConfirmation)
                || request == null || request.Confirmation != expectedConfirmation)
                return BadRequest("Incorrect DEV stage or confirmation.");

            if (!_config.GetValue<bool>("ShopifySettings:AllowDevLegacySourceRehearsal"))
                return StatusCode(403, "DEV shipping source rehearsal writes are disabled.");

            if (!await MutationGate.WaitAsync(TimeSpan.FromSeconds(5), ct))
                return Conflict("Another DEV rehearsal mutation is running.");

            try
            {
                Snapshot before = await GetSnapshotAsync(ct);
                if (request.ExpectedProductGid != before.ProductGid ||
                    request.ExpectedVariantGid != before.VariantGid ||
                    request.ExpectedAutoSourceGid != AutoSourceGid)
                    return Conflict("DEV product, variant or automatic source changed.");

                bool deleting = stage == "delete";
                if (deleting)
                {
                    if (before.SourceCount != 2 || before.ManualSourceGid == null ||
                        request.ExpectedManualSourceGid != before.ManualSourceGid)
                        return Conflict("Exact temporary variant source is missing or changed.");
                }
                else if (before.SourceCount != 1 || before.ManualSourceGid != null)
                    return Conflict("Cannot add a manual source when one already exists.");

                const string mutation = @"
mutation SiteDevOnlyRehearseCollectionSource($collection: CollectionUpdateInput!) {
  collectionUpdate(collection: $collection) {
    collection {
      id
      sources { id title }
    }
    job { id done }
    userErrors { field message }
  }
}";
                object update = deleting
                    ? new { id = CollectionGid, sourcesToDelete = new[] { before.ManualSourceGid! } }
                    : new
                    {
                        id = CollectionGid,
                        sourcesToCreate = new[]
                        {
                            new
                            {
                                source = new
                                {
                                    title = ManualTitle,
                                    targetType = "VARIANTS",
                                    inclusion = new
                                    {
                                        selections = new[]
                                        {
                                            new
                                            {
                                                productId = before.ProductGid,
                                                variantIds = new[] { before.VariantGid }
                                            }
                                        }
                                    }
                                }
                            }
                        }
                    };

                // This is the ONLY call that can change Shopify; all target
                // identifiers are verified before use and fixed to DEV.
                using JsonDocument doc = await QueryShopifyAsync(
                    mutation, new { collection = update }, ct);
                JsonElement payload = doc.RootElement.GetProperty("data")
                    .GetProperty("collectionUpdate");
                JsonElement errors = payload.GetProperty("userErrors");
                if (errors.GetArrayLength() != 0)
                    throw new InvalidOperationException("Shopify rejected DEV collection source mutation.");

                JsonElement updated = payload.GetProperty("collection");
                if (updated.ValueKind != JsonValueKind.Object ||
                    updated.GetProperty("id").GetString() != CollectionGid)
                    throw new InvalidOperationException("DEV collection mutation returned no valid collection.");

                var sourceIds = updated.GetProperty("sources").EnumerateArray()
                    .Select(s => s.GetProperty("id").GetString() ?? "").ToList();
                if (sourceIds.Count != (deleting ? 1 : 2) ||
                    !sourceIds.Contains(AutoSourceGid) ||
                    (deleting && sourceIds.Contains(before.ManualSourceGid!)))
                    throw new InvalidOperationException(
                        "DEV mutation response source topology was unexpected; inspect Shopify before retrying.");

                string? newManualGid = deleting ? null :
                    sourceIds.Single(id => id != AutoSourceGid);
                string? jobGid = null;
                bool? jobDone = null;
                if (payload.TryGetProperty("job", out JsonElement job)
                    && job.ValueKind == JsonValueKind.Object)
                {
                    jobGid = job.GetProperty("id").GetString();
                    jobDone = job.GetProperty("done").GetBoolean();
                }

                return Ok(new
                {
                    devOnly = true,
                    stage,
                    mutationAccepted = true,
                    devShop = DevShop,
                    collectionGid = CollectionGid,
                    productGid = before.ProductGid,
                    variantGid = before.VariantGid,
                    preservedAutomaticSourceGid = AutoSourceGid,
                    previousManualSourceGid = before.ManualSourceGid,
                    currentManualSourceGid = newManualGid,
                    sourceGids = sourceIds,
                    jobGid,
                    jobDone,
                    message = "Verify settled Shopify collection/source membership using GET preflight. " +
                        "Never blindly retry an uncertain mutation."
                });
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
            catch (Exception ex)
            {
                _logger.LogError(ex, "DEV shipping legacy source rehearsal {Stage} had an uncertain outcome.", stage);
                return StatusCode(502, new
                {
                    devOnly = true,
                    stage,
                    outcomeUncertain = true,
                    message = "Recheck DEV source preflight before any further action. " +
                        "Shopify may already have applied the mutation. Never blindly retry."
                });
            }
            finally
            {
                MutationGate.Release();
            }
        }
    }
}
