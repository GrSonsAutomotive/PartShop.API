using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Options;
using Site_2024.Web.Api.Models;
using Site_2024.Web.Api.Services;
using System.Data;

namespace Site_2024.Web.Api.Controllers
{
    // Shipping-class migration is gated separately from the live shipping worker.
    // Idempotent PartId-cursor batches; never use the content-rewriting legacy backfill.
    [ApiController]
    [Route("api/shipping-migration/tags")]
    public sealed class ShippingClassTagBackfillController : ControllerBase
    {
        private readonly IConfiguration _config;
        private readonly IPartService _parts;
        private readonly IShopifyAdminService _shopify;
        private readonly IShippingPoliciesService _policies;
        private readonly ShopifyShippingJobStore _jobs;
        private readonly ShopifySettings _settings;

        public ShippingClassTagBackfillController(IConfiguration config, IPartService parts,
            IShopifyAdminService shopify, IShippingPoliciesService policies,
            ShopifyShippingJobStore jobs, IOptions<ShopifySettings> settings)
        {
            _config = config; _parts = parts; _shopify = shopify;
            _policies = policies; _jobs = jobs; _settings = settings.Value;
        }

        public sealed class ExpectedPart
        {
            public int PartId { get; set; }
            public long ShopifyProductId { get; set; }
        }

        public sealed class BatchRequest
        {
            public int ExpectedPolicyId { get; set; }
            public string ExpectedCollectionGid { get; set; } = "";
            public string ExpectedTagSourceGid { get; set; } = "";
            public int ExpectedCollectionProductCount { get; set; } = -1;
            public List<ExpectedPart> ExpectedParts { get; set; } = new();
            public int AfterPartId { get; set; }
            public int Limit { get; set; } = 10;
            public int ExpectedFirstPartId { get; set; }
            public int ExpectedLastPartId { get; set; }
            public string ExpectedShopDomain { get; set; } = "";
            public string Confirmation { get; set; } = "";
        }

        private static bool Valid(int after, int limit) => after >= 0 && limit is >= 1 and <= 20;

        private async Task<List<(int Id, long ProductId, int PolicyId)>> ReadBatch(
            int after, int limit, CancellationToken ct)
        {
            var rows = new List<(int Id, long ProductId, int PolicyId)>();
            await using var db = new SqlConnection(_config.GetConnectionString("connMSSQL")
                ?? throw new InvalidOperationException("Missing SQL connection."));
            await db.OpenAsync(ct);
            await using var cmd = new SqlCommand(@"
SELECT TOP (@limit) Id, ShopifyProductId, ShippingPolicyId
FROM dbo.Parts WHERE Id > @after AND ShopifyProductId IS NOT NULL
ORDER BY Id;", db);
            cmd.Parameters.Add("@limit", SqlDbType.Int).Value = limit;
            cmd.Parameters.Add("@after", SqlDbType.Int).Value = after;
            await using var reader = await cmd.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
                rows.Add((reader.GetInt32(0), reader.GetInt64(1), reader.GetInt32(2)));
            return rows;
        }

        [HttpGet("preview")]
        [Authorize(Policy = "AdminAction")]
        public async Task<IActionResult> Preview(int afterPartId = 0, int limit = 10)
        {
            if (!Valid(afterPartId, limit)) return BadRequest("Invalid cursor or batch size (max 20).");
            var rows = await ReadBatch(afterPartId, limit, HttpContext.RequestAborted);
            return Ok(new {
                readOnly = true, shopDomain = _settings.ShopDomain, afterPartId,
                count = rows.Count,
                firstPartId = rows.Count == 0 ? (int?)null : rows[0].Id,
                lastPartId = rows.Count == 0 ? (int?)null : rows[^1].Id,
                items = rows.Select(x => new { partId=x.Id, x.ProductId, x.PolicyId })
            });
        }

        [HttpPost("apply-batch")]
        [Authorize(Policy = "PartsDelete")]
        public async Task<IActionResult> ApplyBatch([FromBody] BatchRequest request)
        {
            if (request == null || !Valid(request.AfterPartId, request.Limit) ||
                request.Confirmation != "BACKFILL_SHIPPING_CLASS_TAGS" ||
                request.ExpectedPolicyId <= 0 ||
                string.IsNullOrWhiteSpace(request.ExpectedCollectionGid) ||
                string.IsNullOrWhiteSpace(request.ExpectedTagSourceGid) ||
                request.ExpectedCollectionProductCount < 1 ||
                request.ExpectedParts == null ||
                request.ExpectedParts.Count != request.Limit ||
                request.ExpectedParts.Any(x => x.PartId <= 0 || x.ShopifyProductId <= 0) ||
                request.ExpectedParts.Select(x => x.PartId).Distinct().Count() != request.ExpectedParts.Count)
                return BadRequest("Exact expected part IDs, product IDs, policy, Shopify source, collection count, preview and confirmation are required.");
            if (!_settings.AllowMarketDrivenShippingTagBackfillWrites ||
                !_settings.IsShippingTagBackfillStoreAuthorized() ||
                !string.Equals(_settings.ShopDomain, request.ExpectedShopDomain,
                    StringComparison.OrdinalIgnoreCase))
                return Conflict("Shipping-class migration writes are not authorized.");
            if (!await _shopify.UsesMarketDrivenShippingAsync())
                return Conflict("Shopify Markets shipping is not active.");

            var checkout = _policies.GetAll().Where(x => x.IsActive && x.AllowsOnlineCheckout).ToList();
            if (checkout.Count == 0 ||
                checkout.Any(x => string.IsNullOrWhiteSpace(x.ShopifyShippingCollectionGid)) ||
                checkout.GroupBy(x => x.ShopifyShippingCollectionGid).Any(g => g.Count() != 1))
                return Conflict("Shipping policy collection mappings are incomplete or duplicated.");

            var rows = await ReadBatch(request.AfterPartId, request.Limit, HttpContext.RequestAborted);
            if (rows.Count == 0 || rows.Count != request.ExpectedParts.Count ||
                rows[0].Id != request.ExpectedFirstPartId ||
                rows[^1].Id != request.ExpectedLastPartId )
                return Conflict("The preview batch changed; preview again before applying.");

            // Ensure EVERY cursor-selected row is exactly the expected product and policy.
            // The PartId cursor is not policy-scoped, so checking only its endpoints
            // could silently modify an unintended shipping group.
            for (int i = 0; i < rows.Count; i++)
            {
                if (rows[i].Id != request.ExpectedParts[i].PartId ||
                    rows[i].ProductId != request.ExpectedParts[i].ShopifyProductId ||
                    rows[i].PolicyId != request.ExpectedPolicyId)
                    return Conflict("SQL batch differs from the exact preview; no tags changed.");
            }

            var policy = checkout.SingleOrDefault(x => x.Id == request.ExpectedPolicyId);
            if (policy == null ||
                !string.Equals(policy.ShopifyShippingCollectionGid, request.ExpectedCollectionGid,
                    StringComparison.Ordinal))
                return Conflict("Policy-to-collection mapping changed; no tags changed.");

            var collection = await _shopify.GetShippingCollectionAsync(request.ExpectedCollectionGid);
            if (collection == null ||
                collection.ProductCount != request.ExpectedCollectionProductCount ||
                collection.Sources.Count(x => x.SourceGid == request.ExpectedTagSourceGid &&
                    x.SourceType == "CollectionConditionsSource" &&
                    x.TargetType == "PRODUCTS" &&
                    !x.HasExplicitSelections &&
                    !x.HasExclusions &&
                    x.InclusionConditionCount == 1 &&
                    x.TagConditions.Count == 1 &&
                    x.TagConditions[0].Relation == "TAGGED_WITH" &&
                    x.TagConditions[0].MatchType == "ANY" &&
                    x.TagConditions[0].Values.Count == 1 &&
                    string.Equals(x.TagConditions[0].Values[0],
                        $"ShippingClass_{request.ExpectedPolicyId}",
                        StringComparison.OrdinalIgnoreCase)) != 1)
                return Conflict("Automatic tag source or collection count changed; no tags changed.");

            int changed=0, unchanged=0;
            var errors = new List<object>();
            foreach (var row in rows)
            {
                try
                {
                    if (await _jobs.HasOpenAsync(row.Id, HttpContext.RequestAborted))
                        throw new InvalidOperationException("Shipping update job is open.");
                    var part = _parts.GetPartById(row.Id);
                    if (part?.ShopifyProductId != row.ProductId ||
                        part.ShippingPolicy?.Id != row.PolicyId)
                        throw new InvalidOperationException("SQL part changed after preview.");
                    if (await _shopify.SyncOnlyShippingClassTagForPartAsync(part)) changed++;
                    else unchanged++;
                }
                catch (Exception ex)
                {
                    errors.Add(new { partId=row.Id, reason=ex.Message });
                }
            }
            return Ok(new {
                afterPartId=request.AfterPartId, lastPartId=rows[^1].Id,
                changed, unchanged, failed=errors.Count, errors,
                advanceCursorOnlyIfAllSucceeded=true
            });
        }
    }
}
