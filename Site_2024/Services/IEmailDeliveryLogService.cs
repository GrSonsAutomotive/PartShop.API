namespace Site_2024.Web.Api.Services
{
    public interface IEmailDeliveryLogService
    {
        bool TryBegin(
            string messageKey,
            string messageType,
            string entityType,
            int? entityId,
            string recipient);

        void MarkSent(string messageKey);
        void MarkFailed(string messageKey, string errorMessage);
    }
}
