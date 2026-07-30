using Site_2024.Models.Domain.Parts;
using Site_2024.Web.Api.Models;
using Site_2024.Web.Api.Models.Shopify;

namespace Site_2024.Web.Api.Services
{
    public interface IShopifyAdminService
    {
        Task<ShopifyCreateProductResult> CreateProductForPartAsync(Part part);
        Task<List<ShopifyLocationResult>> GetLocationsAsync();
        Task<List<ShopifyDeliveryProfileResult>> GetDeliveryProfilesAsync();
        Task AssignVariantToDeliveryProfileAsync(long variantId, long deliveryProfileId);
        Task<ShopifyProductInventorySyncResult> SyncProductDetailsForPartAsync(Part part);
        Task<ShopifyProductMediaSyncResult> SyncProductImagesAsync(Part part, IReadOnlyCollection<PartImage> images);
        Task<ShopifyDiscountCreateResult> CreateBasicDiscountCodeAsync(AdminDiscountCode discount);
        Task<ShopifyDiscountDeactivateResult> DeactivateDiscountCodeAsync(string shopifyDiscountGid);
        Task<ShopifyProductPublishResult> PublishProductForPartAsync(Part part);
        Task<ShopifyProductPublishResult> UnpublishProductForPartAsync(Part part);
        Task<ShopifyInventoryQuantityCommitResult> SetInventoryQuantityForRefundAsync(
            long inventoryItemId,
            int previousQuantity,
            int newQuantity,
            string idempotencyKey,
            string referenceDocumentUri);
    }
}