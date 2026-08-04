using Site_2024.Models.Domain.RefundRequests;
using Site_2024.Web.Api.Models;
using Site_2024.Web.Api.Requests;

namespace Site_2024.Web.Api.Services
{
    public interface ISmtpEmailService
    {
        string GetContactRecipientEmail(string inquiryType);
        void SendContactEmail(ContactEmailRequest model, Part? part, string requestOrigin);
        void SendContactConfirmationEmail(ContactEmailRequest model, Part? part, string requestOrigin);
        void SendReturnSubmissionBusinessEmail(RefundRequest model);
        void SendReturnSubmissionCustomerEmail(RefundRequest model);
        void SendReturnStatusEmail(RefundRequest model);
        void SendReturnDecisionEmail(RefundRequest model);
        void SendReturnLabelEmail(RefundRequest model);
        void SendReturnCompletionEmail(RefundRequest model, RefundFinalization finalization);
    }
}
