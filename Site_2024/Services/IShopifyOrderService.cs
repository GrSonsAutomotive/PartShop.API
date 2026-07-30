using Site_2024.Models.Domain.RefundRequests;
using Site_2024.Web.Api.Models;

namespace Site_2024.Web.Api.Services
{
    public interface IShopifyOrderService
    {
        Task<List<ShopifyOrderSummary>> GetRecentOrdersAsync(
            int first,
            string? view);

        Task<ShopifyReturnOrderLookupResult?> GetOrderForReturnAsync(
            string orderNumber,
            string? expectedEmail);

        Task<ShopifyRefundPreviewResult> GetRefundPreviewAsync(
            RefundRequest refundRequest,
            ShopifyRefundPreviewOptions options);

        Task<ShopifyOrderSyncResult> SyncRecentPaidOrdersAsync(
            int first,
            int userId);
    }
}
