using Site_2024.Web.Api.Models;

namespace Site_2024.Web.Api.Services
{
    public class ShopifyPartSyncService : IShopifyPartSyncService
    {
        private readonly IPartService _partService;
        private readonly IShippingPoliciesService _shippingPoliciesService;
        private readonly IShopifyAdminService _shopifyAdminService;

        public ShopifyPartSyncService(
            IPartService partService,
            IShippingPoliciesService shippingPoliciesService,
            IShopifyAdminService shopifyAdminService)
        {
            _partService = partService;
            _shippingPoliciesService = shippingPoliciesService;
            _shopifyAdminService = shopifyAdminService;
        }

        public async Task<ShopifyPartSyncResult> CreateAndSyncProductForPartAsync(int partId)
        {
            Part part = _partService.GetPartById(partId);

            if (part == null)
                throw new ApplicationException($"Part {partId} was not found.");

            if (part.ShopifyProductId.HasValue || part.ShopifyVariantId.HasValue)
                throw new ApplicationException($"Part {partId} already has Shopify IDs.");

            ShopifyCreateProductResult createResult =
                await _shopifyAdminService.CreateProductForPartAsync(part);

            _partService.UpdateShopifyIds(
                partId,
                createResult.ProductId,
                createResult.VariantId,
                createResult.InventoryItemId);

            Part updatedPart = _partService.GetPartById(partId);

            if (updatedPart.ShippingPolicy?.AllowsOnlineCheckout == false)
            {
                // Contact-only items must never remain active in Shopify, even if
                // the store is configured to create new products as ACTIVE.
                await _shopifyAdminService.UnpublishProductForPartAsync(updatedPart);
            }
            else
            {
                await SyncShippingProfileForPartAsync(partId);
            }

            ShopifyProductInventorySyncResult syncResult =
                await _shopifyAdminService.SyncProductDetailsForPartAsync(updatedPart);

            return new ShopifyPartSyncResult
            {
                PartId = partId,
                CreateResult = createResult,
                SyncResult = syncResult
            };
        }

        public async Task SyncShippingProfileForPartAsync(int partId)
        {
            Part part = _partService.GetPartById(partId);

            if (part == null)
                throw new ApplicationException($"Part {partId} was not found.");

            if (!part.ShopifyVariantId.HasValue)
                throw new ApplicationException("Part is missing ShopifyVariantId.");

            ShippingPolicy? policy = _shippingPoliciesService
                .GetAll()
                .FirstOrDefault(item => item.Id == part.ShippingPolicy.Id);

            if (policy == null)
                throw new ApplicationException($"Shipping policy {part.ShippingPolicy.Id} was not found or is inactive.");

            if (!policy.AllowsOnlineCheckout)
                throw new ApplicationException($"Shipping policy '{policy.Name}' is contact-only and is not assigned to Shopify checkout.");

            if (!policy.ShopifyProfileId.HasValue || policy.ShopifyProfileId.Value <= 0)
                throw new ApplicationException($"Shipping policy '{policy.Name}' is not mapped to a Shopify delivery profile.");

            await _shopifyAdminService.AssignVariantToDeliveryProfileAsync(
                part.ShopifyVariantId.Value,
                policy.ShopifyProfileId.Value);
        }
    }
}
