using Site_2024.Models.Domain.RefundRequests;
using Site_2024.Web.Api.Models;
using Site_2024.Web.Api.Models.Shopify;

namespace Site_2024.Web.Api.Services
{
    public interface IRefundFinalizationService
    {
        RefundFinalization? GetByRefundRequestId(int refundRequestId);

        RefundFinalization PrepareFromPreview(
            ShopifyRefundPreviewResult preview,
            int userId);

        RefundFinalization BeginProcessing(
            int refundRequestId,
            int userId);

        void MarkShopifyRequestStarted(
            int refundRequestId,
            int userId);

        void MarkShopifyResult(
            int refundRequestId,
            ShopifyRefundExecutionResult result,
            bool financiallySuccessful,
            string? errorMessage,
            int userId);

        void MarkFailed(
            int refundRequestId,
            string errorMessage,
            int? userId);

        void MarkReconciliationRequired(
            int refundRequestId,
            string errorMessage,
            int? userId);

        RefundFinalization ApplyLocalInventory(
            int refundRequestId,
            int userId);

        void MarkInventoryItemResult(
            int refundRequestId,
            int refundFinalizationItemId,
            bool wasSuccessful,
            string? errorMessage,
            string? detailsJson,
            int? userId);

        void CompleteInventory(
            int refundRequestId,
            int userId);

        void MarkEmailResult(
            int refundRequestId,
            bool wasSent,
            string? errorMessage,
            int? userId);
    }
}
