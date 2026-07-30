using Microsoft.Extensions.Options;
using Site_2024.Models.Domain.RefundRequests;
using Site_2024.Web.Api.Models;
using Site_2024.Web.Api.Requests;
using System.Globalization;
using System.Net;
using System.Net.Mail;
using System.Text;

namespace Site_2024.Web.Api.Services
{
    public class SmtpEmailService : ISmtpEmailService
    {
        private readonly ContactEmailSettings _settings;
        private readonly IWebHostEnvironment _webHostEnvironment;

        public SmtpEmailService(
            IOptions<ContactEmailSettings> options,
            IWebHostEnvironment webHostEnvironment)
        {
            _settings = options.Value;
            _webHostEnvironment = webHostEnvironment;
        }

        public void SendContactEmail(ContactEmailRequest model, Part part, string requestOrigin)
        {
            string recipientEmail = GetRecipientEmail(model.InquiryType);
            string siteBaseUrl = ResolveSiteBaseUrl(requestOrigin);

            using MailMessage mail = new MailMessage();

            mail.From = new MailAddress(
                _settings.FromEmail,
                string.IsNullOrWhiteSpace(_settings.FromDisplayName)
                    ? "Site Contact Form"
                    : _settings.FromDisplayName
            );

            mail.To.Add(recipientEmail);
            mail.ReplyToList.Add(new MailAddress(model.Email, model.Name));

            mail.Subject = BuildSubject(model);
            mail.Body = BuildBody(model, part, siteBaseUrl);
            mail.IsBodyHtml = false;

            using SmtpClient client = new SmtpClient(_settings.SmtpHost, _settings.SmtpPort);

            client.EnableSsl = _settings.EnableSsl;
            client.Credentials = new NetworkCredential(
                _settings.SmtpUsername,
                _settings.SmtpPassword
            );

            client.Send(mail);
        }

        public void SendReturnDecisionEmail(RefundRequest model)
        {
            if (model == null)
            {
                throw new ArgumentNullException(nameof(model));
            }

            string status =
                (model.Status ?? model.StatusName ?? string.Empty).Trim();

            bool isApproved = string.Equals(
                status,
                "Approved",
                StringComparison.OrdinalIgnoreCase);

            bool isDenied = string.Equals(
                status,
                "Denied",
                StringComparison.OrdinalIgnoreCase);

            if (!isApproved && !isDenied)
            {
                throw new InvalidOperationException(
                    "Decision emails can only be sent for Approved or Denied requests.");
            }

            if (string.IsNullOrWhiteSpace(model.CustomerEmail))
            {
                throw new InvalidOperationException(
                    "The return request does not contain a customer email address.");
            }

            using MailMessage mail = new MailMessage();

            mail.From = new MailAddress(
                _settings.FromEmail,
                string.IsNullOrWhiteSpace(_settings.FromDisplayName)
                    ? "GR&Sons Returns"
                    : _settings.FromDisplayName);

            mail.To.Add(new MailAddress(model.CustomerEmail.Trim()));

            if (!string.IsNullOrWhiteSpace(_settings.ReturnsEmail))
            {
                mail.ReplyToList.Add(
                    new MailAddress(
                        _settings.ReturnsEmail.Trim(),
                        "GR&Sons Returns"));
            }

            mail.Subject = BuildReturnDecisionSubject(model, isApproved);
            mail.Body = BuildReturnDecisionBody(model, isApproved);
            mail.IsBodyHtml = false;

            using SmtpClient client =
                new SmtpClient(_settings.SmtpHost, _settings.SmtpPort);

            client.EnableSsl = _settings.EnableSsl;
            client.Credentials = new NetworkCredential(
                _settings.SmtpUsername,
                _settings.SmtpPassword);

            client.Send(mail);
        }

        public void SendReturnLabelEmail(RefundRequest model)
        {
            if (model == null)
            {
                throw new ArgumentNullException(nameof(model));
            }

            string status =
                (model.Status ?? model.StatusName ?? string.Empty).Trim();

            if (!string.Equals(
                status,
                "Approved",
                StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    "Return labels can only be emailed for Approved requests.");
            }

            bool sellerPaid =
                string.Equals(
                    model.ReturnShippingPayer,
                    "Seller",
                    StringComparison.OrdinalIgnoreCase);

            bool buyerPaid =
                string.Equals(
                    model.ReturnShippingPayer,
                    "Buyer",
                    StringComparison.OrdinalIgnoreCase);

            if (!sellerPaid && !buyerPaid)
            {
                throw new InvalidOperationException(
                    "A Pirate Ship PDF label can only be sent for a buyer-paid or seller-paid return.");
            }

            if (buyerPaid
                && (!model.ReturnLabelCost.HasValue
                    || model.ReturnLabelCost.Value <= 0))
            {
                throw new InvalidOperationException(
                    "A buyer-paid PDF label requires a positive documented label cost.");
            }

            if (string.IsNullOrWhiteSpace(model.CustomerEmail))
            {
                throw new InvalidOperationException(
                    "The return request does not contain a customer email address.");
            }

            string labelPath = ResolveReturnLabelPath(model);

            if (!File.Exists(labelPath))
            {
                throw new InvalidOperationException(
                    "The saved Pirate Ship PDF label could not be found.");
            }

            using MailMessage mail = new MailMessage();

            mail.From = new MailAddress(
                _settings.FromEmail,
                string.IsNullOrWhiteSpace(_settings.FromDisplayName)
                    ? "GR&Sons Returns"
                    : _settings.FromDisplayName);

            mail.To.Add(new MailAddress(model.CustomerEmail.Trim()));

            if (!string.IsNullOrWhiteSpace(_settings.ReturnsEmail))
            {
                mail.ReplyToList.Add(
                    new MailAddress(
                        _settings.ReturnsEmail.Trim(),
                        "GR&Sons Returns"));
            }

            string orderReference = string.IsNullOrWhiteSpace(model.OrderNumber)
                ? $"Request #{model.Id}"
                : $"Order {model.OrderNumber}";

            mail.Subject = $"Your Prepaid Return Label - {orderReference}";
            mail.Body = BuildReturnLabelBody(model);
            mail.IsBodyHtml = false;

            string attachmentName =
                string.IsNullOrWhiteSpace(
                    model.ReturnLabelOriginalFileName)
                    ? $"return-label-{model.Id}.pdf"
                    : model.ReturnLabelOriginalFileName.Trim();

            Attachment labelAttachment =
                new Attachment(labelPath, "application/pdf");

            labelAttachment.Name = attachmentName;
            mail.Attachments.Add(labelAttachment);

            using SmtpClient client =
                new SmtpClient(_settings.SmtpHost, _settings.SmtpPort);

            client.EnableSsl = _settings.EnableSsl;
            client.Credentials = new NetworkCredential(
                _settings.SmtpUsername,
                _settings.SmtpPassword);

            client.Send(mail);
        }

        public void SendReturnCompletionEmail(
            RefundRequest model,
            RefundFinalization finalization)
        {
            if (model == null)
            {
                throw new ArgumentNullException(nameof(model));
            }

            if (finalization == null)
            {
                throw new ArgumentNullException(nameof(finalization));
            }

            if (finalization.ShopifySucceededAt == null
                || finalization.ActualRefundedAmount == null)
            {
                throw new InvalidOperationException(
                    "The Shopify refund must be confirmed before the completion email is sent.");
            }

            if (!string.Equals(
                    finalization.InventoryStatus,
                    "Completed",
                    StringComparison.OrdinalIgnoreCase)
                && !string.Equals(
                    finalization.InventoryStatus,
                    "NotRequired",
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    "Inventory must be completed before the refund completion email is sent.");
            }

            if (string.IsNullOrWhiteSpace(model.CustomerEmail))
            {
                throw new InvalidOperationException(
                    "The return request does not contain a customer email address.");
            }

            using MailMessage mail = new MailMessage();

            mail.From = new MailAddress(
                _settings.FromEmail,
                string.IsNullOrWhiteSpace(_settings.FromDisplayName)
                    ? "GR&Sons Returns"
                    : _settings.FromDisplayName);

            mail.To.Add(new MailAddress(model.CustomerEmail.Trim()));

            if (!string.IsNullOrWhiteSpace(_settings.ReturnsEmail))
            {
                mail.ReplyToList.Add(
                    new MailAddress(
                        _settings.ReturnsEmail.Trim(),
                        "GR&Sons Returns"));
            }

            string orderReference = string.IsNullOrWhiteSpace(model.OrderNumber)
                ? $"Return Request #{model.Id}"
                : $"Order {model.OrderNumber}";

            mail.Subject = $"Your Refund Is Complete - {orderReference}";
            mail.Body = BuildReturnCompletionBody(model, finalization);
            mail.IsBodyHtml = false;

            using SmtpClient client =
                new SmtpClient(_settings.SmtpHost, _settings.SmtpPort);

            client.EnableSsl = _settings.EnableSsl;
            client.Credentials = new NetworkCredential(
                _settings.SmtpUsername,
                _settings.SmtpPassword);

            client.Send(mail);
        }

        private static string BuildReturnCompletionBody(
            RefundRequest model,
            RefundFinalization finalization)
        {
            CultureInfo culture = CultureInfo.GetCultureInfo("en-US");
            string currency = string.IsNullOrWhiteSpace(finalization.CurrencyCode)
                ? "USD"
                : finalization.CurrencyCode.Trim().ToUpperInvariant();

            string Money(decimal amount)
            {
                return string.Equals(currency, "USD", StringComparison.OrdinalIgnoreCase)
                    ? amount.ToString("C2", culture)
                    : $"{amount.ToString("N2", culture)} {currency}";
            }

            StringBuilder sb = new StringBuilder();

            sb.AppendLine("Your return refund has been completed.");
            sb.AppendLine();
            sb.AppendLine($"Return Request: #{model.Id}");
            sb.AppendLine($"Order Number: {DisplayValue(model.OrderNumber)}");
            sb.AppendLine();

            sb.AppendLine("Refunded Item(s)");
            sb.AppendLine("----------------------------------------");

            if (finalization.Items == null || finalization.Items.Count == 0)
            {
                sb.AppendLine("No item details were available.");
            }
            else
            {
                foreach (RefundFinalizationItem item in finalization.Items)
                {
                    string title = item.PartName ?? "Returned item";
                    string partNumber = string.IsNullOrWhiteSpace(item.PartNumber)
                        ? string.Empty
                        : $" | SKU/Part #: {item.PartNumber}";

                    sb.AppendLine(
                        $"- {title} | Quantity refunded: {item.RefundQuantity}{partNumber}");
                }
            }

            sb.AppendLine();
            sb.AppendLine("Refund Breakdown");
            sb.AppendLine("----------------------------------------");
            sb.AppendLine(
                $"Merchandise and tax: {Money(finalization.MerchandiseTaxRefundAmount)}");

            if (finalization.OriginalShippingRefundAmount > 0m)
            {
                sb.AppendLine(
                    $"Original outbound shipping refund: {Money(finalization.OriginalShippingRefundAmount)}");
            }

            if (finalization.BuyerPaidLabelDeductionAmount > 0m)
            {
                sb.AppendLine(
                    $"Buyer-paid return label deduction: -{Money(finalization.BuyerPaidLabelDeductionAmount)}");
            }

            if (finalization.AdditionalDeductionAmount > 0m)
            {
                sb.AppendLine(
                    $"Other documented deduction: -{Money(finalization.AdditionalDeductionAmount)}");

                if (!string.IsNullOrWhiteSpace(finalization.AdditionalDeductionReason))
                {
                    sb.AppendLine(
                        $"Deduction reason: {finalization.AdditionalDeductionReason.Trim()}");
                }
            }

            sb.AppendLine(
                $"Final amount refunded: {Money(finalization.ActualRefundedAmount ?? finalization.FinalRefundAmount)}");
            sb.AppendLine();
            sb.AppendLine(
                "The refund was sent through Shopify to the original payment method. Your bank or payment provider controls when the credit appears.");
            sb.AppendLine(
                "Exchanges are not offered. A replacement item may be purchased in a new order.");
            sb.AppendLine();
            sb.AppendLine(
                "Please reply to this email if you have questions about the completed refund.");

            return sb.ToString();
        }

        private string ResolveReturnLabelPath(
            RefundRequest model)
        {
            if (string.IsNullOrWhiteSpace(
                model.ReturnLabelFilePath))
            {
                throw new InvalidOperationException(
                    "A saved Pirate Ship PDF label is required.");
            }

            string labelRoot =
                Path.GetFullPath(
                    GetReturnLabelStorageRoot());

            string storedPath =
                model.ReturnLabelFilePath
                    .Replace('/', Path.DirectorySeparatorChar)
                    .Replace('\\', Path.DirectorySeparatorChar);

            string fullPath =
                Path.GetFullPath(
                    Path.Combine(
                        labelRoot,
                        storedPath));

            string allowedPrefix =
                labelRoot.TrimEnd(
                    Path.DirectorySeparatorChar)
                + Path.DirectorySeparatorChar;

            if (!fullPath.StartsWith(
                    allowedPrefix,
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    "The saved return-label path is invalid.");
            }

            return fullPath;
        }

        private string GetReturnLabelStorageRoot()
        {
            string? home =
                Environment.GetEnvironmentVariable(
                    "HOME");

            if (!string.IsNullOrWhiteSpace(home))
            {
                return Path.Combine(
                    home,
                    "data",
                    "Site_2024",
                    "ReturnLabels");
            }

            return Path.Combine(
                _webHostEnvironment.ContentRootPath,
                "App_Data",
                "ReturnLabels");
        }

        private static string BuildReturnLabelBody(
            RefundRequest model)
        {
            StringBuilder sb = new StringBuilder();

            bool buyerPaid =
                string.Equals(
                    model.ReturnShippingPayer,
                    "Buyer",
                    StringComparison.OrdinalIgnoreCase);

            sb.AppendLine(
                buyerPaid
                    ? "Your buyer-paid return label is attached as a PDF."
                    : "Your prepaid return label is attached as a PDF.");

            if (buyerPaid && model.ReturnLabelCost.HasValue)
            {
                string labelCost =
                    model.ReturnLabelCost.Value.ToString(
                        "C",
                        CultureInfo.GetCultureInfo("en-US"));

                sb.AppendLine(
                    $"The documented label cost of {labelCost} will be deducted from the final refund after the returned item is received and inspected.");
            }

            sb.AppendLine();
            sb.AppendLine($"Return Request: #{model.Id}");
            sb.AppendLine($"Order Number: {DisplayValue(model.OrderNumber)}");
            sb.AppendLine();

            AppendReturnItems(sb, model.Items);

            sb.AppendLine("Pirate Ship Return Label");
            sb.AppendLine("----------------------------------------");
            sb.AppendLine(
                $"Attached file: {DisplayValue(model.ReturnLabelOriginalFileName)}");
            sb.AppendLine($"Carrier: {DisplayValue(model.ReturnCarrier)}");
            sb.AppendLine($"Tracking Number: {DisplayValue(model.ReturnTrackingNumber)}");

            DateTime shipByDeadline =
                model.ApprovalExpiresAt
                ?? DateTime.UtcNow.AddDays(7);

            sb.AppendLine(
                $"Ship-by deadline: {FormatCustomerDate(shipByDeadline)}");

            sb.AppendLine();
            sb.AppendLine(
                "Open the attached PDF and print the label promptly. Attach it securely over any old shipping labels and give the package to the carrier before the ship-by deadline.");
            sb.AppendLine();
            sb.AppendLine("Return Address");
            sb.AppendLine("----------------------------------------");
            sb.AppendLine("GR&Sons (dporschepartsman)");
            sb.AppendLine("30025 Alicia Pkwy #563");
            sb.AppendLine("Laguna Niguel, CA 92677");
            sb.AppendLine();
            sb.AppendLine(
                "No refund is issued when the label is sent. The returned item must be received and inspected before the final refund is calculated.");
            sb.AppendLine();
            sb.AppendLine(
                "Please reply to this email if the PDF attachment does not open or if you have questions about the return.");

            return sb.ToString();
        }

        private static string BuildReturnDecisionSubject(
            RefundRequest model,
            bool isApproved)
        {
            string decision = isApproved ? "Approved" : "Denied";
            string orderReference = string.IsNullOrWhiteSpace(model.OrderNumber)
                ? $"Request #{model.Id}"
                : $"Order {model.OrderNumber}";

            return $"Return {decision} - {orderReference}";
        }

        private static string BuildReturnDecisionBody(
            RefundRequest model,
            bool isApproved)
        {
            StringBuilder sb = new StringBuilder();

            sb.AppendLine(isApproved
                ? "Your return request has been approved."
                : "Your return request has been denied.");
            sb.AppendLine();
            sb.AppendLine($"Return Request: #{model.Id}");
            sb.AppendLine($"Order Number: {DisplayValue(model.OrderNumber)}");
            sb.AppendLine();

            AppendReturnItems(sb, model.Items);

            if (isApproved)
            {
                sb.AppendLine("Return Instructions");
                sb.AppendLine("----------------------------------------");
                sb.AppendLine(DisplayValue(model.CustomerInstructions));
                sb.AppendLine();

                sb.AppendLine($"Return shipping: {BuildShippingPayerText(model)}");

                bool sellerPaidLabel = string.Equals(
                    model.ReturnShippingPayer,
                    "Seller",
                    StringComparison.OrdinalIgnoreCase);

                if (model.ApprovalExpiresAt.HasValue && !sellerPaidLabel)
                {
                    sb.AppendLine(
                        $"Ship-by deadline: {FormatCustomerDate(model.ApprovalExpiresAt.Value)}");
                }

                sb.AppendLine();
                sb.AppendLine("Return Address");
                sb.AppendLine("----------------------------------------");
                sb.AppendLine("GR&Sons (dporschepartsman)");
                sb.AppendLine("30025 Alicia Pkwy #563");
                sb.AppendLine("Laguna Niguel, CA 92677");
                sb.AppendLine();

                if (string.Equals(
                    model.ReturnShippingPayer,
                    "Seller",
                    StringComparison.OrdinalIgnoreCase))
                {
                    sb.AppendLine(
                        "A prepaid Pirate Ship return label will be sent separately. Do not ship the item until the label or additional instructions are received. Your 7-day shipping deadline begins when the label is sent.");
                    sb.AppendLine();
                }
                else if (string.Equals(
                    model.ReturnShippingPayer,
                    "Buyer",
                    StringComparison.OrdinalIgnoreCase))
                {
                    sb.AppendLine(
                        "Return shipping is buyer-paid. You may be instructed to purchase your own postage, or GR&Sons may send a Pirate Ship PDF label and deduct its documented cost from the final refund. If a PDF label is sent, the ship-by deadline will be reset to 7 days from that email.");
                    sb.AppendLine();

                    if (model.IsInternational == true)
                    {
                        sb.AppendLine(
                            "International return postage is the buyer's responsibility and is never reimbursed.");
                        sb.AppendLine();
                    }
                }

                sb.AppendLine(
                    "No refund is issued at approval. The item must be received and inspected before the final refund is calculated.");
                sb.AppendLine(
                    "Exchanges are not offered. After the refund is completed, a replacement item may be purchased in a new order.");
            }
            else
            {
                sb.AppendLine("Decision Reason");
                sb.AppendLine("----------------------------------------");
                sb.AppendLine(DisplayValue(model.DenialReason));
            }

            sb.AppendLine();
            sb.AppendLine("Please reply to this email if you have questions about this decision.");

            return sb.ToString();
        }

        private static void AppendReturnItems(
            StringBuilder sb,
            List<RefundRequestItem> items)
        {
            sb.AppendLine("Reviewed Item(s)");
            sb.AppendLine("----------------------------------------");

            if (items == null || items.Count == 0)
            {
                sb.AppendLine("No item details were available.");
                sb.AppendLine();
                return;
            }

            foreach (RefundRequestItem item in items)
            {
                string title =
                    item.PartName
                    ?? item.ProductTitle
                    ?? "Returned item";

                string sku = item.PartNumber ?? item.Sku ?? string.Empty;
                string skuText = string.IsNullOrWhiteSpace(sku)
                    ? string.Empty
                    : $" | SKU/Part #: {sku}";

                sb.AppendLine(
                    $"- {title} | Quantity: {Math.Max(1, item.Quantity)}{skuText}");
            }

            sb.AppendLine();
        }

        private static string BuildShippingPayerText(RefundRequest model)
        {
            if (string.Equals(
                model.ReturnShippingPayer,
                "Seller",
                StringComparison.OrdinalIgnoreCase))
            {
                return "Seller paid - prepaid Pirate Ship label will follow";
            }

            if (string.Equals(
                model.ReturnShippingPayer,
                "Buyer",
                StringComparison.OrdinalIgnoreCase))
            {
                return "Buyer paid - customer postage or documented label cost deducted from the refund";
            }

            if (string.Equals(
                model.ReturnShippingPayer,
                "NoLabel",
                StringComparison.OrdinalIgnoreCase))
            {
                return "No prepaid label issued - follow the instructions above";
            }

            return "See the instructions above";
        }

        private static string FormatCustomerDate(DateTime value)
        {
            return value.ToString(
                "MMMM d, yyyy",
                CultureInfo.GetCultureInfo("en-US"));
        }

        private static string DisplayValue(string value)
        {
            return string.IsNullOrWhiteSpace(value)
                ? "Not provided"
                : value.Trim();
        }

        private string GetRecipientEmail(string inquiryType)
        {
            string normalized = inquiryType?.Trim().ToLower();

            return normalized switch
            {
                "parts" => _settings.PartsEmail,
                "orders" => _settings.OrdersEmail,
                "returns" => _settings.ReturnsEmail,
                "shipping" => _settings.ShippingEmail,
                "wholesale" => _settings.SalesEmail,
                "website" => _settings.SupportEmail,
                _ => _settings.GeneralEmail
            };
        }

        private static string BuildSubject(ContactEmailRequest model)
        {
            string inquiryLabel = model.InquiryType?.Trim();

            if (string.IsNullOrWhiteSpace(inquiryLabel))
            {
                inquiryLabel = "General";
            }

            return $"Contact Form - {inquiryLabel} - {model.Subject}";
        }

        private static string BuildBody(ContactEmailRequest model, Part part, string siteBaseUrl)
        {
            StringBuilder sb = new StringBuilder();

            sb.AppendLine("New Contact Form Submission");
            sb.AppendLine("----------------------------------------");
            sb.AppendLine($"Inquiry Type: {model.InquiryType}");
            sb.AppendLine($"Name: {model.Name}");
            sb.AppendLine($"Email: {model.Email}");
            sb.AppendLine($"Phone: {(string.IsNullOrWhiteSpace(model.Phone) ? "Not provided" : model.Phone)}");
            sb.AppendLine($"Subject: {model.Subject}");
            sb.AppendLine();
            sb.AppendLine("Customer Message:");
            sb.AppendLine(model.Message);

            if (part != null)
            {
                string customerPath = $"/browse/part/{part.Id}";
                string adminPath = $"/admin/part/{part.Id}";
                string customerUrl = BuildSiteUrl(siteBaseUrl, customerPath);
                string adminUrl = BuildSiteUrl(siteBaseUrl, adminPath);

                sb.AppendLine();
                sb.AppendLine("----------------------------------------");
                sb.AppendLine("Part Reference (added automatically by Site_2024)");
                sb.AppendLine($"Part Name: {part.Name}");
                sb.AppendLine($"Part ID: {part.Id}");
                sb.AppendLine($"Customer Page: {customerUrl}");
                sb.AppendLine($"Admin Page: {adminUrl}");
            }

            return sb.ToString();
        }

        private string ResolveSiteBaseUrl(string requestOrigin)
        {
            string validOrigin = NormalizeHttpOrigin(requestOrigin);

            if (!string.IsNullOrWhiteSpace(validOrigin))
            {
                return validOrigin;
            }

            return NormalizeHttpOrigin(_settings.SiteBaseUrl);
        }

        private static string NormalizeHttpOrigin(string value)
        {
            if (string.IsNullOrWhiteSpace(value) ||
                !Uri.TryCreate(value, UriKind.Absolute, out Uri uri) ||
                (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
            {
                return string.Empty;
            }

            return uri.GetLeftPart(UriPartial.Authority).TrimEnd('/');
        }

        private static string BuildSiteUrl(string siteBaseUrl, string path)
        {
            return string.IsNullOrWhiteSpace(siteBaseUrl)
                ? path
                : $"{siteBaseUrl}{path}";
        }
    }
}
