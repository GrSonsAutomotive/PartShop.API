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

        public sealed class BatchRequest
        {
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
                request.Confirmation != "BACKFILL_SHIPPING_CLASS_TAGS")
                return BadRequest("Explicit preview and confirmation required.");
            if (!_settings.AllowMarketDrivenShippingTagBackfillWrites ||
                !_settings.IsShippingTagBackfillStoreAuthorized() ||
                !_settings.AllowMarketDrivenShippingPublishing ||
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
            if (rows.Count == 0 || rows[0].Id != request.ExpectedFirstPartId ||
                rows[^1].Id != request.ExpectedLastPartId)
                return Conflict("The preview batch changed; preview again before applying.");

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
