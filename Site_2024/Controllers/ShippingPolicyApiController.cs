using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Site_2024.Web.Api.Interfaces;
using Site_2024.Web.Api.Models;
using Site_2024.Web.Api.Models.User;
using Site_2024.Web.Api.Requests.ShippingPolicies;
using Site_2024.Web.Api.Responses;
using Site_2024.Web.Api.Services;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Site_2024.Web.Api.Controllers
{
    [Route("api/shippingpolicies")]
    [ApiController]
    public class ShippingPoliciesApiController : BaseApiController
    {
        private readonly IShippingPoliciesService _service;
        private readonly IAuthenticationService<IUserAuthData> _authService;

        public ShippingPoliciesApiController(IShippingPoliciesService service, IAuthenticationService<IUserAuthData> authService, ILogger<ShippingPoliciesApiController> logger)
            : base(logger)
        {
            _service = service;
            _authService = authService;
        }

        [HttpGet("all")]
        [AllowAnonymous] // or [Authorize] if you want it locked down
        public ActionResult<ItemResponse<List<ShippingPolicy>>> GetAll()
        {
            int code = 200;
            BaseResponse response = null;

            try
            {
                List<ShippingPolicy> list = _service.GetAll();

                if (list == null || list.Count == 0)
                {
                    code = 404;
                    response = new ErrorResponse("No shipping policies found.");
                }
                else
                {
                    response = new ItemResponse<List<ShippingPolicy>> { Item = list };
                }
            }
            catch (Exception ex)
            {
                code = 500;
                base.Logger.LogError(ex.ToString());
                response = new ErrorResponse(ex.Message);
            }

            return StatusCode(code, response);
        }

        // Uses only local SQL. Safe to call before connecting a Shopify test store.
        [HttpGet("shopify/markets-plan")]
        [Authorize(Policy = "AdminAction")]
        public ActionResult<ItemResponse<object>> GetMarketsShippingPlan()
        {
            List<ShippingPolicy> policies = _service.GetAll() ?? new List<ShippingPolicy>();

            var checkoutPolicies = policies.Where(policy => policy.AllowsOnlineCheckout).ToArray();
            var response = new
            {
                TotalActivePolicies = policies.Count,
                CheckoutEnabledPolicies = checkoutPolicies.Length,
                MissingCollectionMappings = checkoutPolicies.Count(
                    policy => string.IsNullOrWhiteSpace(policy.ShopifyShippingCollectionGid)),
                Policies = policies.Select(policy => new
                {
                    policy.Id,
                    policy.Name,
                    policy.AllowsOnlineCheckout,
                    policy.ShopifyProfileId,
                    policy.ShopifyShippingCollectionGid,
                    ShippingClassTag = policy.AllowsOnlineCheckout
                        ? $"ShippingClass_{policy.Id}"
                        : null
                }).ToArray()
            };

            return Ok(new ItemResponse<object> { Item = response });
        }

        [HttpGet("shopify/mode")]
        [Authorize(Policy = "AdminAction")]
        public async Task<ActionResult<ItemResponse<bool>>> GetShopifyShippingMode(
            [FromServices] IShopifyAdminService shopifyAdminService)
        {
            try
            {
                bool enabled = await shopifyAdminService.UsesMarketDrivenShippingAsync();
                return Ok(new ItemResponse<bool> { Item = enabled });
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "Failed to detect Shopify market-driven shipping mode.");
                return StatusCode(502, new ErrorResponse("Unable to determine Shopify shipping mode."));
            }
        }

        [HttpPost("{id:int}/shopify-collection")]
        [Authorize(Policy = "AdminAction")]
        public async Task<ActionResult<ItemResponse<ShopifyCollectionCreateResult>>> CreateShippingCollection(
            int id,
            [FromServices] IShopifyAdminService shopifyAdminService)
        {
            ShippingPolicy? policy = _service.GetAll().FirstOrDefault(item => item.Id == id);
            if (policy == null)
                return NotFound(new ErrorResponse("Active shipping policy was not found."));

            if (!policy.AllowsOnlineCheckout)
                return BadRequest(new ErrorResponse("Contact-only policies must not have a checkout collection."));

            if (!string.IsNullOrWhiteSpace(policy.ShopifyShippingCollectionGid))
                return Conflict(new ErrorResponse("Shipping policy already has a mapped Shopify collection."));

            try
            {
                if (!await shopifyAdminService.UsesMarketDrivenShippingAsync())
                    return Conflict(new ErrorResponse("Configure shipping collections on a market-driven Shopify test store."));

                ShopifyCollectionCreateResult result =
                    await shopifyAdminService.CreateAutomatedShippingCollectionAsync(policy);

                // Persist only after Shopify confirms creation. Failed saves require
                // reconciling the created collection before retrying, not blind recreation.
                _service.UpdateShopifyShippingCollectionGid(id, result.CollectionGid);

                return Ok(new ItemResponse<ShopifyCollectionCreateResult> { Item = result });
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "Failed to provision Shopify shipping collection for policy {PolicyId}.", id);
                return StatusCode(502, new ErrorResponse(
                    "Shipping collection setup failed. Check logs and existing Shopify collections before retrying."));
            }
        }


        // Read-only Shopify inspection; lists collections in pages of up to 50.
        [HttpGet("shopify/collections")]
        [Authorize(Policy = "AdminAction")]
        public async Task<ActionResult<ItemResponse<ShopifyShippingCollectionPage>>> ListShopifyCollections(
            [FromQuery] string? after,
            [FromServices] IShopifyAdminService shopifyAdminService)
        {
            try
            {
                ShopifyShippingCollectionPage page =
                    await shopifyAdminService.GetShippingCollectionsAsync(after);
                return Ok(new ItemResponse<ShopifyShippingCollectionPage> { Item = page });
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "Failed to list Shopify collections.");
                return StatusCode(502, new ErrorResponse("Unable to inspect Shopify collections."));
            }
        }

        // Read-only inspection of one Shopify collection and its source conditions.
        [HttpGet("shopify/collection")]
        [Authorize(Policy = "AdminAction")]
        public async Task<ActionResult<ItemResponse<ShopifyShippingCollectionInfo>>> InspectShopifyCollection(
            [FromQuery] string collectionGid,
            [FromServices] IShopifyAdminService shopifyAdminService)
        {
            if (string.IsNullOrWhiteSpace(collectionGid))
                return BadRequest(new ErrorResponse("A Shopify collection GID is required."));

            try
            {
                ShopifyShippingCollectionInfo? collection =
                    await shopifyAdminService.GetShippingCollectionAsync(collectionGid);
                if (collection == null)
                    return NotFound(new ErrorResponse("Collection not found in the connected Shopify store."));

                return Ok(new ItemResponse<ShopifyShippingCollectionInfo> { Item = collection });
            }
            catch (ArgumentException ex)
            {
                return BadRequest(new ErrorResponse(ex.Message));
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "Failed to inspect Shopify collection.");
                return StatusCode(502, new ErrorResponse("Unable to inspect Shopify collection."));
            }
        }

        // Adopt a Shopify-created collection; never create or modify a collection here.
        // Collection source updates are a separate, explicitly verified operation.
        [HttpPost("{id:int}/shopify-collection/adopt")]
        [Authorize(Policy = "AdminAction")]
        public async Task<ActionResult<ItemResponse<ShopifyShippingCollectionInfo>>> AdoptShippingCollection(
            int id,
            [FromBody] ShopifyShippingCollectionAdoptionRequest request,
            [FromServices] IShopifyAdminService shopifyAdminService)
        {
            if (request == null || string.IsNullOrWhiteSpace(request.CollectionGid)
                || string.IsNullOrWhiteSpace(request.ExpectedTitle))
                return BadRequest(new ErrorResponse("CollectionGid and ExpectedTitle are required."));

            List<ShippingPolicy> policies = _service.GetAll() ?? new();
            ShippingPolicy? policy = policies.FirstOrDefault(item => item.Id == id);
            if (policy == null)
                return NotFound(new ErrorResponse("Active shipping policy not found."));
            if (!policy.AllowsOnlineCheckout)
                return BadRequest(new ErrorResponse("Contact-only shipping policies cannot be mapped."));

            // Never overwrite an existing mapping or reuse a collection assigned to
            // another policy. A repeated request for an existing mapping is safe.
            string gid = request.CollectionGid.Trim();
            if (!string.IsNullOrWhiteSpace(policy.ShopifyShippingCollectionGid)
                && !string.Equals(policy.ShopifyShippingCollectionGid, gid, StringComparison.Ordinal))
                return Conflict(new ErrorResponse("Policy already has a different mapped collection."));

            if (policies.Any(other => other.Id != id
                && string.Equals(other.ShopifyShippingCollectionGid, gid, StringComparison.Ordinal)))
                return Conflict(new ErrorResponse("Collection is already mapped to another shipping policy."));

            try
            {
                if (!await shopifyAdminService.UsesMarketDrivenShippingAsync())
                    return Conflict(new ErrorResponse("Collection adoption requires Market-driven Shipping."));

                ShopifyShippingCollectionInfo? collection =
                    await shopifyAdminService.GetShippingCollectionAsync(gid);
                if (collection == null)
                    return NotFound(new ErrorResponse("Collection not found in the connected Shopify store."));

                if (!string.Equals(collection.Title, request.ExpectedTitle.Trim(), StringComparison.Ordinal))
                    return Conflict(new ErrorResponse("Shopify collection title differs from ExpectedTitle. Reinspect before adopting."));

                if (!collection.Title.EndsWith(policy.Name, StringComparison.OrdinalIgnoreCase))
                    return Conflict(new ErrorResponse("Collection title does not match the local shipping policy name."));

                string expectedTag = $"ShippingClass_{id}";
                if (collection.ShippingClassTags.Any(tag =>
                        !string.Equals(tag, expectedTag, StringComparison.OrdinalIgnoreCase)))
                    return Conflict(new ErrorResponse("Collection has another shipping-class tag condition; review membership before adopting."));

                if (string.IsNullOrWhiteSpace(policy.ShopifyShippingCollectionGid))
                    _service.UpdateShopifyShippingCollectionGid(id, gid);

                // Mapping a collection is not proof that its new-product tag
                // membership source is configured. Verify that separately.
                return Ok(new ItemResponse<ShopifyShippingCollectionInfo> { Item = collection });
            }
            catch (ArgumentException ex)
            {
                return BadRequest(new ErrorResponse(ex.Message));
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "Failed to adopt Shopify collection for policy {PolicyId}.", id);
                return StatusCode(502, new ErrorResponse("Unable to adopt Shopify collection; verify the store and SQL state."));
            }
        }


        // Read-only preflight for a mapped policy's existing Shopify collection.
        [HttpGet("{id:int}/shopify-collection/tag-source-plan")]
        [Authorize(Policy = "AdminAction")]
        public async Task<ActionResult<ItemResponse<ShopifyShippingTagSourcePlan>>> GetTagSourcePlan(
            int id, [FromServices] IShopifyAdminService shopifyAdminService)
        {
            ShippingPolicy? policy = (_service.GetAll() ?? new())
                .FirstOrDefault(item => item.Id == id && item.AllowsOnlineCheckout);
            if (policy == null)
                return NotFound(new ErrorResponse("Active checkout shipping policy not found."));
            if (string.IsNullOrWhiteSpace(policy.ShopifyShippingCollectionGid))
                return Conflict(new ErrorResponse("Shipping policy must be mapped to an existing Shopify collection first."));

            try
            {
                ShopifyShippingCollectionInfo? collection =
                    await shopifyAdminService.GetShippingCollectionAsync(policy.ShopifyShippingCollectionGid);
                if (collection == null)
                    return NotFound(new ErrorResponse("Mapped collection does not exist in this Shopify store."));

                ShopifyShippingTagSourcePlan plan = BuildTagSourcePlan(policy, collection);
                return Ok(new ItemResponse<ShopifyShippingTagSourcePlan> { Item = plan });
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "Unable to prepare shipping source plan for policy {PolicyId}.", id);
                return StatusCode(502, new ErrorResponse("Unable to inspect Shopify collection for source migration."));
            }
        }

        // Dedicated write gate + optimistic source snapshots prevent accidental
        // application to a different or concurrently changed collection.
        [HttpPost("{id:int}/shopify-collection/add-tag-source")]
        [Authorize(Policy = "PartsDelete")]
        public async Task<ActionResult<ItemResponse<ShopifyShippingTagSourceAddResult>>> AddShippingTagSource(
            int id,
            [FromBody] ShopifyShippingTagSourceAddRequest request,
            [FromServices] IShopifyAdminService shopifyAdminService,
            [FromServices] Microsoft.Extensions.Options.IOptions<ShopifySettings> shopifyOptions)
        {
            if (request == null
                || request.Confirmation != "ADD_SHIPPING_TAG_SOURCE"
                || string.IsNullOrWhiteSpace(request.ExpectedTitle)
                || request.ExpectedProductCount < 0
                || request.ExpectedSourceGids == null)
                return BadRequest(new ErrorResponse(
                    "ExpectedTitle, ExpectedProductCount, ExpectedSourceGids and confirmation ADD_SHIPPING_TAG_SOURCE are required."));

            if (!shopifyOptions.Value.AllowMarketDrivenShippingCollectionSourceWrites)
                return Conflict(new ErrorResponse("Shipping collection source writes are disabled for this environment."));

            ShippingPolicy? policy = (_service.GetAll() ?? new())
                .FirstOrDefault(item => item.Id == id && item.AllowsOnlineCheckout);
            if (policy == null)
                return NotFound(new ErrorResponse("Active checkout shipping policy not found."));
            if (string.IsNullOrWhiteSpace(policy.ShopifyShippingCollectionGid))
                return Conflict(new ErrorResponse("Policy has no Shopify collection mapping."));

            try
            {
                if (!await shopifyAdminService.UsesMarketDrivenShippingAsync())
                    return Conflict(new ErrorResponse("This store does not use Market-driven Shipping."));

                ShopifyShippingCollectionInfo? before =
                    await shopifyAdminService.GetShippingCollectionAsync(policy.ShopifyShippingCollectionGid);
                if (before == null)
                    return NotFound(new ErrorResponse("Mapped Shopify collection no longer exists."));

                ShopifyShippingTagSourcePlan plan = BuildTagSourcePlan(policy, before);
                List<string> actualSources = plan.ExistingSourceGids;
                List<string> expectedSources = request.ExpectedSourceGids
                    .Where(source => !string.IsNullOrWhiteSpace(source))
                    .OrderBy(source => source, StringComparer.Ordinal).ToList();

                if (!string.Equals(before.Title, request.ExpectedTitle.Trim(), StringComparison.Ordinal)
                    || before.ProductCount != request.ExpectedProductCount
                    || !actualSources.SequenceEqual(expectedSources, StringComparer.Ordinal))
                    return Conflict(new ErrorResponse("Collection changed since preview. Inspect the tag-source plan again."));

                if (plan.HasConflictingShippingTagRules)
                    return Conflict(new ErrorResponse("Collection already has conflicting shipping-class rules. Review it manually."));

                if (plan.AlreadyConfigured)
                {
                    return Ok(new ItemResponse<ShopifyShippingTagSourceAddResult>
                    {
                        Item = new ShopifyShippingTagSourceAddResult
                        {
                            PolicyId = id, CollectionGid = before.CollectionGid,
                            ShippingClassTag = plan.ShippingClassTag,
                            SourceCreated = false,
                            ProductCountBefore = before.ProductCount,
                            ProductCountAfter = before.ProductCount,
                            PreservedSourceGids = actualSources,
                            CollectionAfter = before
                        }
                    });
                }

                ShopifyCollectionSourceCreateResult change =
                    await shopifyAdminService.AddShippingTagSourceAsync(before.CollectionGid, plan.ShippingClassTag);

                // The mutation response carries the source list. Shopify can update
                // product membership asynchronously, so counts require later review.
                ShopifyShippingCollectionInfo after = change.Collection;
                HashSet<string> afterIds = after.Sources.Select(source => source.SourceGid)
                    .ToHashSet(StringComparer.Ordinal);
                List<string> missingSources = actualSources
                    .Where(gid => !afterIds.Contains(gid)).ToList();
                if (missingSources.Count != 0)
                    throw new ApplicationException(
                        "Shopify source update returned missing original source IDs. STOP and inspect collection before retrying.");

                ShopifyShippingTagSourcePlan afterPlan = BuildTagSourcePlan(policy, after);
                if (!afterPlan.AlreadyConfigured || afterPlan.HasConflictingShippingTagRules)
                    throw new ApplicationException(
                        "Shopify mutation completed but expected tag source was not verified. STOP and inspect before retrying.");

                return Ok(new ItemResponse<ShopifyShippingTagSourceAddResult>
                {
                    Item = new ShopifyShippingTagSourceAddResult
                    {
                        PolicyId = id, CollectionGid = after.CollectionGid,
                        ShippingClassTag = plan.ShippingClassTag,
                        SourceCreated = true,
                        ProductCountBefore = before.ProductCount,
                        ProductCountAfter = after.ProductCount,
                        JobDone = change.JobDone,
                        JobGid = change.JobGid,
                        PreservedSourceGids = actualSources,
                        CollectionAfter = after
                    }
                });
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "Failed shipping tag-source append for policy {PolicyId}. Verify Shopify before any retry.", id);
                return StatusCode(502, new ErrorResponse(
                    "Shopify source update could not be verified. Inspect the collection before retrying; do not blindly repeat the mutation."));
            }
        }

        private static ShopifyShippingTagSourcePlan BuildTagSourcePlan(
            ShippingPolicy policy, ShopifyShippingCollectionInfo collection)
        {
            string expectedTag = $"ShippingClass_{policy.Id}";
            bool dedicated = collection.Sources.Any(source =>
                source.SourceType == "CollectionConditionsSource"
                && source.TargetType == "PRODUCTS"
                && source.InclusionConditionCount == 1
                && !source.HasExplicitSelections
                && !source.HasExclusions
                && source.TagConditions.Count == 1
                && source.TagConditions[0].Relation == "TAGGED_WITH"
                && source.TagConditions[0].MatchType == "ANY"
                && source.TagConditions[0].Values.Count == 1
                && string.Equals(source.TagConditions[0].Values[0], expectedTag,
                    StringComparison.OrdinalIgnoreCase));

            bool conflict = collection.Sources
                .SelectMany(source => source.TagConditions)
                .SelectMany(condition => condition.Values)
                .Any(tag => tag.StartsWith("ShippingClass_", StringComparison.OrdinalIgnoreCase)
                    && !string.Equals(tag, expectedTag, StringComparison.OrdinalIgnoreCase));

            // Having the expected tag embedded in an unrelated, more restrictive
            // source is also unsafe: it does not guarantee new products are included.
            bool ambiguousExpectedTag = collection.Sources
                .Where(source => source.TagConditions.Any(condition => condition.Values.Any(value =>
                    string.Equals(value, expectedTag, StringComparison.OrdinalIgnoreCase))))
                .Any(source => source.InclusionConditionCount != 1
                    || source.HasExplicitSelections
                    || source.HasExclusions
                    || source.TargetType != "PRODUCTS"
                    || source.TagConditions.Count != 1
                    || source.TagConditions[0].Values.Count != 1
                    || source.TagConditions[0].Relation != "TAGGED_WITH"
                    || source.TagConditions[0].MatchType != "ANY");

            return new ShopifyShippingTagSourcePlan
            {
                PolicyId = policy.Id,
                PolicyName = policy.Name,
                CollectionGid = collection.CollectionGid,
                CollectionTitle = collection.Title,
                ProductCount = collection.ProductCount,
                ShippingClassTag = expectedTag,
                ExistingSourceGids = collection.Sources
                    .Select(source => source.SourceGid)
                    .OrderBy(gid => gid, StringComparer.Ordinal).ToList(),
                AlreadyConfigured = dedicated,
                HasConflictingShippingTagRules = conflict || ambiguousExpectedTag
            };
        }

        [HttpGet("shopify/profiles")]
        [Authorize(Policy = "AdminAction")]
        public async Task<ActionResult<ItemResponse<List<Site_2024.Web.Api.Models.Shopify.ShopifyDeliveryProfileResult>>>> GetShopifyProfiles(
            [FromServices] IShopifyAdminService shopifyAdminService)
        {
            try
            {
                var profiles = await shopifyAdminService.GetDeliveryProfilesAsync();
                return Ok(new ItemResponse<List<Site_2024.Web.Api.Models.Shopify.ShopifyDeliveryProfileResult>>
                {
                    Item = profiles
                });
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "Failed to load Shopify delivery profiles.");
                return StatusCode(500, new ErrorResponse(ex.Message));
            }
        }

        [HttpPut("{id:int}/shopify-profile")]
        [Authorize(Policy = "AdminAction")]
        public ActionResult<BaseResponse> UpdateShopifyProfile(
            int id,
            [FromBody] ShippingPolicyShopifyProfileUpdateRequest model)
        {
            try
            {
                _service.UpdateShopifyProfileId(id, model.ShopifyProfileId);
                return Ok(new SuccessResponse());
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "Failed to map ShippingPolicy {ShippingPolicyId} to Shopify profile.", id);
                return StatusCode(500, new ErrorResponse(ex.Message));
            }
        }

        [HttpPost]
        [Authorize(Policy = "AdminAction")]
        public ActionResult<ItemResponse<int>> Create(ShippingPolicyAddRequest model)
        {
            int code = 201;
            BaseResponse response = null;

            try
            {
                var user = _authService.GetCurrentUser();
                int id = _service.Add(model, user.Id);

                response = new ItemResponse<int> { Item = id };
            }
            catch (Exception ex)
            {
                code = 500;
                base.Logger.LogError(ex.ToString());
                response = new ErrorResponse(ex.Message);
            }

            return StatusCode(code, response);
        }
    }
}

