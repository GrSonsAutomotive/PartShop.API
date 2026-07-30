using System.Collections.Generic;
using Site_2024.Models.Domain.RefundRequests;
using Site_2024.Models.Requests.RefundRequests;
using Site_2024.Web.Api.Constructors;
using Site_2024.Web.Api.Models;

namespace Site_2024.Web.Api.Services
{
    public interface IRefundRequestService
    {
        int Add(RefundRequestAddRequest model, int? userId);
        RefundRequest? GetById(int id);
        Paged<RefundRequest>? GetPaginated(int pageIndex, int pageSize, RefundRequestSearchRequest model);
        List<ReturnReason> GetReasons();
        List<ReturnStatus> GetStatuses();
        int AddItem(int refundRequestId, RefundRequestItemAddRequest model);
        int AddPhoto(
            int refundRequestId,
            RefundRequestPhotoAddRequest model);

        void ReplaceMatchedShopifyItems(
            int refundRequestId,
            ShopifyOrderSummary order,
            List<RefundRequestShopifyItemSelectionRequest> selections);

        List<RefundRequestDuplicateConflict> GetDuplicateConflicts(
            int refundRequestId);

        void ApplyDecision(
            int id,
            RefundRequestDecisionRequest model,
            ReturnEligibilityEvaluation eligibility,
            int userId);

        void MarkDecisionEmailResult(
            int id,
            bool wasSent,
            string? errorMessage);

        void SaveReturnLabel(
            int id,
            string storedFilePath,
            string originalFileName,
            string contentType,
            RefundRequestReturnLabelRequest model,
            int userId);

        void MarkReturnLabelEmailResult(
            int id,
            bool wasSent,
            string? errorMessage);

        void UpdateReturnTracking(
            int id,
            RefundRequestReturnTrackingRequest model,
            int userId);

        void MarkReturnDelivered(
            int id,
            RefundRequestReturnDeliveredRequest model,
            int userId);

        void MarkItemReceived(
            int id,
            RefundRequestMarkReceivedRequest model,
            int userId);

        void CompleteInspection(
            int id,
            RefundRequestCompleteInspectionRequest model,
            int userId);

        void UpdateStatus(
            int id,
            RefundRequestUpdateStatusRequest model,
            int userId);
    }
}
