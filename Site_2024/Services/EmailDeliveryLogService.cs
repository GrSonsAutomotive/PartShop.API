using Site_2024.Web.Api.Interfaces;
using System;
using System.Data;
using System.Data.SqlClient;

namespace Site_2024.Web.Api.Services
{
    public class EmailDeliveryLogService : IEmailDeliveryLogService
    {
        private readonly IDataProvider _data;

        public EmailDeliveryLogService(IDataProvider data)
        {
            _data = data;
        }

        public bool TryBegin(
            string messageKey,
            string messageType,
            string entityType,
            int? entityId,
            string recipient)
        {
            if (string.IsNullOrWhiteSpace(messageKey))
            {
                throw new ArgumentException(
                    "An email message key is required.",
                    nameof(messageKey));
            }

            bool shouldSend = false;

            _data.ExecuteCmd(
                "[dbo].[EmailDeliveryLog_TryBegin]",
                inputParamMapper: delegate (SqlParameterCollection col)
                {
                    col.AddWithValue("@MessageKey", messageKey.Trim());
                    col.AddWithValue("@MessageType", messageType.Trim());
                    col.AddWithValue("@EntityType", entityType.Trim());
                    col.AddWithValue(
                        "@EntityId",
                        entityId.HasValue ? entityId.Value : DBNull.Value);
                    col.AddWithValue("@Recipient", recipient.Trim());
                },
                singleRecordMapper: delegate (IDataReader reader, short set)
                {
                    if (set == 0)
                    {
                        shouldSend = Convert.ToBoolean(reader["ShouldSend"]);
                    }
                });

            return shouldSend;
        }

        public void MarkSent(string messageKey)
        {
            _data.ExecuteNonQuery(
                "[dbo].[EmailDeliveryLog_MarkSent]",
                inputParamMapper: delegate (SqlParameterCollection col)
                {
                    col.AddWithValue("@MessageKey", messageKey.Trim());
                });
        }

        public void MarkFailed(string messageKey, string errorMessage)
        {
            _data.ExecuteNonQuery(
                "[dbo].[EmailDeliveryLog_MarkFailed]",
                inputParamMapper: delegate (SqlParameterCollection col)
                {
                    col.AddWithValue("@MessageKey", messageKey.Trim());
                    col.AddWithValue(
                        "@ErrorMessage",
                        string.IsNullOrWhiteSpace(errorMessage)
                            ? "Unknown email delivery error."
                            : errorMessage.Trim());
                });
        }
    }
}
