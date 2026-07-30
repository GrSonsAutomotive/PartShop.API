using Site_2024.Models.Domain.RefundRequests;
using Site_2024.Web.Api.Models.Shopify;

namespace Site_2024.Web.Api.Services
{
    public interface IShopifyRefundService
    {
        Task<ShopifyRefundExecutionResult> CreateRefundAsync(
            RefundFinalization finalization,
            Action markDispatchStarted);

        Task<ShopifyRefundExecutionResult> GetRefundStatusAsync(
            string shopifyRefundGid);
    }
}
