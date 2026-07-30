using Site_2024.Models.Domain.RefundRequests;
using Site_2024.Web.Api.Interfaces;
using Site_2024.Web.Api.Models;
using Site_2024.Web.Api.Models.Shopify;
using System.Data;
using System.Data.SqlClient;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Site_2024.Web.Api.Services
{
    public class RefundFinalizationService : IRefundFinalizationService
    {
        private static readonly JsonSerializerOptions JsonOptions =
            new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            };

        private readonly IDataProvider _data;

        public RefundFinalizationService(IDataProvider data)
        {
            _data = data;
        }

        public RefundFinalization? GetByRefundRequestId(
            int refundRequestId)
        {
            RefundFinalization? finalization = null;

            _data.ExecuteCmd(
                "[dbo].[RefundFinalizations_GetByRefundRequestId]",
                inputParamMapper: delegate (SqlParameterCollection col)
                {
                    col.AddWithValue(
                        "@RefundRequestId",
                        refundRequestId);
                },
                singleRecordMapper: delegate (
                    IDataReader reader,
                    short set)
                {
                    if (set == 0)
                    {
                        finalization = MapFinalization(reader);
                    }
                    else if (set == 1 && finalization != null)
                    {
                        finalization.Items.Add(MapItem(reader));
                    }
                    else if (set == 2 && finalization != null)
                    {
                        finalization.Events.Add(MapEvent(reader));
                    }
                });

            return finalization;
        }

        public RefundFinalization PrepareFromPreview(
            ShopifyRefundPreviewResult preview,
            int userId)
        {
            ValidatePreview(preview, userId);

            RefundFinalization? existing =
                GetByRefundRequestId(preview.RefundRequestId);

            string idempotencyKey =
                existing == null
                    ? CreateIdempotencyKey(preview.RefundRequestId)
                    : existing.IdempotencyKey;

            string preparedCalculationJson =
                BuildPreparedCalculationJson(preview);

            string preparedCalculationHash =
                CalculateSha256(preparedCalculationJson);

            string itemsJson = JsonSerializer.Serialize(
                preview.Items.Select(item => new
                {
                    refundRequestItemId =
                        item.RefundRequestItemId,
                    refundQuantity =
                        item.QuantityToRefund,
                    shopifyRefundableQuantity =
                        item.ShopifyRefundableQuantity,
                    merchandiseRefundAmount =
                        item.ShopifySubtotalAmount,
                    taxRefundAmount =
                        item.ShopifyTaxAmount
                }),
                JsonOptions);

            int preparedId = 0;

            _data.ExecuteCmd(
                "[dbo].[RefundFinalizations_Prepare]",
                inputParamMapper: delegate (
                    SqlParameterCollection col)
                {
                    col.AddWithValue(
                        "@RefundRequestId",
                        preview.RefundRequestId);

                    col.Add(
                        new SqlParameter(
                            "@IdempotencyKey",
                            SqlDbType.NVarChar,
                            100)
                        {
                            Value = idempotencyKey
                        });

                    col.Add(
                        new SqlParameter(
                            "@ShopifyOrderGid",
                            SqlDbType.NVarChar,
                            200)
                        {
                            Value = DbValue(
                                preview.ShopifyOrderGid)
                        });

                    col.Add(
                        new SqlParameter(
                            "@CurrencyCode",
                            SqlDbType.NVarChar,
                            10)
                        {
                            Value = preview.CurrencyCode
                        });

                    col.AddWithValue(
                        "@ShopifyShippingRefundableAmount",
                        preview.ShopifyShippingRefundableAmount);

                    col.AddWithValue(
                        "@OriginalShippingRefundAmount",
                        preview.OriginalShippingRefundAmount);

                    col.AddWithValue(
                        "@AdditionalDeductionAmount",
                        preview.AdditionalDeductionAmount);

                    col.Add(
                        new SqlParameter(
                            "@AdditionalDeductionReason",
                            SqlDbType.NVarChar,
                            2000)
                        {
                            Value = DbValue(
                                preview.AdditionalDeductionReason)
                        });

                    col.AddWithValue(
                        "@ShopifyMaximumRefundableAmount",
                        preview.ShopifyMaximumRefundableAmount);

                    col.Add(
                        new SqlParameter(
                            "@PreparedCalculationHash",
                            SqlDbType.NVarChar,
                            128)
                        {
                            Value = preparedCalculationHash
                        });

                    col.Add(
                        new SqlParameter(
                            "@PreparedCalculationJson",
                            SqlDbType.NVarChar,
                            -1)
                        {
                            Value = preparedCalculationJson
                        });

                    col.Add(
                        new SqlParameter(
                            "@ShopifyPreviewJson",
                            SqlDbType.NVarChar,
                            -1)
                        {
                            Value = DbValue(
                                preview.ShopifyPreviewJson)
                        });

                    col.Add(
                        new SqlParameter(
                            "@ItemsJson",
                            SqlDbType.NVarChar,
                            -1)
                        {
                            Value = itemsJson
                        });

                    col.AddWithValue("@UserId", userId);

                    col.Add(
                        new SqlParameter(
                            "@Id",
                            SqlDbType.Int)
                        {
                            Direction =
                                ParameterDirection.Output
                        });
                },
                singleRecordMapper: delegate (
                    IDataReader reader,
                    short set)
                {
                    if (set == 0)
                    {
                        preparedId = Convert.ToInt32(
                            reader["Id"]);
                    }
                });

            if (preparedId <= 0)
            {
                throw new InvalidOperationException(
                    "The prepared refund was not saved.");
            }

            RefundFinalization? result =
                GetByRefundRequestId(preview.RefundRequestId);

            if (result == null)
            {
                throw new InvalidOperationException(
                    "The prepared refund was saved but could not be reloaded.");
            }

            return result;
        }

        public RefundFinalization BeginProcessing(
            int refundRequestId,
            int userId)
        {
            _data.ExecuteNonQuery(
                "[dbo].[RefundFinalizations_MarkProcessing]",
                inputParamMapper: delegate (SqlParameterCollection col)
                {
                    col.AddWithValue("@RefundRequestId", refundRequestId);
                    col.AddWithValue("@UserId", userId);
                },
                returnParameters: null);

            return GetByRefundRequestId(refundRequestId)
                ?? throw new InvalidOperationException(
                    "The prepared refund could not be reloaded after entering Processing.");
        }

        public void MarkShopifyRequestStarted(
            int refundRequestId,
            int userId)
        {
            _data.ExecuteNonQuery(
                "[dbo].[RefundFinalizations_MarkShopifyRequestStarted]",
                inputParamMapper: delegate (SqlParameterCollection col)
                {
                    col.AddWithValue("@RefundRequestId", refundRequestId);
                    col.AddWithValue("@UserId", userId);
                },
                returnParameters: null);
        }

        public void MarkShopifyResult(
            int refundRequestId,
            ShopifyRefundExecutionResult result,
            bool financiallySuccessful,
            string? errorMessage,
            int userId)
        {
            if (result == null)
            {
                throw new ArgumentNullException(nameof(result));
            }

            ShopifyRefundTransactionResult? primary =
                result.PrimaryTransaction;

            if (financiallySuccessful)
            {
                _data.ExecuteNonQuery(
                    "[dbo].[RefundFinalizations_ShopifyResult_Update]",
                    inputParamMapper: delegate (SqlParameterCollection col)
                    {
                        col.AddWithValue("@RefundRequestId", refundRequestId);
                        col.AddWithValue("@Succeeded", true);
                        col.AddWithValue(
                            "@ShopifyRefundId",
                            result.ShopifyRefundId);
                        col.Add(
                            new SqlParameter(
                                "@ShopifyRefundGid",
                                SqlDbType.NVarChar,
                                200)
                            {
                                Value = DbValue(result.ShopifyRefundGid)
                            });
                        col.AddWithValue(
                            "@ShopifyTransactionId",
                            primary?.ShopifyTransactionId.HasValue == true
                                ? (object)primary.ShopifyTransactionId.Value
                                : DBNull.Value);
                        col.Add(
                            new SqlParameter(
                                "@ShopifyTransactionGid",
                                SqlDbType.NVarChar,
                                200)
                            {
                                Value = DbValue(primary?.ShopifyTransactionGid)
                            });
                        col.Add(
                            new SqlParameter(
                                "@ShopifyTransactionStatus",
                                SqlDbType.NVarChar,
                                50)
                            {
                                Value = DbValue(primary?.Status)
                            });
                        col.AddWithValue(
                            "@ActualRefundedAmount",
                            result.ActualRefundedAmount);
                        col.Add(
                            new SqlParameter(
                                "@ShopifyResponseJson",
                                SqlDbType.NVarChar,
                                -1)
                            {
                                Value = DbValue(result.RawResponseJson)
                            });
                        col.AddWithValue("@ErrorMessage", DBNull.Value);
                        col.AddWithValue("@UserId", userId);
                    },
                    returnParameters: null);

                return;
            }

            _data.ExecuteNonQuery(
                "[dbo].[RefundFinalizations_ShopifyPending_Update]",
                inputParamMapper: delegate (SqlParameterCollection col)
                {
                    col.AddWithValue("@RefundRequestId", refundRequestId);
                    col.AddWithValue("@ShopifyRefundId", result.ShopifyRefundId);
                    col.Add(
                        new SqlParameter(
                            "@ShopifyRefundGid",
                            SqlDbType.NVarChar,
                            200)
                        {
                            Value = DbValue(result.ShopifyRefundGid)
                        });
                    col.AddWithValue(
                        "@ShopifyTransactionId",
                        primary?.ShopifyTransactionId.HasValue == true
                            ? (object)primary.ShopifyTransactionId.Value
                            : DBNull.Value);
                    col.Add(
                        new SqlParameter(
                            "@ShopifyTransactionGid",
                            SqlDbType.NVarChar,
                            200)
                        {
                            Value = DbValue(primary?.ShopifyTransactionGid)
                        });
                    col.Add(
                        new SqlParameter(
                            "@ShopifyTransactionStatus",
                            SqlDbType.NVarChar,
                            50)
                        {
                            Value = DbValue(primary?.Status)
                        });
                    col.AddWithValue(
                        "@ActualRefundedAmount",
                        result.ActualRefundedAmount);
                    col.Add(
                        new SqlParameter(
                            "@ShopifyResponseJson",
                            SqlDbType.NVarChar,
                            -1)
                        {
                            Value = DbValue(result.RawResponseJson)
                        });
                    col.Add(
                        new SqlParameter(
                            "@ErrorMessage",
                            SqlDbType.NVarChar,
                            4000)
                        {
                            Value = DbValue(errorMessage)
                        });
                    col.AddWithValue("@UserId", userId);
                },
                returnParameters: null);
        }

        public void MarkFailed(
            int refundRequestId,
            string errorMessage,
            int? userId)
        {
            _data.ExecuteNonQuery(
                "[dbo].[RefundFinalizations_ShopifyResult_Update]",
                inputParamMapper: delegate (SqlParameterCollection col)
                {
                    col.AddWithValue("@RefundRequestId", refundRequestId);
                    col.AddWithValue("@Succeeded", false);
                    col.AddWithValue("@ShopifyRefundId", DBNull.Value);
                    col.AddWithValue("@ShopifyRefundGid", DBNull.Value);
                    col.AddWithValue("@ShopifyTransactionId", DBNull.Value);
                    col.AddWithValue("@ShopifyTransactionGid", DBNull.Value);
                    col.AddWithValue("@ShopifyTransactionStatus", DBNull.Value);
                    col.AddWithValue("@ActualRefundedAmount", DBNull.Value);
                    col.AddWithValue("@ShopifyResponseJson", DBNull.Value);
                    col.Add(
                        new SqlParameter(
                            "@ErrorMessage",
                            SqlDbType.NVarChar,
                            4000)
                        {
                            Value = DbValue(errorMessage)
                        });
                    col.AddWithValue(
                        "@UserId",
                        userId.HasValue
                            ? (object)userId.Value
                            : DBNull.Value);
                },
                returnParameters: null);
        }

        public void MarkReconciliationRequired(
            int refundRequestId,
            string errorMessage,
            int? userId)
        {
            _data.ExecuteNonQuery(
                "[dbo].[RefundFinalizations_MarkReconciliationRequired]",
                inputParamMapper: delegate (SqlParameterCollection col)
                {
                    col.AddWithValue("@RefundRequestId", refundRequestId);
                    col.Add(
                        new SqlParameter(
                            "@ErrorMessage",
                            SqlDbType.NVarChar,
                            4000)
                        {
                            Value = DbValue(errorMessage)
                        });
                    col.AddWithValue(
                        "@UserId",
                        userId.HasValue
                            ? (object)userId.Value
                            : DBNull.Value);
                },
                returnParameters: null);
        }

        public RefundFinalization ApplyLocalInventory(
            int refundRequestId,
            int userId)
        {
            _data.ExecuteNonQuery(
                "[dbo].[RefundFinalizations_ApplyLocalInventory]",
                inputParamMapper: delegate (SqlParameterCollection col)
                {
                    col.AddWithValue("@RefundRequestId", refundRequestId);
                    col.AddWithValue("@UserId", userId);
                },
                returnParameters: null);

            return GetByRefundRequestId(refundRequestId)
                ?? throw new InvalidOperationException(
                    "The local refund inventory state could not be reloaded.");
        }

        public void MarkInventoryItemResult(
            int refundRequestId,
            int refundFinalizationItemId,
            bool wasSuccessful,
            string? errorMessage,
            string? detailsJson,
            int? userId)
        {
            _data.ExecuteNonQuery(
                "[dbo].[RefundFinalizations_InventoryItemResult_Update]",
                inputParamMapper: delegate (SqlParameterCollection col)
                {
                    col.AddWithValue("@RefundRequestId", refundRequestId);
                    col.AddWithValue(
                        "@RefundFinalizationItemId",
                        refundFinalizationItemId);
                    col.AddWithValue("@Succeeded", wasSuccessful);
                    col.Add(
                        new SqlParameter(
                            "@ErrorMessage",
                            SqlDbType.NVarChar,
                            4000)
                        {
                            Value = DbValue(errorMessage)
                        });
                    col.Add(
                        new SqlParameter(
                            "@DetailsJson",
                            SqlDbType.NVarChar,
                            -1)
                        {
                            Value = DbValue(detailsJson)
                        });
                    col.AddWithValue(
                        "@UserId",
                        userId.HasValue
                            ? (object)userId.Value
                            : DBNull.Value);
                },
                returnParameters: null);
        }

        public void CompleteInventory(
            int refundRequestId,
            int userId)
        {
            _data.ExecuteNonQuery(
                "[dbo].[RefundFinalizations_InventoryComplete]",
                inputParamMapper: delegate (SqlParameterCollection col)
                {
                    col.AddWithValue("@RefundRequestId", refundRequestId);
                    col.AddWithValue("@UserId", userId);
                },
                returnParameters: null);
        }

        public void MarkEmailResult(
            int refundRequestId,
            bool wasSent,
            string? errorMessage,
            int? userId)
        {
            _data.ExecuteNonQuery(
                "[dbo].[RefundFinalizations_EmailResult_Update]",
                inputParamMapper: delegate (SqlParameterCollection col)
                {
                    col.AddWithValue("@RefundRequestId", refundRequestId);
                    col.AddWithValue("@WasSent", wasSent);
                    col.Add(
                        new SqlParameter(
                            "@ErrorMessage",
                            SqlDbType.NVarChar,
                            2000)
                        {
                            Value = DbValue(errorMessage)
                        });
                    col.AddWithValue(
                        "@UserId",
                        userId.HasValue
                            ? (object)userId.Value
                            : DBNull.Value);
                },
                returnParameters: null);
        }

        private static void ValidatePreview(
            ShopifyRefundPreviewResult preview,
            int userId)
        {
            if (preview == null)
            {
                throw new ArgumentNullException(nameof(preview));
            }

            if (preview.RefundRequestId <= 0)
            {
                throw new InvalidOperationException(
                    "A valid refund request is required.");
            }

            if (userId <= 0)
            {
                throw new InvalidOperationException(
                    "A valid preparing administrator is required.");
            }

            if (preview.ShopifyOrderId <= 0
                || string.IsNullOrWhiteSpace(
                    preview.ShopifyOrderGid))
            {
                throw new InvalidOperationException(
                    "The Shopify order is missing from the refund preview.");
            }

            if (!preview.OrderIsRefundable)
            {
                throw new InvalidOperationException(
                    "Shopify no longer considers this order refundable.");
            }

            if (string.IsNullOrWhiteSpace(
                    preview.CurrencyCode))
            {
                throw new InvalidOperationException(
                    "The Shopify refund currency is required.");
            }

            if (preview.Items == null
                || preview.Items.Count == 0)
            {
                throw new InvalidOperationException(
                    "The refund preview does not contain any inspected items.");
            }

            if (preview.Items.Any(item =>
                    item.RefundRequestItemId <= 0
                    || item.ShopifyLineItemId <= 0
                    || item.QuantityToRefund <= 0
                    || item.QuantityToRefund
                        > item.QuantityReceived
                    || item.QuantityToRefund
                        > item.ShopifyRefundableQuantity))
            {
                throw new InvalidOperationException(
                    "The refund preview contains an invalid or no-longer-refundable item quantity.");
            }

            decimal merchandise = MoneyRound(
                preview.Items.Sum(
                    item => item.ShopifySubtotalAmount));

            decimal tax = MoneyRound(
                preview.Items.Sum(
                    item => item.ShopifyTaxAmount));

            decimal finalAmount = MoneyRound(
                merchandise
                + tax
                + preview.OriginalShippingRefundAmount
                - preview.BuyerPaidLabelDeductionAmount
                - preview.AdditionalDeductionAmount);

            if (Math.Abs(
                    merchandise
                    - preview.MerchandiseRefundAmount)
                > 0.01m
                || Math.Abs(
                    tax
                    - preview.TaxRefundAmount)
                > 0.01m
                || Math.Abs(
                    finalAmount
                    - preview.FinalRefundAmount)
                > 0.01m)
            {
                throw new InvalidOperationException(
                    "The Shopify preview line-item totals no longer match its refund calculation. Reload the preview before preparing it.");
            }

            if (preview.FinalRefundAmount < 0m
                || preview.FinalRefundAmount
                    > preview.ShopifyMaximumRefundableAmount)
            {
                throw new InvalidOperationException(
                    "The final amount is outside Shopify's refundable limit.");
            }
        }

        private static string BuildPreparedCalculationJson(
            ShopifyRefundPreviewResult preview)
        {
            return JsonSerializer.Serialize(
                new
                {
                    preview.RefundRequestId,
                    preview.ShopifyOrderId,
                    preview.ShopifyOrderGid,
                    preview.OrderName,
                    preview.CurrencyCode,
                    preview.SellerError,
                    preview.IsInternational,
                    preview.ReturnShippingPayer,
                    preview.OriginalShippingRequested,
                    preview.OriginalShippingAllowed,
                    preview.MerchandiseRefundAmount,
                    preview.TaxRefundAmount,
                    preview.ShopifyShippingRefundableAmount,
                    preview.OriginalShippingRefundAmount,
                    preview.BuyerPaidLabelDeductionAmount,
                    preview.AdditionalDeductionAmount,
                    preview.AdditionalDeductionReason,
                    preview.ShopifyMaximumRefundableAmount,
                    preview.FinalRefundAmount,
                    preview.PreviewedAtUtc,
                    items = preview.Items.Select(item => new
                    {
                        item.RefundRequestItemId,
                        item.PartId,
                        item.PartName,
                        item.PartNumber,
                        item.ShopifyLineItemId,
                        item.ShopifyLineItemGid,
                        item.ShopifyTitle,
                        item.ShopifySku,
                        item.ShopifyRefundableQuantity,
                        item.QuantityReceived,
                        item.QuantityToRefund,
                        item.RestockQuantity,
                        item.HoldQuantity,
                        item.DamagedQuantity,
                        item.ShopifySubtotalAmount,
                        item.ShopifyTaxAmount,
                        item.ShopifyTotalAmount
                    }),
                    suggestedTransactions =
                        preview.SuggestedTransactions
                },
                JsonOptions);
        }

        private static string CreateIdempotencyKey(
            int refundRequestId)
        {
            return string.Format(
                "site-2024-refund-{0}-{1:N}",
                refundRequestId,
                Guid.NewGuid());
        }

        private static string CalculateSha256(string value)
        {
            byte[] bytes = Encoding.UTF8.GetBytes(value);
            byte[] hash = SHA256.HashData(bytes);
            return Convert.ToHexString(hash);
        }

        private static decimal MoneyRound(decimal value)
        {
            return Math.Round(
                value,
                2,
                MidpointRounding.AwayFromZero);
        }

        private static object DbValue(string? value)
        {
            return string.IsNullOrWhiteSpace(value)
                ? DBNull.Value
                : value.Trim();
        }

        private static RefundFinalization MapFinalization(
            IDataReader reader)
        {
            return new RefundFinalization
            {
                Id = ReadInt32(reader, "Id"),
                RefundRequestId =
                    ReadInt32(reader, "RefundRequestId"),
                Status =
                    ReadString(reader, "Status")
                    ?? string.Empty,
                IdempotencyKey =
                    ReadString(reader, "IdempotencyKey")
                    ?? string.Empty,
                PreparedRevision =
                    ReadInt32(reader, "PreparedRevision"),
                PreparedByUserId =
                    ReadInt32(reader, "PreparedByUserId"),
                PreparedByName =
                    ReadString(reader, "PreparedByName"),
                PreparedAt =
                    ReadDateTime(reader, "PreparedAt"),
                ProcessingStartedAt =
                    ReadNullableDateTime(
                        reader,
                        "ProcessingStartedAt"),
                ShopifyRequestStartedAt =
                    ReadNullableDateTime(
                        reader,
                        "ShopifyRequestStartedAt"),
                IssuedByUserId =
                    ReadNullableInt32(
                        reader,
                        "IssuedByUserId"),
                IssuedByName =
                    ReadString(reader, "IssuedByName"),
                AttemptCount =
                    ReadInt32(reader, "AttemptCount"),
                LastAttemptAt =
                    ReadNullableDateTime(
                        reader,
                        "LastAttemptAt"),
                LastError =
                    ReadString(reader, "LastError"),
                ShopifyOrderId =
                    ReadInt64(reader, "ShopifyOrderId"),
                ShopifyOrderGid =
                    ReadString(reader, "ShopifyOrderGid"),
                CurrencyCode =
                    ReadString(reader, "CurrencyCode")
                    ?? string.Empty,
                SellerErrorSnapshot =
                    ReadNullableBool(
                        reader,
                        "SellerErrorSnapshot"),
                IsInternationalSnapshot =
                    ReadNullableBool(
                        reader,
                        "IsInternationalSnapshot"),
                ReturnShippingPayerSnapshot =
                    ReadString(
                        reader,
                        "ReturnShippingPayerSnapshot"),
                MerchandiseRefundAmount =
                    ReadDecimal(
                        reader,
                        "MerchandiseRefundAmount"),
                TaxRefundAmount =
                    ReadDecimal(reader, "TaxRefundAmount"),
                MerchandiseTaxRefundAmount =
                    ReadDecimal(
                        reader,
                        "MerchandiseTaxRefundAmount"),
                ShopifyShippingRefundableAmount =
                    ReadDecimal(
                        reader,
                        "ShopifyShippingRefundableAmount"),
                OriginalShippingRefundAmount =
                    ReadDecimal(
                        reader,
                        "OriginalShippingRefundAmount"),
                BuyerPaidLabelDeductionAmount =
                    ReadDecimal(
                        reader,
                        "BuyerPaidLabelDeductionAmount"),
                AdditionalDeductionAmount =
                    ReadDecimal(
                        reader,
                        "AdditionalDeductionAmount"),
                AdditionalDeductionReason =
                    ReadString(
                        reader,
                        "AdditionalDeductionReason"),
                ShopifyMaximumRefundableAmount =
                    ReadDecimal(
                        reader,
                        "ShopifyMaximumRefundableAmount"),
                FinalRefundAmount =
                    ReadDecimal(reader, "FinalRefundAmount"),
                PreparedCalculationHash =
                    ReadString(
                        reader,
                        "PreparedCalculationHash"),
                PreparedCalculationJson =
                    ReadString(
                        reader,
                        "PreparedCalculationJson",
                        trim: false),
                ShopifyPreviewJson =
                    ReadString(
                        reader,
                        "ShopifyPreviewJson",
                        trim: false),
                ShopifyPreviewedAt =
                    ReadNullableDateTime(
                        reader,
                        "ShopifyPreviewedAt"),
                ShopifyRefundId =
                    ReadNullableInt64(
                        reader,
                        "ShopifyRefundId"),
                ShopifyRefundGid =
                    ReadString(reader, "ShopifyRefundGid"),
                ShopifyTransactionId =
                    ReadNullableInt64(
                        reader,
                        "ShopifyTransactionId"),
                ShopifyTransactionGid =
                    ReadString(
                        reader,
                        "ShopifyTransactionGid"),
                ShopifyTransactionStatus =
                    ReadString(
                        reader,
                        "ShopifyTransactionStatus"),
                ActualRefundedAmount =
                    ReadNullableDecimal(
                        reader,
                        "ActualRefundedAmount"),
                ShopifyResponseJson =
                    ReadString(
                        reader,
                        "ShopifyResponseJson",
                        trim: false),
                ShopifySucceededAt =
                    ReadNullableDateTime(
                        reader,
                        "ShopifySucceededAt"),
                InventoryStatus =
                    ReadString(reader, "InventoryStatus")
                    ?? string.Empty,
                InventoryAttemptCount =
                    ReadInt32(
                        reader,
                        "InventoryAttemptCount"),
                InventoryLastAttemptAt =
                    ReadNullableDateTime(
                        reader,
                        "InventoryLastAttemptAt"),
                InventoryLastError =
                    ReadString(
                        reader,
                        "InventoryLastError"),
                InventoryCompletedAt =
                    ReadNullableDateTime(
                        reader,
                        "InventoryCompletedAt"),
                CompletionEmailStatus =
                    ReadString(
                        reader,
                        "CompletionEmailStatus")
                    ?? string.Empty,
                CompletionEmailAttempts =
                    ReadInt32(
                        reader,
                        "CompletionEmailAttempts"),
                CompletionEmailSentAt =
                    ReadNullableDateTime(
                        reader,
                        "CompletionEmailSentAt"),
                CompletionEmailLastAttemptAt =
                    ReadNullableDateTime(
                        reader,
                        "CompletionEmailLastAttemptAt"),
                CompletionEmailLastError =
                    ReadString(
                        reader,
                        "CompletionEmailLastError"),
                CompletedAt =
                    ReadNullableDateTime(
                        reader,
                        "CompletedAt"),
                DateCreated =
                    ReadDateTime(reader, "DateCreated"),
                DateModified =
                    ReadDateTime(reader, "DateModified"),
                RowVersion =
                    ReadBytes(reader, "RowVersion")
            };
        }

        private static RefundFinalizationItem MapItem(
            IDataReader reader)
        {
            return new RefundFinalizationItem
            {
                Id = ReadInt32(reader, "Id"),
                RefundFinalizationId =
                    ReadInt32(
                        reader,
                        "RefundFinalizationId"),
                RefundRequestItemId =
                    ReadInt32(
                        reader,
                        "RefundRequestItemId"),
                PartId =
                    ReadNullableInt32(reader, "PartId"),
                PartName =
                    ReadString(reader, "PartName"),
                PartNumber =
                    ReadString(reader, "PartNumber"),
                ShopifyLineItemId =
                    ReadInt64(
                        reader,
                        "ShopifyLineItemId"),
                ShopifyVariantId =
                    ReadNullableInt64(
                        reader,
                        "ShopifyVariantId"),
                RefundQuantity =
                    ReadInt32(reader, "RefundQuantity"),
                QuantityReceivedSnapshot =
                    ReadInt32(
                        reader,
                        "QuantityReceivedSnapshot"),
                ShopifyRefundableQuantitySnapshot =
                    ReadInt32(
                        reader,
                        "ShopifyRefundableQuantitySnapshot"),
                MerchandiseRefundAmount =
                    ReadDecimal(
                        reader,
                        "MerchandiseRefundAmount"),
                TaxRefundAmount =
                    ReadDecimal(reader, "TaxRefundAmount"),
                ItemRefundAmount =
                    ReadDecimal(reader, "ItemRefundAmount"),
                CurrencyCode =
                    ReadString(reader, "CurrencyCode")
                    ?? string.Empty,
                RestockQuantitySnapshot =
                    ReadInt32(
                        reader,
                        "RestockQuantitySnapshot"),
                HoldQuantitySnapshot =
                    ReadInt32(
                        reader,
                        "HoldQuantitySnapshot"),
                DamagedQuantitySnapshot =
                    ReadInt32(
                        reader,
                        "DamagedQuantitySnapshot"),
                LocalQuantityAtPrepare =
                    ReadNullableInt32(
                        reader,
                        "LocalQuantityAtPrepare"),
                LocalInventoryStatus =
                    ReadString(
                        reader,
                        "LocalInventoryStatus")
                    ?? string.Empty,
                LocalQuantityBefore =
                    ReadNullableInt32(
                        reader,
                        "LocalQuantityBefore"),
                LocalQuantityAfter =
                    ReadNullableInt32(
                        reader,
                        "LocalQuantityAfter"),
                LocalInventoryCommittedAt =
                    ReadNullableDateTime(
                        reader,
                        "LocalInventoryCommittedAt"),
                LocalInventoryCommittedByUserId =
                    ReadNullableInt32(
                        reader,
                        "LocalInventoryCommittedByUserId"),
                LocalInventoryCommittedByName =
                    ReadString(
                        reader,
                        "LocalInventoryCommittedByName"),
                LocalInventoryLastError =
                    ReadString(
                        reader,
                        "LocalInventoryLastError"),
                ShopifyInventoryStatus =
                    ReadString(
                        reader,
                        "ShopifyInventoryStatus")
                    ?? string.Empty,
                ShopifyInventoryIdempotencyKey =
                    ReadString(
                        reader,
                        "ShopifyInventoryIdempotencyKey"),
                ShopifyInventoryAttemptCount =
                    ReadInt32(
                        reader,
                        "ShopifyInventoryAttemptCount"),
                ShopifyInventoryLastAttemptAt =
                    ReadNullableDateTime(
                        reader,
                        "ShopifyInventoryLastAttemptAt"),
                ShopifyInventoryLastError =
                    ReadString(
                        reader,
                        "ShopifyInventoryLastError"),
                ShopifyInventorySyncedAt =
                    ReadNullableDateTime(
                        reader,
                        "ShopifyInventorySyncedAt"),
                DateCreated =
                    ReadDateTime(reader, "DateCreated"),
                DateModified =
                    ReadDateTime(reader, "DateModified"),
                RowVersion =
                    ReadBytes(reader, "RowVersion")
            };
        }

        private static RefundFinalizationEvent MapEvent(
            IDataReader reader)
        {
            return new RefundFinalizationEvent
            {
                Id = ReadInt64(reader, "Id"),
                RefundFinalizationId =
                    ReadInt32(
                        reader,
                        "RefundFinalizationId"),
                RefundRequestId =
                    ReadInt32(reader, "RefundRequestId"),
                RefundFinalizationItemId =
                    ReadNullableInt32(
                        reader,
                        "RefundFinalizationItemId"),
                EventType =
                    ReadString(reader, "EventType")
                    ?? string.Empty,
                Status =
                    ReadString(reader, "Status"),
                Message =
                    ReadString(reader, "Message"),
                DetailsJson =
                    ReadString(
                        reader,
                        "DetailsJson",
                        trim: false),
                CreatedByUserId =
                    ReadNullableInt32(
                        reader,
                        "CreatedByUserId"),
                CreatedByName =
                    ReadString(reader, "CreatedByName"),
                DateCreated =
                    ReadDateTime(reader, "DateCreated")
            };
        }

        private static int GetOrdinal(
            IDataReader reader,
            string name)
        {
            return reader.GetOrdinal(name);
        }

        private static string? ReadString(
            IDataReader reader,
            string name,
            bool trim = true)
        {
            int ordinal = GetOrdinal(reader, name);

            if (reader.IsDBNull(ordinal))
            {
                return null;
            }

            string value =
                Convert.ToString(reader.GetValue(ordinal))
                ?? string.Empty;

            return trim ? value.Trim() : value;
        }

        private static int ReadInt32(
            IDataReader reader,
            string name)
        {
            return Convert.ToInt32(
                reader[GetOrdinal(reader, name)]);
        }

        private static int? ReadNullableInt32(
            IDataReader reader,
            string name)
        {
            int ordinal = GetOrdinal(reader, name);

            return reader.IsDBNull(ordinal)
                ? null
                : Convert.ToInt32(
                    reader.GetValue(ordinal));
        }

        private static long ReadInt64(
            IDataReader reader,
            string name)
        {
            return Convert.ToInt64(
                reader[GetOrdinal(reader, name)]);
        }

        private static long? ReadNullableInt64(
            IDataReader reader,
            string name)
        {
            int ordinal = GetOrdinal(reader, name);

            return reader.IsDBNull(ordinal)
                ? null
                : Convert.ToInt64(
                    reader.GetValue(ordinal));
        }

        private static decimal ReadDecimal(
            IDataReader reader,
            string name)
        {
            return Convert.ToDecimal(
                reader[GetOrdinal(reader, name)]);
        }

        private static decimal? ReadNullableDecimal(
            IDataReader reader,
            string name)
        {
            int ordinal = GetOrdinal(reader, name);

            return reader.IsDBNull(ordinal)
                ? null
                : Convert.ToDecimal(
                    reader.GetValue(ordinal));
        }

        private static bool? ReadNullableBool(
            IDataReader reader,
            string name)
        {
            int ordinal = GetOrdinal(reader, name);

            return reader.IsDBNull(ordinal)
                ? null
                : Convert.ToBoolean(
                    reader.GetValue(ordinal));
        }

        private static DateTime ReadDateTime(
            IDataReader reader,
            string name)
        {
            DateTime value = Convert.ToDateTime(
                reader[GetOrdinal(reader, name)]);

            return DateTime.SpecifyKind(
                value,
                DateTimeKind.Utc);
        }

        private static DateTime? ReadNullableDateTime(
            IDataReader reader,
            string name)
        {
            int ordinal = GetOrdinal(reader, name);

            if (reader.IsDBNull(ordinal))
            {
                return null;
            }

            DateTime value = Convert.ToDateTime(
                reader.GetValue(ordinal));

            return DateTime.SpecifyKind(
                value,
                DateTimeKind.Utc);
        }

        private static byte[]? ReadBytes(
            IDataReader reader,
            string name)
        {
            int ordinal = GetOrdinal(reader, name);

            return reader.IsDBNull(ordinal)
                ? null
                : (byte[])reader.GetValue(ordinal);
        }
    }
}
