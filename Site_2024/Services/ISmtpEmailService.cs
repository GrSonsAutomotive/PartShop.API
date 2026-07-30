using Site_2024.Models.Domain.RefundRequests;
using Site_2024.Web.Api.Models;
using Site_2024.Web.Api.Requests;

namespace Site_2024.Web.Api.Services
{
    public interface ISmtpEmailService
    {
        void SendContactEmail(ContactEmailRequest model, Part part, string requestOrigin);
        void SendReturnDecisionEmail(RefundRequest model);
        void SendReturnLabelEmail(RefundRequest model);
        void SendReturnCompletionEmail(RefundRequest model, RefundFinalization finalization);
    }
}
