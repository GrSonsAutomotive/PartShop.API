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
        Task<bool> UsesMarketDrivenShippingAsync();
        Task<ShopifyCollectionCreateResult> CreateAutomatedShippingCollectionAsync(ShippingPolicy policy);
        Task<ShopifyShippingCollectionPage> GetShippingCollectionsAsync(string? after);
        Task<ShopifyShippingCollectionInfo?> GetShippingCollectionAsync(string collectionGid);
        Task<ShopifyShippingMembershipAudit?> AuditShippingCollectionMembershipAsync(string collectionGid);
        Task<Dictionary<string, string?>> GetSoleProductVariantsAsync(
            IReadOnlyList<string> productGids);
        Task<ShopifyShippingMembershipPage?> GetShippingMembershipPageAsync(
            string collectionGid, string? sourceGid, string kind, string? after);
        Task<ShopifyCollectionSourceCreateResult> AddShippingTagSourceAsync(string collectionGid, string shippingClassTag);
        Task<ShopifyShippingManualSelectionMutationResult> RemoveManualProductSelectionAsync(
            string collectionGid, string sourceGid, string productGid);
        Task<ShopifyShippingManualSelectionMutationResult> DeleteEmptyingManualSourceAsync(
            string collectionGid, string sourceGid);
        Task AssignVariantToDeliveryProfileAsync(long variantId, long deliveryProfileId);
        Task<ShopifyProductInventorySyncResult> SyncProductDetailsForPartAsync(Part part);
        Task SyncProductTagsForPartAsync(Part part);
        Task<bool> SyncOnlyShippingClassTagForPartAsync(Part part);
        Task<ShopifyProductShippingSnapshot> GetProductShippingSnapshotAsync(long productId);
        Task<bool> CollectionContainsProductAsync(string collectionGid, string productGid);
        Task<ShopifyProductMediaSyncResult> SyncProductImagesAsync(Part part, IReadOnlyCollection<PartImage> images);
        Task<ShopifyCollectionCreateResult> CreateAutomatedCollectionForDiscountAsync(AdminDiscountCode discount);
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