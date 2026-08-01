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

        public PartsApiController(
            IPartService service,
            IPartImageService partImageService,
            ILocationService locationService,
            IShopifyPartSyncService shopifyPartSyncService,
            IShopifyAdminService shopifyAdminService,
            ILogger<PartsApiController> logger,
            IAuthenticationService<IUserAuthData> authService,
            IWebHostEnvironment webHostEnvironment
        ) : base(logger)
        {
            _service = service;
            _partImageService = partImageService;
            _locationService = locationService;
            _shopifyPartSyncService = shopifyPartSyncService;
            _shopifyAdminService = shopifyAdminService;
            _authService = authService;
            _webHostEnvironment = webHostEnvironment;
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

                    string uploadsFolder = Path.Combine(_webHostEnvironment.WebRootPath, "uploads", "items");
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

                string uploadsFolder = Path.Combine(_webHostEnvironment.WebRootPath, "uploads", "items");
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

                // Normalize strings
                model.Description = string.IsNullOrWhiteSpace(model.Description) ? null : model.Description.Trim();
                model.Image = string.IsNullOrWhiteSpace(model.Image) ? null : model.Image.Trim();
                model.OtherBox = string.IsNullOrWhiteSpace(model.OtherBox) ? null : model.OtherBox.Trim();
                model.AdminNotes = string.IsNullOrWhiteSpace(model.AdminNotes) ? null : model.AdminNotes.Trim();

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
                    model.Description != null ||
                    model.ConditionId.HasValue;

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
                response = new ErrorResponse("Invalid LocationId or AvailableId.");
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
                || !string.IsNullOrWhiteSpace(m.Description)
                || !string.IsNullOrWhiteSpace(m.Image)
                || !string.IsNullOrWhiteSpace(m.OtherBox)
                || !string.IsNullOrWhiteSpace(m.AdminNotes)
                || !string.IsNullOrWhiteSpace(m.Year);

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

