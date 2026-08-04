using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Site_2024.Web.Api.Models;
using Site_2024.Web.Api.Requests;
using Site_2024.Web.Api.Responses;
using Site_2024.Web.Api.Services;

namespace Site_2024.Web.Api.Controllers
{
    [Route("api/contact")]
    [ApiController]
    public class ContactApiController : BaseApiController
    {
        private readonly ISmtpEmailService _emailService;
        private readonly IEmailDeliveryLogService _emailDeliveryLogService;
        private readonly IPartService _partService;

        public ContactApiController(
            ISmtpEmailService emailService,
            IEmailDeliveryLogService emailDeliveryLogService,
            IPartService partService,
            ILogger<ContactApiController> logger) : base(logger)
        {
            _emailService = emailService;
            _emailDeliveryLogService = emailDeliveryLogService;
            _partService = partService;
        }

        [HttpPost]
        [AllowAnonymous]
        public ActionResult<SuccessResponse> Send(ContactEmailRequest model)
        {
            int code = 200;
            BaseResponse response = null;

            try
            {
                if (model?.ClientSubmissionId == null
                    || model.ClientSubmissionId == Guid.Empty)
                {
                    return BadRequest(
                        new ErrorResponse(
                            "A valid contact submission id is required."));
                }

                Part? part = null;

                if (model.PartId.HasValue)
                {
                    part = _partService.GetPartByIdCustomer(
                        model.PartId.Value);

                    if (part == null)
                    {
                        return NotFound(
                            new ErrorResponse(
                                "The referenced part could not be found."));
                    }
                }

                string requestOrigin =
                    Request.Headers.Origin.FirstOrDefault();
                string submissionId =
                    model.ClientSubmissionId.Value.ToString("N");
                string businessRecipient =
                    _emailService.GetContactRecipientEmail(
                        model.InquiryType);

                SendLoggedEmail(
                    $"contact-business:{submissionId}",
                    "ContactBusinessNotification",
                    "ContactSubmission",
                    null,
                    businessRecipient,
                    () => _emailService.SendContactEmail(
                        model,
                        part,
                        requestOrigin));

                try
                {
                    SendLoggedEmail(
                        $"contact-customer:{submissionId}",
                        "ContactCustomerConfirmation",
                        "ContactSubmission",
                        null,
                        model.Email,
                        () => _emailService.SendContactConfirmationEmail(
                            model,
                            part,
                            requestOrigin));
                }
                catch (Exception confirmationException)
                {
                    Logger.LogError(
                        confirmationException,
                        "Contact message was delivered to the business, but the customer confirmation failed for submission {SubmissionId}.",
                        submissionId);
                }

                response = new SuccessResponse();
            }
            catch (Exception ex)
            {
                code = 500;
                response = new ErrorResponse(
                    "Unable to send contact message at this time.");
                Logger.LogError(ex, "Contact form email failed.");
            }

            return StatusCode(code, response);
        }

        private void SendLoggedEmail(
            string messageKey,
            string messageType,
            string entityType,
            int? entityId,
            string recipient,
            Action sendAction)
        {
            bool shouldSend = _emailDeliveryLogService.TryBegin(
                messageKey,
                messageType,
                entityType,
                entityId,
                recipient);

            if (!shouldSend)
            {
                return;
            }

            try
            {
                sendAction();
                _emailDeliveryLogService.MarkSent(messageKey);
            }
            catch (Exception ex)
            {
                _emailDeliveryLogService.MarkFailed(
                    messageKey,
                    ex.Message);
                throw;
            }
        }
    }
}
