using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Site_2024.Models.Domain.Parts;
using Site_2024.Web.Api.Interfaces;
using Site_2024.Web.Api.Models;
using Site_2024.Web.Api.Models.User;
using Site_2024.Web.Api.Responses;
using Site_2024.Web.Api.Services;

namespace Site_2024.Web.Api.Controllers
{
    // SQL-backed queue. All writes require explicit store authorization and feature gates.
    [ApiController]
    [Route("api/home/{partId:int}/shopify/shipping-reassignment-job")]
    public sealed class ShopifyShippingJobController : ControllerBase
    {
        private readonly ShopifyShippingJobStore _store;
        private readonly IPartService _parts;
        private readonly IShippingPoliciesService _policies;
        private readonly IShopifyAdminService _shopify;
        private readonly IAuthenticationService<IUserAuthData> _auth;
        private readonly ShopifySettings _settings;
        private readonly ShopifyShippingLegacySelectionVerifier _legacyVerifier;

        public ShopifyShippingJobController(ShopifyShippingJobStore store,
            IPartService parts, IShippingPoliciesService policies,
            IShopifyAdminService shopify, IAuthenticationService<IUserAuthData> auth,
            IOptions<ShopifySettings> settings,
            ShopifyShippingLegacySelectionVerifier legacyVerifier)
        {
            _store = store;
            _parts = parts;
            _policies = policies;
            _shopify = shopify;
            _auth = auth;
            _settings = settings.Value;
            _legacyVerifier = legacyVerifier;
        }

        [HttpGet]
        [Authorize(Policy = "AdminAction")]
        public async Task<ActionResult<ItemResponse<ShopifyShippingReassignmentJob?>>> GetLatest(int partId)
        {
            if (!_settings.AsyncShippingJobsSchemaReady)
                return Ok(new ItemResponse<ShopifyShippingReassignmentJob?> { Item = null });

            var job = await _store.GetLatestAsync(partId, HttpContext.RequestAborted);
            return Ok(new ItemResponse<ShopifyShippingReassignmentJob?> { Item = job });
        }

        [HttpPost]
        [Authorize(Policy = "PartsDelete")]
        public async Task<ActionResult<ItemResponse<ShopifyShippingReassignmentJob>>> Enqueue(
            int partId, [FromBody] ShopifyShippingReassignmentRequest request)
        {
            if (!_settings.AsyncShippingJobsSchemaReady ||
                !_settings.AllowMarketDrivenShippingAsyncReassignmentWrites ||
                !_settings.EnableMarketDrivenShippingAsyncWorker)
                return Conflict(new ErrorResponse(
                    "Asynchronous shipping reassignment is disabled until the production job schema and worker are enabled. No changes made."));
            if (!_settings.IsAsyncShippingStoreAuthorized())
                return Conflict(new ErrorResponse(
                    "Shipping jobs are not authorized for the configured Shopify store. No changes made."));
            if (request == null ||
                request.Confirmation != "REASSIGN_MARKETS_SHIPPING_POLICY" ||
                request.ExpectedCurrentShippingPolicyId <= 0 ||
                request.TargetShippingPolicyId <= 0 ||
                request.ExpectedCurrentShippingPolicyId == request.TargetShippingPolicyId ||
                string.IsNullOrWhiteSpace(request.ExpectedShopifyProductGid))
                return BadRequest(new ErrorResponse("Invalid shipping reassignment confirmation or policies."));

            var user = _auth.GetCurrentUser();
            if (user == null)
                return Unauthorized(new ErrorResponse("Sign in as AdminHigh."));

            try
            {
                var part = _parts.GetPartById(partId);
                if (part?.ShopifyProductId.HasValue != true ||
                    !part.ShopifyVariantId.HasValue || !part.ShopifyInventoryItemId.HasValue)
                    return Conflict(new ErrorResponse("Part or Shopify mapping is incomplete."));
                string productGid = $"gid://shopify/Product/{part.ShopifyProductId.Value}";
                if (part.ShippingPolicy?.Id != request.ExpectedCurrentShippingPolicyId ||
                    productGid != request.ExpectedShopifyProductGid)
                    return Conflict(new ErrorResponse("Part changed; refresh before requesting the shipping update."));
                if (!_settings.AllowMarketDrivenShippingPublishing ||
                    !await _shopify.UsesMarketDrivenShippingAsync())
                    return Conflict(new ErrorResponse("Market-driven Shipping publishing is not configured."));
                var policies = _policies.GetAll() ?? new List<ShippingPolicy>();
                var old = policies.FirstOrDefault(p => p.Id == request.ExpectedCurrentShippingPolicyId);
                var target = policies.FirstOrDefault(p => p.Id == request.TargetShippingPolicyId &&
                    p.IsActive && p.AllowsOnlineCheckout);
                if (old?.AllowsOnlineCheckout != true ||
                    target == null ||
                    string.IsNullOrWhiteSpace(old.ShopifyShippingCollectionGid) ||
                    string.IsNullOrWhiteSpace(target.ShopifyShippingCollectionGid))
                    return Conflict(new ErrorResponse("Both policies must have mapped checkout collections."));

                var snapshot = await _shopify.GetProductShippingSnapshotAsync(part.ShopifyProductId.Value);
                if (snapshot.ProductGid != productGid ||
                    snapshot.Status != "ACTIVE" || !snapshot.PublishedOnOnlineStore ||
                    snapshot.ShippingClassTags.Count != 1 ||
                    !string.Equals(snapshot.ShippingClassTags[0],
                        $"ShippingClass_{old.Id}", StringComparison.OrdinalIgnoreCase))
                    return Conflict(new ErrorResponse(
                        "Part must be ACTIVE, published and use its current SQL shipping tag. Inspect an interrupted prior change first."));

                // Reject older migrated products BEFORE locking the part in SQL.
                // Original Shopify VARIANTS selections do not move when the
                // ShippingClass tag changes. An incomplete scan fails closed.
                await _legacyVerifier.RequireTagOnlyMembershipAsync(
                    policies, productGid, HttpContext.RequestAborted);

                ShopifyShippingCollectionInfo? destination =
                    await _shopify.GetShippingCollectionAsync(target.ShopifyShippingCollectionGid);
                bool correctTagRule = destination?.Sources.Any(src =>
                    src.SourceType == "CollectionConditionsSource" &&
                    src.TargetType == "PRODUCTS" &&
                    src.InclusionConditionCount == 1 &&
                    !src.HasExplicitSelections && !src.HasExclusions &&
                    src.TagConditions.Count == 1 &&
                    src.TagConditions[0].Relation == "TAGGED_WITH" &&
                    src.TagConditions[0].MatchType == "ANY" &&
                    src.TagConditions[0].Values.Count == 1 &&
                    string.Equals(src.TagConditions[0].Values[0],
                        $"ShippingClass_{target.Id}", StringComparison.OrdinalIgnoreCase)) == true;
                if (!correctTagRule)
                    return Conflict(new ErrorResponse(
                        "Target Shopify collection does not have the expected automatic shipping tag source."));

                if (!await _shopify.CollectionContainsProductAsync(
                        old.ShopifyShippingCollectionGid, productGid) ||
                    await _shopify.CollectionContainsProductAsync(
                        target.ShopifyShippingCollectionGid, productGid))
                    return Conflict(new ErrorResponse(
                        "Shopify product is not exclusively in its original shipping collection. No changes made."));

                Guid jobId = await _store.EnqueueAsync(partId, part.ShopifyProductId.Value,
                    old.Id, target.Id, user.Id, HttpContext.RequestAborted);
                var job = await _store.GetLatestAsync(partId, HttpContext.RequestAborted);
                if (job?.JobId != jobId)
                    return StatusCode(503, new ErrorResponse(
                        "Job may have been queued but status could not be read; do not submit again."));
                return Accepted(new ItemResponse<ShopifyShippingReassignmentJob> { Item = job });
            }
            catch (InvalidOperationException ex)
            {
                // Only read-only Shopify preflight operations run before EnqueueAsync.
                return Conflict(new ErrorResponse(
                    $"Shipping reassignment safety check failed. No change queued. {ex.Message}"));
            }
            catch (Microsoft.Data.SqlClient.SqlException ex) when (ex.Number is 2601 or 2627 or 51130 or 51131 or 51132 or 51133 or 51134)
            {
                return Conflict(new ErrorResponse(ex.Message));
            }
            catch (Exception ex)
            {
                // No Shopify writes occur from this HTTP endpoint.
                return StatusCode(503, new ErrorResponse(
                    "Could not safely queue shipping update. Inspect job status before retrying. " +
                    ex.GetType().Name));
            }
        }
    }
}
