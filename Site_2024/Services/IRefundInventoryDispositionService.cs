using Site_2024.Models.Domain.RefundRequests;

namespace Site_2024.Web.Api.Services
{
    public interface IRefundInventoryDispositionService
    {
        int InitializeByRefundRequestId(int refundRequestId, int? userId);

        RefundInventoryDisposition? GetByRefundRequestId(int refundRequestId);

        RefundInventoryDispositionAction? GetActionById(long actionId);

        RefundInventoryDispositionAction PrepareAction(
            int dispositionItemId,
            string actionType,
            int quantity,
            string reason,
            string idempotencyKey,
            int userId);

        RefundInventoryDispositionAction ApplyLocal(
            long actionId,
            int userId);

        RefundInventoryDispositionAction MarkShopifyResult(
            long actionId,
            bool wasSuccessful,
            string? errorMessage,
            string? detailsJson,
            int? userId);
    }
}
