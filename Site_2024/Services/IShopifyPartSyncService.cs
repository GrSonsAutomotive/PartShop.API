using Site_2024.Web.Api.Models;

namespace Site_2024.Web.Api.Services
{
    public interface IShopifyPartSyncService
    {
        Task<ShopifyPartSyncResult> CreateAndSyncProductForPartAsync(int partId);
        Task SyncShippingProfileForPartAsync(int partId);
    }
}