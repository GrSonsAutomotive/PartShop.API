using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Site_2024.Web.Api.Models;
using Site_2024.Web.Api.Services;
using System.Data;
using System.Data.SqlClient;

namespace Site_2024.Web.Api.Controllers
{
    // Read-only: exposes just enough SQL state for a human-reviewed,
    // resumable pending-publication run. NEVER publishes from this endpoint.
    [ApiController]
    [Route("api/home/shopify/pending")]
    public sealed class PendingShopifyPublicationController : ControllerBase
    {
        private readonly IConfiguration _configuration;
        private readonly IAvailableService _availability;
        private readonly IShippingPoliciesService _policies;
        private readonly ILogger<PendingShopifyPublicationController> _logger;

        public PendingShopifyPublicationController(
            IConfiguration configuration,
            IAvailableService availability,
            IShippingPoliciesService policies,
            ILogger<PendingShopifyPublicationController> logger)
        {
            _configuration = configuration;
            _availability = availability;
            _policies = policies;
            _logger = logger;
        }

        public sealed class PendingPart
        {
            public int PartId { get; set; }
            public string Name { get; set; } = string.Empty;
            public string PartNumber { get; set; } = string.Empty;
            public int ShippingPolicyId { get; set; }
            public int Quantity { get; set; }
            public long? ShopifyProductId { get; set; }
            public long? ShopifyVariantId { get; set; }
            public long? ShopifyInventoryItemId { get; set; }
            public DateTime DateCreated { get; set; }
            public bool Eligible { get; set; }
            public string Reason { get; set; } = string.Empty;
        }

        public sealed class PendingPreview
        {
            public bool ReadOnly { get; set; } = true;
            public int AfterPartId { get; set; }
            public int Limit { get; set; }
            public bool HasMore { get; set; }
            public int? NextAfterPartId { get; set; }
            public List<PendingPart> Items { get; set; } = new();
        }

        [HttpGet("preview")]
        [Authorize(Policy = "AdminAction")]
        public async Task<IActionResult> Preview(
            [FromQuery] int afterPartId = 0,
            [FromQuery] int limit = 20,
            CancellationToken cancellationToken = default)
        {
            if (afterPartId < 0 || limit < 1 || limit > 50)
                return BadRequest("afterPartId must be nonnegative and limit must be 1-50.");

            try
            {
                Available? pending = (_availability.GetAll() ?? new())
                    .FirstOrDefault(x => string.Equals(
                        x.Status?.Trim(), "Pending", StringComparison.OrdinalIgnoreCase));
                if (pending == null)
                    return StatusCode(503, "Pending inventory status is not configured.");

                var policies = (_policies.GetAll() ?? new())
                    .ToDictionary(x => x.Id);

                var page = new PendingPreview { AfterPartId = afterPartId, Limit = limit };
                await using var connection = new SqlConnection(
                    _configuration.GetConnectionString("connMSSQL")
                    ?? throw new InvalidOperationException("SQL connection is not configured."));
                await connection.OpenAsync(cancellationToken);

                // These are local SQL records only. A successful preview is NOT
                // permission to publish: the existing per-part endpoint rechecks
                // current SQL status, tag, Shopify membership and publication.
                await using var cmd = new SqlCommand(@"
SELECT TOP (@top) Id, Name, PartNumber, ShippingPolicyId, Quantity,
    ShopifyProductId, ShopifyVariantId, ShopifyInventoryItemId, DateCreated
FROM dbo.Parts
WHERE AvailableId = @pending AND Id > @after
ORDER BY Id;", connection);
                cmd.Parameters.Add("@top", SqlDbType.Int).Value = limit + 1;
                cmd.Parameters.Add("@pending", SqlDbType.Int).Value = pending.Id;
                cmd.Parameters.Add("@after", SqlDbType.Int).Value = afterPartId;

                await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
                while (await reader.ReadAsync(cancellationToken))
                {
                    var part = new PendingPart
                    {
                        PartId = reader.GetInt32(0),
                        Name = reader.IsDBNull(1) ? "" : reader.GetString(1),
                        PartNumber = reader.IsDBNull(2) ? "" : reader.GetString(2),
                        ShippingPolicyId = reader.GetInt32(3),
                        Quantity = reader.IsDBNull(4) ? 0 : reader.GetInt32(4),
                        ShopifyProductId = reader.IsDBNull(5) ? null : reader.GetInt64(5),
                        ShopifyVariantId = reader.IsDBNull(6) ? null : reader.GetInt64(6),
                        ShopifyInventoryItemId = reader.IsDBNull(7) ? null : reader.GetInt64(7),
                        DateCreated = reader.IsDBNull(8) ? DateTime.MinValue : reader.GetDateTime(8)
                    };

                    if (!policies.TryGetValue(part.ShippingPolicyId, out ShippingPolicy? policy)
                        || !policy.IsActive || !policy.AllowsOnlineCheckout)
                        part.Reason = "Policy is not an active checkout policy.";
                    else if (part.ShopifyProductId.GetValueOrDefault() <= 0 ||
                        part.ShopifyVariantId.GetValueOrDefault() <= 0 ||
                        part.ShopifyInventoryItemId.GetValueOrDefault() <= 0)
                        part.Reason = "Missing Shopify IDs. Review individually; never recreate blindly.";
                    else if (part.Quantity <= 0)
                        part.Reason = "No stock available for customer checkout.";
                    else
                    {
                        part.Eligible = true;
                        part.Reason = "Pending; eligible for individually verified publishing.";
                    }

                    page.Items.Add(part);
                }

                page.HasMore = page.Items.Count > limit;
                if (page.HasMore)
                    page.Items.RemoveAt(page.Items.Count - 1);
                page.NextAfterPartId = page.HasMore && page.Items.Count > 0
                    ? page.Items[^1].PartId : null;
                return Ok(page);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed read-only pending-part publication preview.");
                return StatusCode(502, "Unable to inspect pending inventory. No changes were made.");
            }
        }
    }
}
