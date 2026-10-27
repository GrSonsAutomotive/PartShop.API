using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Site_2024.Models.Domain.Parts;
using Site_2024.Web.Api.Constructors;
using Site_2024.Web.Api.Interfaces;
using Site_2024.Web.Api.Models;
using Site_2024.Web.Api.Models.Shopify;
using Site_2024.Web.Api.Models.User;
using Site_2024.Web.Api.Requests;
using Site_2024.Web.Api.Responses;
using Site_2024.Web.Api.Services;

namespace Site_2024.Web.Api.Controllers
{
    [Route("api/home")]
    [ApiController]
    public class PartsApiController : BaseApiController
    {
        private readonly IPartService _service;
        private readonly IPartImageService _partImageService;
        private readonly ILocationService _locationService;
        private readonly IShopifyPartSyncService _shopifyPartSyncService;
        private readonly IShopifyAdminService _shopifyAdminService;
        private readonly IAuthenticationService<IUserAuthData> _authService;
        private readonly IWebHostEnvironment _webHostEnvironment;
        private readonly IConfiguration _configuration;

        public PartsApiController(
            IPartService service,
            IPartImageService partImageService,
            ILocationService locationService,
            IShopifyPartSyncService shopifyPartSyncService,
            IShopifyAdminService shopifyAdminService,
            ILogger<PartsApiController> logger,
            IAuthenticationService<IUserAuthData> authService,
            IWebHostEnvironment webHostEnvironment,
            IConfiguration configuration
        ) : base(logger)
        {
            _service = service;
            _partImageService = partImageService;
            _locationService = locationService;
            _shopifyPartSyncService = shopifyPartSyncService;
            _shopifyAdminService = shopifyAdminService;
            _authService = authService;
            _webHostEnvironment = webHostEnvironment;
            _configuration = configuration;
        }

        [HttpPost("add-new")]
        [Authorize(Policy = "AdminAction")]
        public async Task<ActionResult<ItemResponse<int>>> Add([FromForm] PartAddRequest model, IFormFile? image)
        {
            int code = 201;
            BaseResponse response;

            try
            {
                var user = _authService.GetCurrentUser();
                if (user == null)
                {
                    code = 401;
                    response = new ErrorResponse("You must be logged in.");
                    return StatusCode(code, response);
                }

                // Validate LocationId exists
                var loc = _locationService.GetLocationById(model.LocationId);
                if (loc == null)
                {
                    code = 400;
                    response = new ErrorResponse("Invalid LocationId.");
                    return StatusCode(code, response);
                }

                // Basic file validation
                string? imageUrl = null;
                if (image != null && image.Length > 0)
                {
                    string ext = Path.GetExtension(image.FileName).ToLowerInvariant();
                    string[] allowed = { ".jpg", ".jpeg", ".png", ".webp" };

                    if (!allowed.Contains(ext))
                    {
                        code = 400;
                        response = new ErrorResponse("Invalid image type. Allowed: jpg, jpeg, png, webp.");
                        return StatusCode(code, response);
                    }

                    const long maxBytes = 5 * 1024 * 1024;
                    if (image.Length > maxBytes)
                    {
                        code = 400;
                        response = new ErrorResponse("Image too large. Max size is 5MB.");
                        return StatusCode(code, response);
                    }

                    string uploadRoot =
                        _configuration["UploadStorage:RootPath"]
                        ?? Path.Combine(_webHostEnvironment.WebRootPath, "uploads");

                    string uploadsFolder = Path.Combine(uploadRoot, "items");

                    Directory.CreateDirectory(uploadsFolder);

                    string fileName = $"{Guid.NewGuid()}{ext}";
                    string filePath = Path.Combine(uploadsFolder, fileName);

                    using (var stream = new FileStream(filePath, FileMode.Create))
                    {
                        await image.CopyToAsync(stream);
                    }

                    imageUrl = $"/uploads/items/{fileName}";
                }

                if (model.AdminNotes != null && model.AdminNotes.Length > 2000)
                {
                    code = 400;
                    response = new ErrorResponse("Admin Notes cannot exceed 2000 characters.");
                    return StatusCode(code, response);
                }

                model.Image = imageUrl;
                model.AvailableId = 1; // server rule: new parts default to Available

                int userId = user.Id;

                // 1. Create the part locally first.
                int id = _service.Insert(model, userId);

                // 2. Then try Shopify sync.
                // Important: Shopify failure should not fail local part creation.
                try
                {
                    ShopifyPartSyncResult shopifyResult =
                        await _shopifyPartSyncService.CreateAndSyncProductForPartAsync(id);

                    base.Logger.LogInformation(
                        "Shopify product created for PartId {PartId}. ShopifyProductId: {ShopifyProductId}, ShopifyVariantId: {ShopifyVariantId}, ShopifyInventoryItemId: {ShopifyInventoryItemId}",
                        id,
                        shopifyResult.CreateResult.ProductId,
                        shopifyResult.CreateResult.VariantId,
                        shopifyResult.CreateResult.InventoryItemId);
                }
                catch (Exception shopifyEx)
                {
                    base.Logger.LogError(
                        shopifyEx,
                        "Shopify sync failed for newly created PartId {PartId}. Local part was still created.",
                        id);

                    // Later we can save this to a ShopifySyncStatus / ShopifySyncError field.
                    // For now, local add still succeeds.
                }

                // 3. Keep the same response so your front end does not need to change.
                response = new ItemResponse<int> { Item = id };
            }
            catch (Exception ex)
            {
                code = 500;
                response = new ErrorResponse(ex.Message);
                base.Logger.LogError(ex, "Add part failed.");
            }

            return StatusCode(code, response);
        }

        [HttpPost("{id:int}/shopify/publish")]
        [Authorize(Policy = "AdminAction")]
        public async Task<ActionResult<ItemResponse<ShopifyProductPublishResult>>> PublishShopifyProduct(int id)
        {
            int code = 200;
            BaseResponse response;

            try
            {
                var user = _authService.GetCurrentUser();

                if (user == null)
                {
                    code = 401;
                    response = new ErrorResponse("You must be logged in.");
                    return StatusCode(code, response);
                }

                Part part = _service.GetPartById(id);

                if (part == null)
                {
                    code = 404;
                    response = new ErrorResponse("Part not found.");
                    return StatusCode(code, response);
                }

                if (part.ShippingPolicy?.AllowsOnlineCheckout == false)
                {
                    code = 400;
                    response = new ErrorResponse(
                        "This item requires a custom shipping quote and cannot be published for Shopify checkout.");
                    return StatusCode(code, response);
                }

                if (!part.ShopifyProductId.HasValue)
                {
                    code = 400;
                    response = new ErrorResponse("This part has not been synced to Shopify yet.");
                    return StatusCode(code, response);
                }

                // Ensure the selected Site_2024 shipping tier is assigned before publishing.
                await _shopifyPartSyncService.SyncShippingProfileForPartAsync(id);

                // Publish uses the latest local quantity, price, SKU, and photos.
                ShopifyProductInventorySyncResult inventoryResult =
                    await _shopifyAdminService.SyncProductDetailsForPartAsync(part);

                List<PartImage> images = _partImageService.GetByPartId(id);

                ShopifyProductMediaSyncResult mediaResult =
                    await _shopifyAdminService.SyncProductImagesAsync(part, images);

                ShopifyProductPublishResult publishResult =
                    await _shopifyAdminService.PublishProductForPartAsync(part);

                publishResult.InventoryQuantity = inventoryResult.Quantity;
                publishResult.ImagesRequested = mediaResult.ImagesRequested;
                publishResult.ImagesAdded = mediaResult.ImagesAdded;
                publishResult.ImagesSkipped = mediaResult.ImagesSkipped;

                response = new ItemResponse<ShopifyProductPublishResult>
                {
                    Item = publishResult
                };
            }
            catch (Exception ex)
            {
                code = 500;
                response = new ErrorResponse(ex.Message);
                Logger.LogError(ex, "Shopify publish failed for PartId {PartId}", id);
            }

            return StatusCode(code, response);
        }


        [HttpPost("{id:int}/shopify/sync")]
        [Authorize(Policy = "AdminAction")]
        public async Task<ActionResult<ItemResponse<ShopifyPartManualSyncResult>>> SyncShopifyProduct(int id)
        {
            int code = 200;
            BaseResponse response;

            try
            {
                var user = _authService.GetCurrentUser();

                if (user == null)
                {
                    code = 401;
                    response = new ErrorResponse("You must be logged in.");
                    return StatusCode(code, response);
                }

                Part part = _service.GetPartById(id);

                if (part == null)
                {
                    code = 404;
                    response = new ErrorResponse("Part not found.");
                    return StatusCode(code, response);
                }

                if (!part.ShopifyProductId.HasValue ||
                    !part.ShopifyVariantId.HasValue ||
                    !part.ShopifyInventoryItemId.HasValue)
                {
                    code = 400;
                    response = new ErrorResponse(
                        "This part is missing one or more Shopify IDs.");
                    return StatusCode(code, response);
                }

                await _shopifyPartSyncService.SyncShippingProfileForPartAsync(id);

                ShopifyProductInventorySyncResult inventoryResult =
                    await _shopifyAdminService.SyncProductDetailsForPartAsync(part);

                List<PartImage> images = _partImageService.GetByPartId(id);

                ShopifyProductMediaSyncResult mediaResult =
                    await _shopifyAdminService.SyncProductImagesAsync(part, images);

                response = new ItemResponse<ShopifyPartManualSyncResult>
                {
                    Item = new ShopifyPartManualSyncResult
                    {
                        PartId = id,
                        Inventory = inventoryResult,
                        Media = mediaResult
                    }
                };
            }
            catch (Exception ex)
            {
                code = 500;
                response = new ErrorResponse(ex.Message);
                Logger.LogError(ex, "Manual Shopify sync failed for PartId {PartId}", id);
            }

            return StatusCode(code, response);
        }

        [HttpPost("shopify/tags/backfill")]
        [Authorize(Policy = "AdminAction")]
        public async Task<ActionResult<ItemResponse<ShopifyTagBackfillResult>>> BackfillShopifyTags()
        {
            int code = 200;
            BaseResponse response;

            try
            {
                var user = _authService.GetCurrentUser();

                if (user == null)
                {
                    code = 401;
                    return StatusCode(code, new ErrorResponse("You must be logged in."));
                }

                ShopifyTagBackfillResult result = new();
                const int pageSize = 50;
                int pageIndex = 0;
                bool hasNextPage;

                do
                {
                    Paged<PartSummary>? page = _service.GetAllPaginated(pageIndex, pageSize);

                    if (page == null)
                    {
                        break;
                    }

                    foreach (PartSummary summary in page.PagedItems)
                    {
                        result.PartsExamined++;
                        Part? part = _service.GetPartById(summary.Id);

                        if (part?.ShopifyProductId.HasValue != true)
                        {
                            result.ProductsSkipped++;
                            continue;
                        }

                        try
                        {
                            await _shopifyAdminService.SyncProductTagsForPartAsync(part);
                            result.ProductsUpdated++;
                        }
                        catch (Exception itemEx)
                        {
                            result.ProductsFailed++;

                            if (result.Errors.Count < 25)
                            {
                                result.Errors.Add($"Part {summary.Id}: {itemEx.Message}");
                            }

                            Logger.LogError(
                                itemEx,
                                "Shopify managed-tag backfill failed for PartId {PartId}",
                                summary.Id);
                        }
                    }

                    hasNextPage = page.HasNextPage;
                    pageIndex++;
                }
                while (hasNextPage);

                response = new ItemResponse<ShopifyTagBackfillResult>
                {
                    Item = result
                };
            }
            catch (Exception ex)
            {
                code = 500;
                response = new ErrorResponse(ex.Message);
                Logger.LogError(ex, "Shopify managed-tag backfill failed.");
            }

            return StatusCode(code, response);
        }

        [HttpPost("{id:int}/shopify/unpublish")]
        [Authorize(Policy = "AdminAction")]
        public async Task<ActionResult<ItemResponse<ShopifyProductPublishResult>>> UnpublishShopifyProduct(int id)
        {
            int code = 200;
            BaseResponse response;

            try
            {
                var user = _authService.GetCurrentUser();

                if (user == null)
                {
                    code = 401;
                    response = new ErrorResponse("You must be logged in.");
                    return StatusCode(code, response);
                }

                Part part = _service.GetPartById(id);

                if (part == null)
                {
                    code = 404;
                    response = new ErrorResponse("Part not found.");
                    return StatusCode(code, response);
                }

                if (!part.ShopifyProductId.HasValue)
                {
                    code = 400;
                    response = new ErrorResponse("This part has not been synced to Shopify yet.");
                    return StatusCode(code, response);
                }

                ShopifyProductPublishResult result =
                    await _shopifyAdminService.UnpublishProductForPartAsync(part);

                response = new ItemResponse<ShopifyProductPublishResult>
                {
                    Item = result
                };
            }
            catch (Exception ex)
            {
                code = 500;
                response = new ErrorResponse(ex.Message);
                Logger.LogError(ex, "Shopify unpublish failed for PartId {PartId}", id);
            }

            return StatusCode(code, response);
        }

        [HttpPost("{id:int}/images")]
        [Consumes("multipart/form-data")]
        [Authorize(Policy = "AdminAction")]
        public ActionResult<BaseResponse> UploadImages(int id, [FromForm] Requests.PartImagesUploadRequest model)
        {
            int code = 200;
            BaseResponse response;

            try
            {
                var user = _authService.GetCurrentUser();
                if (user == null)
                {
                    code = 401;
                    return StatusCode(code, new ErrorResponse("You must be logged in."));
                }

                if (model?.Images == null || model.Images.Count == 0)
                {
                    code = 400;
                    return StatusCode(code, new ErrorResponse("At least one image is required."));
                }

                string[] allowed = { ".jpg", ".jpeg", ".png", ".webp" };
                const long maxBytes = 5 * 1024 * 1024;

                string uploadRoot =
                    _configuration["UploadStorage:RootPath"]
                    ?? Path.Combine(_webHostEnvironment.WebRootPath, "uploads");

                string uploadsFolder = Path.Combine(uploadRoot, "items");

                Directory.CreateDirectory(uploadsFolder);

                var urls = new List<string>();

                for (int i = 0; i < model.Images.Count; i++)
                {
                    IFormFile image = model.Images[i];

                    if (image == null || image.Length == 0)
                    {
                        code = 400;
                        return StatusCode(code, new ErrorResponse("One of the images is empty."));
                    }

                    string ext = Path.GetExtension(image.FileName).ToLowerInvariant();
                    if (!allowed.Contains(ext))
                    {
                        code = 400;
                        return StatusCode(code, new ErrorResponse($"Invalid image type: {ext}. Allowed: jpg, jpeg, png, webp."));
                    }

                    if (image.Length > maxBytes)
                    {
                        code = 400;
                        return StatusCode(code, new ErrorResponse("Image too large. Max size is 5MB."));
                    }

                    string fileName = $"{Guid.NewGuid()}{ext}";
                    string filePath = Path.Combine(uploadsFolder, fileName);

                    using (var stream = new FileStream(filePath, FileMode.Create))
                    {
                        image.CopyTo(stream);
                    }

                    string imageUrl = $"/uploads/items/{fileName}";
                    urls.Add(imageUrl);

                    bool isPrimary = (i == 0);
                    int sortOrder = i;

                    _partImageService.Add(id, imageUrl, isPrimary, sortOrder, user.Id);
                }

                // Keep Parts.Image in sync with the primary image
                _service.PatchPart(id, new PartPatchRequest { Image = urls[0] }, user.Id);

                response = new ItemResponse<List<string>> { Item = urls };
            }
            catch (Exception ex)
            {
                code = 500;
                base.Logger.LogError(ex.ToString());
                response = new ErrorResponse(ex.Message);
            }

            return StatusCode(code, response);
        }



        [HttpGet("{id:int}/images")]
        [AllowAnonymous]
        public ActionResult<ItemResponse<List<PartImage>>> GetImagesByPartId(int id)
        {
            int code = 200;
            BaseResponse response;

            try
            {
                List<PartImage> list = _partImageService.GetByPartId(id);
                response = new ItemResponse<List<PartImage>> { Item = list };
            }
            catch (Exception ex)
            {
                code = 500;
                base.Logger.LogError(ex.ToString());
                response = new ErrorResponse(ex.Message);
            }

            return StatusCode(code, response);
        }

        // Read-only preview. Intentionally does not authorize a shipping-policy
        // reassignment. Migrated collections can have manual memberships that
        // survive product tag changes, and audits currently show only 10 selections
        // per source. Ambiguous/partial membership scans are explicitly flagged.
        [HttpGet("{id:int}/shopify/shipping-reassignment-plan")]
        [Authorize(Policy = "AdminAction")]
        public async Task<ActionResult<ItemResponse<ShopifyShippingReassignmentPlan>>> GetShippingReassignmentPlan(
            int id,
            [FromQuery] int targetShippingPolicyId,
            [FromServices] IShippingPoliciesService policiesService)
        {
            if (targetShippingPolicyId <= 0)
                return BadRequest(new ErrorResponse("A valid targetShippingPolicyId is required."));

            try
            {
                Part part = _service.GetPartById(id);
                if (part == null)
                    return NotFound(new ErrorResponse("Part not found."));

                List<ShippingPolicy> policies = policiesService.GetAll() ?? new();
                ShippingPolicy? target = policies.FirstOrDefault(p => p.Id == targetShippingPolicyId && p.IsActive);
                if (target == null)
                    return BadRequest(new ErrorResponse("Target shipping policy is not active or does not exist."));

                ShippingPolicy? current = policies.FirstOrDefault(p => p.Id == part.ShippingPolicy?.Id);
                bool marketDriven = await _shopifyAdminService.UsesMarketDrivenShippingAsync();

                ShopifyShippingReassignmentPlan plan = new()
                {
                    PartId = id,
                    CurrentShippingPolicyId = part.ShippingPolicy?.Id,
                    TargetShippingPolicyId = target.Id,
                    ShopifyProductGid = part.ShopifyProductId.HasValue
                        ? $"gid://shopify/Product/{part.ShopifyProductId.Value}" : null,
                    MarketDrivenShipping = marketDriven,
                    ShippingPolicyChanges = targetShippingPolicyId != part.ShippingPolicy?.Id,
                    CurrentCollectionGid = current?.ShopifyShippingCollectionGid,
                    TargetCollectionGid = target.ShopifyShippingCollectionGid,
                    ExpectedTargetTag = target.AllowsOnlineCheckout
                        ? $"ShippingClass_{target.Id}" : null,
                    TargetIsContactOnly = !target.AllowsOnlineCheckout
                };

                if (!plan.ShippingPolicyChanges)
                {
                    plan.Notes.Add("The part already uses this shipping policy; no reassignment is needed.");
                }
                else if (!marketDriven)
                {
                    plan.Notes.Add("The store uses legacy delivery profiles; this preview only audits Market-driven Shipping.");
                }
                else if (!part.ShopifyProductId.HasValue)
                {
                    plan.Notes.Add("The part has no Shopify product; there is no Shopify membership to remove.");
                }
                else
                {
                    // This is a preview, not a verified plan for executing writes.
                    // A production-grade reassignment must check every mapped
                    // collection and paginate every manual source.
                    if (current?.AllowsOnlineCheckout == true)
                    {
                        if (string.IsNullOrWhiteSpace(current.ShopifyShippingCollectionGid))
                        {
                            plan.Notes.Add("Current policy has no mapped collection; membership cannot be inspected.");
                            plan.OldManualSelectionScanComplete = false;
                        }
                        else
                        {
                            ShopifyShippingCollectionInfo? sourceCollection =
                                await _shopifyAdminService.GetShippingCollectionAsync(
                                    current.ShopifyShippingCollectionGid);
                            ShopifyShippingMembershipAudit? audit =
                                await _shopifyAdminService.AuditShippingCollectionMembershipAsync(
                                    current.ShopifyShippingCollectionGid);

                            if (sourceCollection == null || audit == null)
                            {
                                plan.OldManualSelectionScanComplete = false;
                                plan.Notes.Add("Current shipping collection could not be audited.");
                            }
                            else
                            {
                                plan.OldManualSelectionScanComplete = true;
                                foreach (ShopifyShippingSourceMembershipAudit member in audit.Sources)
                                {
                                    ShopifyShippingCollectionSourceInfo? source =
                                        sourceCollection.Sources.FirstOrDefault(s => s.SourceGid == member.SourceGid);
                                    if (member.MoreManualSelections || source == null)
                                    {
                                        plan.OldManualSelectionScanComplete = false;
                                        continue;
                                    }
                                    if (member.ManualSelectionProductGids.Contains(plan.ShopifyProductGid!))
                                    {
                                        plan.OldManualSourceGidsContainingProduct.Add(member.SourceGid);
                                        if (source.SourceType != "CollectionConditionsSource"
                                            || source.TargetType != "PRODUCTS"
                                            || source.InclusionConditionCount != 0
                                            || source.HasExclusions)
                                            plan.Notes.Add("The part has a complex manually selected source requiring separate review.");
                                    }
                                }
                                if (plan.OldManualSelectionScanComplete != true)
                                    plan.Notes.Add("Some manual source selections exceed the first audit page; full pagination is required.");
                            }
                        }
                    }

                    if (target.AllowsOnlineCheckout)
                    {
                        if (string.IsNullOrWhiteSpace(target.ShopifyShippingCollectionGid))
                            plan.Notes.Add("Target policy has no mapped Shopify collection.");
                        else
                        {
                            ShopifyShippingCollectionInfo? destination =
                                await _shopifyAdminService.GetShippingCollectionAsync(
                                    target.ShopifyShippingCollectionGid);
                            plan.TargetHasMatchingTagSource = destination?.Sources.Any(source =>
                                source.SourceType == "CollectionConditionsSource"
                                && source.TargetType == "PRODUCTS"
                                && !source.HasExclusions
                                && !source.HasExplicitSelections
                                && source.InclusionConditionCount == 1
                                && source.TagConditions.Count == 1
                                && source.TagConditions[0].Relation == "TAGGED_WITH"
                                && source.TagConditions[0].MatchType == "ANY"
                                && source.TagConditions[0].Values.Count == 1
                                && string.Equals(source.TagConditions[0].Values[0],
                                    plan.ExpectedTargetTag, StringComparison.OrdinalIgnoreCase)) == true;
                            if (!plan.TargetHasMatchingTagSource)
                                plan.Notes.Add("Target collection lacks a verified, unrestricted shipping-tag source.");
                        }
                    }
                    else
                    {
                        plan.Notes.Add("Contact-only target must be unpublished before any membership change.");
                    }
                }

                // Cross-policy audit: migration may leave historical manual memberships
                // outside the current SQL policy collection. Sampled source selections
                // are never treated as complete when Shopify reports another page.
                if (marketDriven && plan.ShippingPolicyChanges && part.ShopifyProductId.HasValue)
                {
                    var checkoutPolicies = policies
                        .Where(p => p.IsActive && p.AllowsOnlineCheckout)
                        .ToList();

                    plan.UnmappedActiveCheckoutPolicyIds = checkoutPolicies
                        .Where(p => string.IsNullOrWhiteSpace(p.ShopifyShippingCollectionGid))
                        .Select(p => p.Id).OrderBy(v => v).ToList();

                    plan.AllMappedManualSelectionScansComplete =
                        plan.UnmappedActiveCheckoutPolicyIds.Count == 0;

                    foreach (var group in checkoutPolicies
                        .Where(p => !string.IsNullOrWhiteSpace(p.ShopifyShippingCollectionGid))
                        .GroupBy(p => p.ShopifyShippingCollectionGid!, StringComparer.Ordinal))
                    {
                        plan.MappedCollectionsInspected++;
                        try
                        {
                            ShopifyShippingMembershipAudit? audit =
                                await _shopifyAdminService.AuditShippingCollectionMembershipAsync(group.Key);
                            if (audit == null || audit.CollectionGid != group.Key)
                            {
                                plan.AllMappedManualSelectionScansComplete = false;
                                plan.CollectionsWithIncompleteScans.Add(group.Key);
                                continue;
                            }

                            bool scanComplete = true;
                            foreach (ShopifyShippingSourceMembershipAudit source in audit.Sources)
                            {
                                // Unknown source types cannot be certified by the
                                // manual-selection query; never assume they're empty.
                                if (source.SourceType != "CollectionConditionsSource"
                                    || source.MoreManualSelections)
                                {
                                    scanComplete = false;
                                }

                                if (source.ManualSelectionProductGids.Contains(plan.ShopifyProductGid!))
                                {
                                    foreach (ShippingPolicy mappedPolicy in group)
                                    {
                                        plan.ManualMembershipsAcrossMappedCollections.Add(
                                            new ShopifyShippingManualMembershipHit
                                            {
                                                PolicyId = mappedPolicy.Id,
                                                CollectionGid = group.Key,
                                                SourceGid = source.SourceGid
                                            });
                                    }
                                }
                            }

                            if (!scanComplete)
                            {
                                plan.AllMappedManualSelectionScansComplete = false;
                                plan.CollectionsWithIncompleteScans.Add(group.Key);
                            }
                        }
                        catch (Exception scanEx)
                        {
                            Logger.LogWarning(scanEx,
                                "Cross-policy shipping membership read failed for PartId {PartId}, Collection {CollectionGid}.",
                                id, group.Key);
                            plan.AllMappedManualSelectionScansComplete = false;
                            plan.CollectionsWithIncompleteScans.Add(group.Key);
                        }
                    }

                    if (plan.UnmappedActiveCheckoutPolicyIds.Count > 0)
                        plan.Notes.Add("Some active checkout policies are unmapped; migration readiness cannot be established.");
                    if (plan.AllMappedManualSelectionScansComplete != true)
                        plan.Notes.Add("Cross-policy manual membership scan is incomplete. Do not proceed with reassignment.");
                    if (plan.ManualMembershipsAcrossMappedCollections.Count > 0)
                        plan.Notes.Add("Manual memberships exist in mapped collections; automatic tag-only reassignment is not sufficient.");
                }


                if (marketDriven && part.ShopifyProductId.HasValue)
                {
                    ShopifyProductShippingSnapshot snapshot =
                        await _shopifyAdminService.GetProductShippingSnapshotAsync(
                            part.ShopifyProductId.Value);
                    plan.ShopifyProductStatus = snapshot.Status;
                    plan.ShopifyPublishedOnOnlineStore = snapshot.PublishedOnOnlineStore;
                    plan.ShopifyOnlineStorePublicationGid = snapshot.OnlineStorePublicationGid;
                    plan.ShopifyShippingClassTags = snapshot.ShippingClassTags;
                    string currentShippingTag = $"ShippingClass_{part.ShippingPolicy?.Id}";
                    plan.CurrentShopifyTagMatchesSql =
                        snapshot.ShippingClassTags.Count == 1
                        && string.Equals(snapshot.ShippingClassTags[0], currentShippingTag,
                            StringComparison.OrdinalIgnoreCase);
                    if (plan.CurrentShopifyTagMatchesSql != true)
                        plan.Notes.Add("Shopify shipping-class tags do not uniquely match the part's current SQL policy; investigate before reassignment.");
                }

                plan.Notes.Add("Read-only preview only. Automatic Market-driven Shipping reassignment remains blocked.");
                return Ok(new ItemResponse<ShopifyShippingReassignmentPlan> { Item = plan });
            }
            catch (Exception ex)
            {
                Logger.LogError(ex,
                    "Failed read-only shipping reassignment preview for PartId {PartId}.", id);
                return StatusCode(502, new ErrorResponse(
                    "Could not inspect Shopify shipping memberships. No changes were made."));
            }
        }


        // Explicit, independently gated DEV rollout. The normal PATCH route
        // still rejects shipping-policy changes in Markets mode.
        // This first version supports unmixed, automatic-tag reassignment only.
        // Manual selection removal stays in its separately audited operation.
        [HttpPost("{id:int}/shopify/shipping-reassign")]
        [Authorize(Policy = "PartsDelete")]
        public async Task<ActionResult<ItemResponse<ShopifyShippingReassignmentResult>>> ReassignMarketShipping(
            int id, [FromBody] ShopifyShippingReassignmentRequest request,
            [FromServices] IShippingPoliciesService policiesService,
            [FromServices] Microsoft.Extensions.Options.IOptions<ShopifySettings> shopifyOptions)
        {
            if (!shopifyOptions.Value.AllowMarketDrivenShippingReassignmentWrites)
                return Conflict(new ErrorResponse(
                    "Market-driven shipping reassignment writes are disabled. No changes made."));

            if (request == null
                || request.Confirmation != "REASSIGN_MARKETS_SHIPPING_POLICY"
                || request.TargetShippingPolicyId <= 0
                || request.ExpectedCurrentShippingPolicyId <= 0
                || string.IsNullOrWhiteSpace(request.ExpectedShopifyProductGid))
                return BadRequest(new ErrorResponse(
                    "Expected source policy, destination policy, Shopify product GID and explicit confirmation are required."));

            var user = _authService.GetCurrentUser();
            if (user == null)
                return Unauthorized(new ErrorResponse("Admin authentication required."));

            string stage = "preflight";
            Part? part = null;
            bool pauseAttempted = false;
            bool localUpdated = false;
            bool restored = false;
            try
            {
                part = _service.GetPartById(id);
                if (part?.ShopifyProductId.HasValue != true
                    || !part.ShopifyVariantId.HasValue
                    || !part.ShopifyInventoryItemId.HasValue)
                    return Conflict(new ErrorResponse("Part or complete Shopify mapping missing. No changes made."));

                int currentId = part.ShippingPolicy?.Id ?? 0;
                string productGid = $"gid://shopify/Product/{part.ShopifyProductId.Value}";
                if (currentId != request.ExpectedCurrentShippingPolicyId
                    || currentId == request.TargetShippingPolicyId
                    || productGid != request.ExpectedShopifyProductGid)
                    return Conflict(new ErrorResponse("Part shipping policy or Shopify ID changed. No changes made."));

                if (!await _shopifyAdminService.UsesMarketDrivenShippingAsync())
                    return Conflict(new ErrorResponse("Store does not use Market-driven Shipping."));

                if (!shopifyOptions.Value.AllowMarketDrivenShippingPublishing)
                    return Conflict(new ErrorResponse("Market-driven shipping publishing is disabled."));

                List<ShippingPolicy> policies = policiesService.GetAll() ?? new();
                ShippingPolicy? current = policies.FirstOrDefault(p => p.Id == currentId);
                ShippingPolicy? target = policies.FirstOrDefault(p =>
                    p.Id == request.TargetShippingPolicyId && p.IsActive && p.AllowsOnlineCheckout);
                if (current?.AllowsOnlineCheckout != true
                    || target == null
                    || string.IsNullOrWhiteSpace(current.ShopifyShippingCollectionGid)
                    || string.IsNullOrWhiteSpace(target.ShopifyShippingCollectionGid))
                    return Conflict(new ErrorResponse("Both policies must be mapped checkout policies. No changes made."));

                // Unlike the standalone audit, require every active checkout policy
                // to be mapped and every manual source fully inspected.
                List<ShippingPolicy> checkoutPolicies = policies
                    .Where(p => p.IsActive && p.AllowsOnlineCheckout).ToList();
                if (checkoutPolicies.Any(p => string.IsNullOrWhiteSpace(p.ShopifyShippingCollectionGid)))
                    return Conflict(new ErrorResponse("Active checkout policies are unmapped. No changes made."));

                foreach (var gid in checkoutPolicies.Select(p => p.ShopifyShippingCollectionGid!)
                    .Distinct(StringComparer.Ordinal))
                {
                    ShopifyShippingMembershipAudit? audit =
                        await _shopifyAdminService.AuditShippingCollectionMembershipAsync(gid);
                    if (audit == null || audit.CollectionGid != gid
                        || audit.Sources.Any(source =>
                            source.SourceType != "CollectionConditionsSource" || source.MoreManualSelections))
                        return Conflict(new ErrorResponse(
                            "Cannot prove all manual memberships absent; use the read-only plan. No changes made."));
                    if (audit.Sources.Any(source => source.ManualSelectionProductGids.Contains(productGid)))
                        return Conflict(new ErrorResponse(
                            "Manual collection memberships require cleanup before reassignment. No changes made."));
                }

                ShopifyShippingCollectionInfo? destination =
                    await _shopifyAdminService.GetShippingCollectionAsync(target.ShopifyShippingCollectionGid);
                bool validDestination = destination?.Sources.Any(s =>
                    s.SourceType == "CollectionConditionsSource"
                    && s.TargetType == "PRODUCTS"
                    && s.InclusionConditionCount == 1
                    && !s.HasExplicitSelections && !s.HasExclusions
                    && s.TagConditions.Count == 1
                    && s.TagConditions[0].Relation == "TAGGED_WITH"
                    && s.TagConditions[0].MatchType == "ANY"
                    && s.TagConditions[0].Values.Count == 1
                    && string.Equals(s.TagConditions[0].Values[0],
                        $"ShippingClass_{target.Id}", StringComparison.OrdinalIgnoreCase)) == true;
                if (!validDestination)
                    return Conflict(new ErrorResponse("Target collection lacks the expected automatic tag source."));

                ShopifyProductShippingSnapshot original =
                    await _shopifyAdminService.GetProductShippingSnapshotAsync(part.ShopifyProductId.Value);
                if (original.ProductGid != productGid
                    || original.Status != "ACTIVE"
                    || !original.PublishedOnOnlineStore
                    || original.ShippingClassTags.Count != 1
                    || !string.Equals(original.ShippingClassTags[0],
                        $"ShippingClass_{currentId}", StringComparison.OrdinalIgnoreCase))
                    return Conflict(new ErrorResponse(
                        "This first controlled operation requires an ACTIVE, published product with exactly the old shipping tag. No changes made."));

                if (!await _shopifyAdminService.CollectionContainsProductAsync(
                        current.ShopifyShippingCollectionGid, productGid))
                    return Conflict(new ErrorResponse(
                        "Product is not in its current mapped shipping collection. No changes made."));

                if (await _shopifyAdminService.CollectionContainsProductAsync(
                        target.ShopifyShippingCollectionGid, productGid))
                    return Conflict(new ErrorResponse(
                        "Product already belongs to its destination collection. Inspect before changing."));

                // No writes happen before all above checks pass.
                stage = "unpublish";
                pauseAttempted = true;
                await _shopifyAdminService.UnpublishProductForPartAsync(part);
                ShopifyProductShippingSnapshot paused =
                    await _shopifyAdminService.GetProductShippingSnapshotAsync(part.ShopifyProductId.Value);
                if (paused.Status != "DRAFT" || paused.PublishedOnOnlineStore)
                    throw new ApplicationException("Product could not be verified as draft and unpublished.");

                stage = "sync_target_tag";
                part.ShippingPolicy = target; // in-memory only; no SQL changed yet
                await _shopifyAdminService.SyncProductTagsForPartAsync(part);
                ShopifyProductShippingSnapshot tagged =
                    await _shopifyAdminService.GetProductShippingSnapshotAsync(part.ShopifyProductId.Value);
                if (tagged.Status != "DRAFT" || tagged.PublishedOnOnlineStore
                    || tagged.ShippingClassTags.Count != 1
                    || !string.Equals(tagged.ShippingClassTags[0],
                        $"ShippingClass_{target.Id}", StringComparison.OrdinalIgnoreCase))
                    throw new ApplicationException("Shopify tag or draft status did not verify.");

                stage = "verify_collection_membership";
                bool verified = false;
                for (int attempt = 0; attempt < 8; attempt++)
                {
                    bool inNew = await _shopifyAdminService.CollectionContainsProductAsync(
                        target.ShopifyShippingCollectionGid, productGid);
                    bool inOld = await _shopifyAdminService.CollectionContainsProductAsync(
                        current.ShopifyShippingCollectionGid, productGid);
                    if (inNew && !inOld)
                    {
                        verified = true;
                        break;
                    }
                    if (attempt < 7)
                        await Task.Delay(TimeSpan.FromSeconds(1));
                }
                if (!verified)
                    throw new ApplicationException(
                        "Shopify collection membership did not settle correctly; leave unpublished.");

                // Optimistic re-read: detect another admin changing SQL during
                // Shopify writes. A durable distributed lock is still needed
                // before a production rollout.
                stage = "sql_commit";
                Part latest = _service.GetPartById(id);
                if (latest.ShippingPolicy?.Id != currentId
                    || latest.ShopifyProductId != part.ShopifyProductId)
                    throw new ApplicationException("SQL product changed during Shopify reassignment.");

                _service.PatchPart(id,
                    new PartPatchRequest { ShippingPolicyId = target.Id }, user.Id);
                localUpdated = true;
                Part afterPatch = _service.GetPartById(id);
                if (afterPatch.ShippingPolicy?.Id != target.Id)
                    throw new ApplicationException("SQL shipping policy did not verify after update.");

                // Only restore checkout after SQL and BOTH Shopify memberships verify.
                stage = "restore_publication";
                await _shopifyAdminService.PublishProductForPartAsync(afterPatch);
                ShopifyProductShippingSnapshot finalSnapshot =
                    await _shopifyAdminService.GetProductShippingSnapshotAsync(part.ShopifyProductId.Value);
                if (finalSnapshot.Status != "ACTIVE" || !finalSnapshot.PublishedOnOnlineStore
                    || finalSnapshot.ShippingClassTags.Count != 1
                    || !string.Equals(finalSnapshot.ShippingClassTags[0],
                        $"ShippingClass_{target.Id}", StringComparison.OrdinalIgnoreCase))
                    throw new ApplicationException("Post-publication verification failed.");
                restored = true;

                Logger.LogInformation(
                    "Verified Shopify Markets shipping reassignment for PartId {PartId}: {OldPolicy} -> {NewPolicy}.",
                    id, currentId, target.Id);
                return Ok(new ItemResponse<ShopifyShippingReassignmentResult>
                {
                    Item = new ShopifyShippingReassignmentResult
                    {
                        PartId = id,
                        PreviousPolicyId = currentId,
                        CurrentPolicyId = target.Id,
                        ShopifyProductGid = productGid,
                        ShopifyMembershipVerified = verified,
                        LocalPolicyUpdated = localUpdated,
                        PublicationRestored = restored,
                        OriginalPublishedOnOnlineStore = original.PublishedOnOnlineStore
                    }
                });
            }
            catch (Exception ex)
            {
                Logger.LogError(ex,
                    "Shopify Markets shipping reassignment failed at {Stage} for PartId {PartId}. SQL policy saved: {LocalUpdated}; publication restored: {Restored}.",
                    stage, id, localUpdated, restored);

                // If a write was attempted, enforce DRAFT as a final defensive
                // action. Unpublish can fail partially: surface that uncertainty.
                bool unpublishedConfirmed = !pauseAttempted;
                if (pauseAttempted && part?.ShopifyProductId.HasValue == true)
                {
                    try
                    {
                        await _shopifyAdminService.UnpublishProductForPartAsync(part);
                        var snap = await _shopifyAdminService.GetProductShippingSnapshotAsync(
                            part.ShopifyProductId.Value);
                        unpublishedConfirmed = snap.Status == "DRAFT" && !snap.PublishedOnOnlineStore;
                    }
                    catch (Exception safetyEx)
                    {
                        Logger.LogCritical(safetyEx,
                            "Failed protective Shopify unpublish after shipping reassignment error for PartId {PartId}.",
                            id);
                    }
                }
                return StatusCode(502, new ErrorResponse(
                    $"Shipping reassignment stopped at '{stage}'. SQL policy saved: {localUpdated}. " +
                    (unpublishedConfirmed
                        ? "Any affected product was left unpublished for review. "
                        : "URGENT: Shopify publication state could not be verified. Inspect the product immediately. ") +
                    "Do not retry blindly; inspect Shopify and SQL before recovery."));
            }
        }


        // One-off DEV recovery for the interrupted Part #169 reassignment.
        // Shopify already has ShippingClass_19 and verified memberships, but SQL
        // still says policy 9. This endpoint NEVER changes Shopify tags/sources.
        // It is deliberately scoped to the DEV store/product and off by default.
        [HttpPost("{id:int}/shopify/shipping-reassign-recover")]
        [Authorize(Policy = "PartsDelete")]
        public async Task<ActionResult<ItemResponse<ShopifyShippingReassignmentResult>>> RecoverDevShippingReassignment(
            int id, [FromBody] ShopifyShippingReassignmentRequest request,
            [FromServices] IShippingPoliciesService policiesService,
            [FromServices] Microsoft.Extensions.Options.IOptions<ShopifySettings> shopifyOptions)
        {
            const string devShop = "gr-sons-shipping-dev.myshopify.com";
            const string expectedProductGid = "gid://shopify/Product/15424506527964";
            const string expectedOldCollection = "gid://shopify/Collection/511152357596";
            const string expectedNewCollection = "gid://shopify/Collection/511160320220";

            if (!shopifyOptions.Value.AllowMarketDrivenShippingRecoveryWrites)
                return Conflict(new ErrorResponse(
                    "DEV shipping-reassignment recovery is disabled. No changes made."));

            if (!string.Equals(shopifyOptions.Value.ShopDomain, devShop,
                    StringComparison.OrdinalIgnoreCase))
                return Conflict(new ErrorResponse(
                    "This recovery operation is restricted to the Shopify DEV store."));

            if (id != 169 || request == null
                || request.ExpectedCurrentShippingPolicyId != 9
                || request.TargetShippingPolicyId != 19
                || request.ExpectedShopifyProductGid != expectedProductGid
                || request.Confirmation != "RECOVER_PART_169_SHIPPING_9_TO_19")
                return BadRequest(new ErrorResponse(
                    "DEV Part #169 recovery requires the exact expected IDs and confirmation."));

            var user = _authService.GetCurrentUser();
            if (user == null)
                return Unauthorized(new ErrorResponse("Admin authentication required."));

            string stage = "preflight";
            bool sqlWriteAttempted = false;
            bool publicationAttempted = false;
            try
            {
                if (!shopifyOptions.Value.AllowMarketDrivenShippingPublishing
                    || !await _shopifyAdminService.UsesMarketDrivenShippingAsync())
                    return Conflict(new ErrorResponse(
                        "Shopify Markets publishing or Market-driven Shipping is not enabled."));

                Part part = _service.GetPartById(id);
                if (part?.ShopifyProductId != 15424506527964L
                    || !part.ShopifyVariantId.HasValue
                    || !part.ShopifyInventoryItemId.HasValue
                    || part.ShippingPolicy?.Id != 9)
                    return Conflict(new ErrorResponse(
                        "Part is no longer in the expected SQL recovery state. No changes made."));

                List<ShippingPolicy> policies = policiesService.GetAll() ?? new();
                ShippingPolicy? oldPolicy = policies.FirstOrDefault(p => p.Id == 9);
                ShippingPolicy? newPolicy = policies.FirstOrDefault(p => p.Id == 19);
                if (oldPolicy?.AllowsOnlineCheckout != true
                    || newPolicy?.IsActive != true
                    || newPolicy.AllowsOnlineCheckout != true
                    || oldPolicy.ShopifyShippingCollectionGid != expectedOldCollection
                    || newPolicy.ShopifyShippingCollectionGid != expectedNewCollection)
                    return Conflict(new ErrorResponse(
                        "Expected DEV shipping policy mappings changed. No changes made."));

                ShopifyShippingCollectionInfo? destination =
                    await _shopifyAdminService.GetShippingCollectionAsync(expectedNewCollection);
                bool targetRuleConfirmed = destination?.Sources.Any(source =>
                    source.SourceType == "CollectionConditionsSource"
                    && source.TargetType == "PRODUCTS"
                    && !source.HasExclusions
                    && !source.HasExplicitSelections
                    && source.InclusionConditionCount == 1
                    && source.TagConditions.Count == 1
                    && source.TagConditions[0].Relation == "TAGGED_WITH"
                    && source.TagConditions[0].MatchType == "ANY"
                    && source.TagConditions[0].Values.Count == 1
                    && string.Equals(source.TagConditions[0].Values[0],
                        "ShippingClass_19", StringComparison.OrdinalIgnoreCase)) == true;
                if (!targetRuleConfirmed)
                    return Conflict(new ErrorResponse(
                        "Destination collection's automatic tag rule is no longer verified. No changes made."));

                ShopifyProductShippingSnapshot snapshot =
                    await _shopifyAdminService.GetProductShippingSnapshotAsync(15424506527964L);
                if (snapshot.ProductGid != expectedProductGid
                    || snapshot.Status != "DRAFT"
                    || snapshot.PublishedOnOnlineStore
                    || snapshot.ShippingClassTags.Count != 1
                    || !string.Equals(snapshot.ShippingClassTags[0],
                        "ShippingClass_19", StringComparison.OrdinalIgnoreCase))
                    return Conflict(new ErrorResponse(
                        "Shopify product is not safely unpublished with exactly ShippingClass_19. No changes made."));

                // All active checkout policies must have mapped Shopify collections,
                // and every manual source must be fully scanned. A product in ANY
                // other checkout collection is a hard stop, not just the old policy.
                List<ShippingPolicy> checkoutPolicies = policies
                    .Where(p => p.IsActive && p.AllowsOnlineCheckout).ToList();
                if (checkoutPolicies.Any(p => string.IsNullOrWhiteSpace(p.ShopifyShippingCollectionGid)))
                    return Conflict(new ErrorResponse(
                        "At least one active checkout policy is unmapped. No changes made."));

                foreach (string collectionGid in checkoutPolicies
                    .Select(p => p.ShopifyShippingCollectionGid!)
                    .Distinct(StringComparer.Ordinal))
                {
                    ShopifyShippingMembershipAudit? audit =
                        await _shopifyAdminService.AuditShippingCollectionMembershipAsync(collectionGid);
                    if (audit == null || audit.CollectionGid != collectionGid
                        || audit.Sources.Any(source =>
                            source.SourceType != "CollectionConditionsSource"
                            || source.MoreManualSelections
                            || source.ManualSelectionProductGids.Contains(expectedProductGid)))
                        return Conflict(new ErrorResponse(
                            "Manual source membership cannot be ruled out for all checkout collections. No changes made."));

                    bool contains = await _shopifyAdminService.CollectionContainsProductAsync(
                        collectionGid, expectedProductGid);
                    if (contains != (collectionGid == expectedNewCollection))
                        return Conflict(new ErrorResponse(
                            "Shopify membership differs from the verified destination-only state. No changes made."));
                }

                // Final pre-commit recheck. Never update SQL if Shopify/publication
                // changed while we were scanning the checkout collections.
                stage = "final_precommit";
                Part latest = _service.GetPartById(id);
                ShopifyProductShippingSnapshot latestShopify =
                    await _shopifyAdminService.GetProductShippingSnapshotAsync(15424506527964L);
                if (latest.ShippingPolicy?.Id != 9
                    || latest.ShopifyProductId != 15424506527964L
                    || latestShopify.Status != "DRAFT"
                    || latestShopify.PublishedOnOnlineStore
                    || latestShopify.ShippingClassTags.Count != 1
                    || !string.Equals(latestShopify.ShippingClassTags[0],
                        "ShippingClass_19", StringComparison.OrdinalIgnoreCase))
                    return Conflict(new ErrorResponse(
                        "SQL or Shopify changed during recovery preflight. No changes made."));

                stage = "sql_commit";
                sqlWriteAttempted = true;
                _service.PatchPart(id, new PartPatchRequest { ShippingPolicyId = 19 }, user.Id);
                Part updated = _service.GetPartById(id);
                if (updated.ShippingPolicy?.Id != 19
                    || updated.ShopifyProductId != 15424506527964L)
                    throw new ApplicationException(
                        "SQL shipping policy could not be verified after update.");

                // Keep the listing draft until the final checks are complete.
                stage = "pre_publish_verification";
                ShopifyProductShippingSnapshot beforePublishing =
                    await _shopifyAdminService.GetProductShippingSnapshotAsync(15424506527964L);
                if (beforePublishing.Status != "DRAFT"
                    || beforePublishing.PublishedOnOnlineStore
                    || beforePublishing.ShippingClassTags.Count != 1
                    || !string.Equals(beforePublishing.ShippingClassTags[0],
                        "ShippingClass_19", StringComparison.OrdinalIgnoreCase)
                    || !await _shopifyAdminService.CollectionContainsProductAsync(
                        expectedNewCollection, expectedProductGid)
                    || await _shopifyAdminService.CollectionContainsProductAsync(
                        expectedOldCollection, expectedProductGid))
                    throw new ApplicationException(
                        "Shopify publication or collection membership changed after SQL commit.");

                stage = "restore_publication";
                publicationAttempted = true;
                await _shopifyAdminService.PublishProductForPartAsync(updated);
                ShopifyProductShippingSnapshot published =
                    await _shopifyAdminService.GetProductShippingSnapshotAsync(15424506527964L);
                if (published.Status != "ACTIVE"
                    || !published.PublishedOnOnlineStore
                    || published.ShippingClassTags.Count != 1
                    || !string.Equals(published.ShippingClassTags[0],
                        "ShippingClass_19", StringComparison.OrdinalIgnoreCase)
                    || !await _shopifyAdminService.CollectionContainsProductAsync(
                        expectedNewCollection, expectedProductGid)
                    || await _shopifyAdminService.CollectionContainsProductAsync(
                        expectedOldCollection, expectedProductGid))
                    throw new ApplicationException("Published product failed final shipping-state verification.");

                Logger.LogInformation(
                    "Completed DEV recovery for Part #169: SQL policy 9 to 19; Shopify publication restored.");
                return Ok(new ItemResponse<ShopifyShippingReassignmentResult>
                {
                    Item = new ShopifyShippingReassignmentResult
                    {
                        PartId = id,
                        PreviousPolicyId = 9,
                        CurrentPolicyId = 19,
                        ShopifyProductGid = expectedProductGid,
                        ShopifyMembershipVerified = true,
                        LocalPolicyUpdated = true,
                        PublicationRestored = true,
                        OriginalPublishedOnOnlineStore = true
                    }
                });
            }
            catch (Exception ex)
            {
                Logger.LogError(ex,
                    "DEV Part #169 shipping recovery failed at {Stage}. SQL write attempted: {SqlWriteAttempted}; publication attempted: {PublicationAttempted}.",
                    stage, sqlWriteAttempted, publicationAttempted);

                // Any uncertainty after a write must keep the product out of
                // checkout. Check the actual SQL state separately; a SQL
                // exception can happen after a successful server-side commit.
                bool draftVerified = false;
                if (sqlWriteAttempted)
                {
                    try
                    {
                        Part? part = _service.GetPartById(169);
                        if (part?.ShopifyProductId == 15424506527964L)
                            await _shopifyAdminService.UnpublishProductForPartAsync(part);
                        ShopifyProductShippingSnapshot safe =
                            await _shopifyAdminService.GetProductShippingSnapshotAsync(15424506527964L);
                        draftVerified = safe.Status == "DRAFT" && !safe.PublishedOnOnlineStore;
                    }
                    catch (Exception safetyEx)
                    {
                        Logger.LogCritical(safetyEx,
                            "URGENT: Could not verify DEV Part #169 remained unpublished after recovery failure.");
                    }
                }
                return StatusCode(502, new ErrorResponse(
                    $"DEV shipping recovery stopped at '{stage}'. " +
                    (sqlWriteAttempted
                        ? "SQL commit may have occurred; read SQL state before any retry. "
                        : "No SQL update was attempted. ") +
                    (sqlWriteAttempted && !draftVerified
                        ? "URGENT: Shopify publication could not be verified; inspect immediately. "
                        : "Any Shopify listing changes were kept unpublished. ") +
                    "Do not repeat this request blindly."));
            }
        }

        [HttpPatch("{id:int}")]
        [Authorize(Policy = "AdminAction")]
        public async Task<ActionResult<BaseResponse>> UpdatePart(int id, [FromBody] PartPatchRequest model)
        {
            int code = 200;
            BaseResponse response;

            try
            {
                var user = _authService.GetCurrentUser();
                if (user == null)
                {
                    code = 401;
                    return StatusCode(code, new ErrorResponse("You must be logged in."));
                }

                if (model == null)
                {
                    code = 400;
                    return StatusCode(code, new ErrorResponse("Patch payload is required."));
                }

                // Normalize strings. OtherBox/AdminNotes deliberately preserve an
                // empty string so an admin can clear those optional fields.
                model.Name = model.Name?.Trim();
                model.PartNumber = model.PartNumber?.Trim();
                model.Brand = model.Brand?.Trim();
                model.Description = model.Description?.Trim();
                model.Image = string.IsNullOrWhiteSpace(model.Image) ? null : model.Image.Trim();
                model.OtherBox = model.OtherBox?.Trim();
                model.AdminNotes = model.AdminNotes?.Trim();
                model.Year = model.Year?.Trim();

                if (model.Quantity.HasValue && model.Quantity.Value < 0)
                {
                    code = 400;
                    return StatusCode(code, new ErrorResponse("Quantity cannot be negative."));
                }

                // Reject empty patch (no-op)
                if (!HasAnyPatchField(model))
                {
                    code = 400;
                    return StatusCode(code, new ErrorResponse("At least one field is required to patch."));
                }

                if (model.Name != null && (model.Name.Length < 2 || model.Name.Length > 128))
                {
                    code = 400;
                    return StatusCode(code, new ErrorResponse("Part name must be between 2 and 128 characters."));
                }

                if (model.PartNumber != null && (model.PartNumber.Length < 2 || model.PartNumber.Length > 128))
                {
                    code = 400;
                    return StatusCode(code, new ErrorResponse("Part number must be between 2 and 128 characters."));
                }

                if (model.Brand != null && model.Brand.Length > 128)
                {
                    code = 400;
                    return StatusCode(code, new ErrorResponse("Brand cannot exceed 128 characters."));
                }

                if (model.Description != null && model.Description.Length < 2)
                {
                    code = 400;
                    return StatusCode(code, new ErrorResponse("Description must be at least 2 characters."));
                }

                if (model.Year != null && model.Year.Length > 50)
                {
                    code = 400;
                    return StatusCode(code, new ErrorResponse("Year(s) cannot exceed 50 characters."));
                }

                if (model.OtherBox != null && model.OtherBox.Length > 100)
                {
                    code = 400;
                    return StatusCode(code, new ErrorResponse("Other Box cannot exceed 100 characters."));
                }

                if (model.Categories != null)
                {
                    if (model.Categories.Count == 0)
                    {
                        code = 400;
                        return StatusCode(code, new ErrorResponse("At least one category is required."));
                    }

                    if (model.Categories.Any(category => category.CatagoryId <= 0))
                    {
                        code = 400;
                        return StatusCode(code, new ErrorResponse("Every category must have a valid category ID."));
                    }

                    if (model.Categories
                        .GroupBy(category => category.CatagoryId)
                        .Any(group => group.Count() > 1))
                    {
                        code = 400;
                        return StatusCode(code, new ErrorResponse("A category cannot be assigned more than once."));
                    }
                }

                if (model.Fitments != null)
                {
                    if (model.Fitments.Count == 0)
                    {
                        code = 400;
                        return StatusCode(code, new ErrorResponse("At least one make/model fitment is required."));
                    }

                    foreach (PartFitmentAddRequest fitment in model.Fitments)
                    {
                        if (fitment.MakeId <= 0)
                        {
                            code = 400;
                            return StatusCode(code, new ErrorResponse("Every fitment must have a valid make/model."));
                        }

                        bool hasStart = fitment.YearStart.HasValue;
                        bool hasEnd = fitment.YearEnd.HasValue;

                        if (hasStart != hasEnd)
                        {
                            code = 400;
                            return StatusCode(code, new ErrorResponse("Fitment start and end years must both be supplied, or both omitted."));
                        }

                        if (hasStart &&
                            (fitment.YearStart!.Value < 1900 || fitment.YearStart.Value > 3000 ||
                             fitment.YearEnd!.Value < 1900 || fitment.YearEnd.Value > 3000))
                        {
                            code = 400;
                            return StatusCode(code, new ErrorResponse("Fitment years must be between 1900 and 3000."));
                        }

                        if (hasStart && fitment.YearStart!.Value > fitment.YearEnd!.Value)
                        {
                            code = 400;
                            return StatusCode(code, new ErrorResponse("Fitment start year cannot be after the end year."));
                        }
                    }

                    if (model.Fitments
                        .GroupBy(fitment => new { fitment.MakeId, fitment.YearStart, fitment.YearEnd })
                        .Any(group => group.Count() > 1))
                    {
                        code = 400;
                        return StatusCode(code, new ErrorResponse("Duplicate fitments are not allowed."));
                    }
                }

                // Validate manual fields only (high ROI)
                if (model.Price.HasValue)
                {
                    if (model.Price.Value < 0.01m || model.Price.Value > 1_000_000m)
                    {
                        code = 400;
                        return StatusCode(code, new ErrorResponse("Price must be between 0.01 and 1,000,000."));
                    }

                    if (decimal.Round(model.Price.Value, 2) != model.Price.Value)
                    {
                        code = 400;
                        return StatusCode(code, new ErrorResponse("Price can have at most 2 decimal places."));
                    }
                }

                if (model.AdminNotes != null && model.AdminNotes.Length > 2000)
                {
                    code = 400;
                    return StatusCode(code, new ErrorResponse("Admin Notes cannot exceed 2000 characters."));
                }

                if (model.Description != null && model.Description.Length > 4000)
                {
                    code = 400;
                    return StatusCode(code, new ErrorResponse("Description cannot exceed 4000 characters."));
                }

                if (model.Image != null && model.Image.Length > 260)
                {
                    code = 400;
                    return StatusCode(code, new ErrorResponse("Image path cannot exceed 260 characters."));
                }

                Part existingPart = _service.GetPartById(id);

                if (existingPart == null)
                {
                    code = 404;
                    return StatusCode(code, new ErrorResponse("Part not found."));
                }

                // Market-driven shipping still has migrated manual collection
                // membership. A tag update alone does NOT safely reassign it.
                // Block a policy change before SQL is saved, until the complete
                // unpublish/membership/verification workflow has been built.
                if (model.ShippingPolicyId.HasValue
                    && existingPart.ShopifyProductId.HasValue
                    && model.ShippingPolicyId.Value != existingPart.ShippingPolicy?.Id)
                {
                    bool marketDriven;
                    try
                    {
                        marketDriven = await _shopifyAdminService.UsesMarketDrivenShippingAsync();
                    }
                    catch (Exception ex)
                    {
                        Logger.LogError(ex,
                            "Unable to verify Shopify shipping mode before changing policy for PartId {PartId}.",
                            id);
                        return StatusCode(503, new ErrorResponse(
                            "Could not verify Shopify shipping mode. No part changes were saved."));
                    }

                    if (marketDriven)
                    {
                        return StatusCode(409, new ErrorResponse(
                            "Shipping-policy reassignment is not yet enabled for Market-driven Shipping. " +
                            "No part changes were saved. Use the read-only shipping-reassignment-plan endpoint first."));
                    }
                }

                _service.PatchPart(id, model, user.Id);

                PartPatchResult result = new PartPatchResult
                {
                    LocalUpdated = true,
                    ShopifySyncAttempted = false,
                    ShopifySyncSucceeded = false
                };

                // Product-facing changes are automatically pushed to Shopify.
                // Condition changes are especially important because they change
                // the managed tags used by automated discount collections.
                bool shouldSyncProductDetails =
                    model.Quantity.HasValue ||
                    model.Price.HasValue ||
                    model.Name != null ||
                    model.PartNumber != null ||
                    model.Description != null ||
                    model.ConditionId.HasValue ||
                    model.Categories != null ||
                    model.Fitments != null ||
                    model.Year != null;

                if (shouldSyncProductDetails || model.ShippingPolicyId.HasValue)
                {
                    Part updatedPart = _service.GetPartById(id);

                    bool hasShopifyProduct =
                        updatedPart?.ShopifyProductId.HasValue == true;

                    bool hasCompleteShopifyMapping =
                        hasShopifyProduct &&
                        updatedPart!.ShopifyVariantId.HasValue &&
                        updatedPart.ShopifyInventoryItemId.HasValue;

                    bool isContactOnly =
                        updatedPart?.ShippingPolicy?.AllowsOnlineCheckout == false;

                    if (hasShopifyProduct)
                    {
                        result.ShopifySyncAttempted = true;

                        try
                        {
                            // Changing to Calculated shipping makes the listing
                            // contact-only. Immediately move any Shopify product
                            // back to Draft so it cannot be purchased directly.
                            if (model.ShippingPolicyId.HasValue && isContactOnly)
                            {
                                await _shopifyAdminService
                                    .UnpublishProductForPartAsync(updatedPart!);
                            }
                            else if (model.ShippingPolicyId.HasValue &&
                                     hasCompleteShopifyMapping)
                            {
                                await _shopifyPartSyncService
                                    .SyncShippingProfileForPartAsync(id);
                            }

                            // Shipping-only changes still need to refresh the Shopify
                            // shipping-class tag. Product-detail sync below covers
                            // the cases that also changed other product fields.
                            if (model.ShippingPolicyId.HasValue && !shouldSyncProductDetails)
                            {
                                await _shopifyAdminService.SyncProductTagsForPartAsync(updatedPart!);
                            }

                            ShopifyProductInventorySyncResult? shopifyResult = null;

                            if (shouldSyncProductDetails &&
                                hasCompleteShopifyMapping)
                            {
                                shopifyResult = await _shopifyAdminService
                                    .SyncProductDetailsForPartAsync(updatedPart!);
                            }

                            result.ShopifySyncSucceeded = true;
                            result.ShopifyQuantity = shopifyResult?.Quantity;
                        }
                        catch (Exception shopifyEx)
                        {
                            result.Warning =
                                "The local change was saved, but Shopify did not sync. Use Sync with Shopify to retry.";

                            Logger.LogError(
                                shopifyEx,
                                "Automatic Shopify sync failed after local update for PartId {PartId}.",
                                id);
                        }
                    }
                }

                response = new ItemResponse<PartPatchResult>
                {
                    Item = result
                };
            }
            catch (Exception ex) when (IsFkViolation(ex))
            {
                code = 400;
                response = new ErrorResponse("One or more referenced IDs are invalid (category, make/model, location, availability, condition, or shipping policy).");
            }
            catch (Exception ex)
            {
                code = 500;
                base.Logger.LogError(ex.ToString());
                response = new ErrorResponse("An unexpected error occurred.");
            }


            return StatusCode(code, response);
        }

        private static bool HasAnyPatchField(PartPatchRequest m)
        {
            return m.Price.HasValue
                || m.AvailableId.HasValue
                || m.Quantity.HasValue
                || m.LocationId.HasValue
                || m.ConditionId.HasValue
                || m.ShippingPolicyId.HasValue
                || m.Name != null
                || m.PartNumber != null
                || m.Brand != null
                || m.Description != null
                || !string.IsNullOrWhiteSpace(m.Image)
                || m.OtherBox != null
                || m.AdminNotes != null
                || m.Year != null
                || m.Categories != null
                || m.Fitments != null;

        }

        private static bool IsFkViolation(Exception ex)
        {
            // Walk the exception chain to look for SQL Server FK violation signature.
            // SQL Server FK violations often contain: "FOREIGN KEY constraint" and/or "conflicted with the FOREIGN KEY constraint"
            // If your data layer wraps exceptions, this still works.
            Exception current = ex;
            while (current != null)
            {
                string msg = current.Message ?? string.Empty;
                if (msg.IndexOf("FOREIGN KEY constraint", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    msg.IndexOf("conflicted with the FOREIGN KEY constraint", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return true;
                }

                current = current.InnerException;
            }

            return false;
        }


        [HttpGet("available/admin")]
        [Authorize(Policy = "AdminAction")]
        public ActionResult<ItemResponse<Paged<PartSummary>>> GetAvailablePaginated(int pageIndex, int pageSize)
        {
            int code = 200;
            BaseResponse response;
            int availableId = 1;

            try
            {
                Paged<PartSummary> pages = _service.GetAvailablePaginated(pageIndex, pageSize, availableId);

                if (pages == null)
                {
                    code = 404;
                    response = new ErrorResponse("App Resource not found.");
                }
                else
                {
                    response = new ItemResponse<Paged<PartSummary>> { Item = pages };
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

        [HttpGet("model/{modelId:int}")]
        public ActionResult<ItemResponse<Paged<PartCustomerSummary>>> GetByModelCustomer(int pageIndex, int pageSize, int modelId)
        {
            int code = 200;
            BaseResponse response;

            try
            {
                Paged<PartCustomerSummary> pages = _service.GetByModelPaginatedCustomer(pageIndex, pageSize, modelId);

                if (pages == null)
                {
                    code = 404;
                    response = new ErrorResponse("Parts not found.");
                }
                else
                {
                    response = new ItemResponse<Paged<PartCustomerSummary>> { Item = pages };
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

        [HttpGet("model/{modelId:int}/admin")]
        [Authorize(Policy = "AdminAction")]
        public ActionResult<ItemResponse<Paged<PartSummary>>> GetByModelAdmin(int pageIndex, int pageSize, int modelId)
        {
            int code = 200;
            BaseResponse response;

            try
            {
                Paged<PartSummary> pages = _service.GetByModelPaginated(pageIndex, pageSize, modelId);

                if (pages == null)
                {
                    code = 404;
                    response = new ErrorResponse("Parts not found.");
                }
                else
                {
                    response = new ItemResponse<Paged<PartSummary>> { Item = pages };
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

        [HttpGet("category/{categoryId:int}")]
        public ActionResult<ItemResponse<Paged<PartCustomerSummary>>> GetByCategoryCustomer(int pageIndex, int pageSize, int categoryId)
        {
            int code = 200;
            BaseResponse response;

            try
            {
                Paged<PartCustomerSummary> pages = _service.GetByCategoryPaginatedCustomer(pageIndex, pageSize, categoryId);

                if (pages == null)
                {
                    code = 404;
                    response = new ErrorResponse("Parts not found.");
                }
                else
                {
                    response = new ItemResponse<Paged<PartCustomerSummary>> { Item = pages };
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

        [HttpGet("category/{categoryId:int}/admin")]
        [Authorize(Policy = "AdminAction")]
        public ActionResult<ItemResponse<Paged<PartSummary>>> GetByCategoryAdmin(int pageIndex, int pageSize, int categoryId)
        {
            int code = 200;
            BaseResponse response;

            try
            {
                Paged<PartSummary> pages = _service.GetByCategoryPaginated(pageIndex, pageSize, categoryId);

                if (pages == null)
                {
                    code = 404;
                    response = new ErrorResponse("Parts not found.");
                }
                else
                {
                    response = new ItemResponse<Paged<PartSummary>> { Item = pages };
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
        //New API call for paginated parts
        [HttpGet("/api/parts/customer/paginate")]
        public ActionResult<ItemResponse<Paged<PartCustomerSummary>>> GetCustomerPaginated(int pageIndex, int pageSize)
        {
            int code = 200;
            BaseResponse response;

            try
            {
                const int availableId = 1;
                Paged<PartCustomerSummary> pages = _service.GetAvailablePaginatedForCustomer(pageIndex, pageSize, availableId);

                if (pages == null)
                {
                    code = 404;
                    response = new ErrorResponse("Parts not found.");
                }
                else
                {
                    response = new ItemResponse<Paged<PartCustomerSummary>> { Item = pages };
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

        //Old API call for paginated parts DO NOT DELETE YET
        [HttpGet("available")]
        public ActionResult<ItemResponse<Paged<PartCustomerSummary>>> GetAvailablePaginatedCustomers(int pageIndex, int pageSize)
        {
            int code = 200;
            BaseResponse response;
            int availableId = 1;

            try
            {
                Paged<PartCustomerSummary> pages = _service.GetAvailablePaginatedForCustomer(pageIndex, pageSize, availableId);

                if (pages == null)
                {
                    code = 404;
                    response = new ErrorResponse("App Resource not found.");
                }
                else
                {
                    response = new ItemResponse<Paged<PartCustomerSummary>> { Item = pages };
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

        [HttpGet("stock")]
        [Authorize(Policy = "AdminAction")]
        public ActionResult<ItemResponse<Paged<PartSummary>>> GetPartsPaginated(int pageIndex, int pageSize)
        {
            int code = 200;
            BaseResponse response;

            try
            {
                Paged<PartSummary> pages = _service.GetPartsPaginated(pageIndex, pageSize);

                if (pages == null)
                {
                    code = 404;
                    response = new ErrorResponse("App Resource not found.");
                }
                else
                {
                    response = new ItemResponse<Paged<PartSummary>> { Item = pages };
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

        [HttpGet("admin/{id:int}")]
        [Authorize(Policy = "AdminAction")]
        public ActionResult<ItemResponse<Part>> GetPartsById(int id)
        {
            int code = 200;
            BaseResponse response;

            try
            {
                Part part = _service.GetPartById(id);

                if (part == null)
                {
                    code = 404;
                    response = new ErrorResponse("Not found.");
                }
                else
                {
                    response = new ItemResponse<Part> { Item = part };
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

        [HttpGet("part/{id:int}")]
        public ActionResult<ItemResponse<Part>> GetPartByIdCustomer(int id)
        {
            int code = 200;
            BaseResponse response;

            try
            {
                Part part = _service.GetPartByIdCustomer(id);

                if (part == null)
                {
                    code = 404;
                    response = new ErrorResponse("Not found.");
                }
                else
                {
                    response = new ItemResponse<Part> { Item = part };
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

        [HttpGet("search")]
        [Authorize(Policy = "AdminAction")]
        public ActionResult<ItemResponse<List<PartSearchResult>>> Search([FromQuery] PartSearchRequest model)
        {
            int code = 200;
            BaseResponse response;

            try
            {
                List<PartSearchResult> items = _service.Search(model) ?? new List<PartSearchResult>();
                response = new ItemResponse<List<PartSearchResult>> { Item = items };
            }
            catch (Exception ex)
            {
                code = 500;
                base.Logger.LogError(ex.ToString());
                response = new ErrorResponse(ex.Message);
            }

            return StatusCode(code, response);
        }

        [HttpGet("search/customer")]
        public ActionResult<ItemResponse<Paged<PartCustomerSummary>>> SearchCustomer(int pageIndex, int pageSize, [FromQuery] CustomerSearchRequest model)
        {
            int code = 200;
            BaseResponse response;

            try
            {
                Paged<PartCustomerSummary> pages = _service.SearchCustomer(pageIndex, pageSize, model);

                if (pages == null)
                {
                    code = 404;
                    response = new ErrorResponse("App Resource not found.");
                }
                else
                {
                    response = new ItemResponse<Paged<PartCustomerSummary>> { Item = pages };
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

        [HttpDelete("{id:int}")]
        [Authorize(Policy = "PartsDelete")]
        public ActionResult<SuccessResponse> Delete(int id)
        {
            int code = 200;
            BaseResponse response;

            try
            {
                _service.DeletePart(id);
                response = new SuccessResponse();
            }
            catch (Exception ex)
            {
                code = 500;
                base.Logger.LogError(ex.ToString());
                response = new ErrorResponse(ex.Message);
            }

            return StatusCode(code, response);
        }

        // Keep this as a debug endpoint if you still need it (Week 1: stability).
        [HttpGet("test-image")]
        public IActionResult GetTestImage()
        {
            var path = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "uploads", "items",
                "963835b6-bb43-494b-83b4-0102f3d6a86b.jpg");

            if (!System.IO.File.Exists(path))
            {
                return NotFound("File not found at: " + path);
            }

            try
            {
                return PhysicalFile(path, "image/jpeg");
            }
            catch (Exception ex)
            {
                base.Logger.LogError(ex.ToString());
                return StatusCode(500, $"Error sending file: {ex.Message}");
            }
        }
    }
}

