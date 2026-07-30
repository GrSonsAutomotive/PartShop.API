using Site_2024.Models.Domain.RefundRequests;
using Site_2024.Web.Api.Interfaces;
using System;
using System.Data;
using System.Data.SqlClient;

namespace Site_2024.Web.Api.Services
{
    public class RefundInventoryDispositionService :
        IRefundInventoryDispositionService
    {
        private readonly IDataProvider _data;

        public RefundInventoryDispositionService(IDataProvider data)
        {
            _data = data;
        }

        public int InitializeByRefundRequestId(
            int refundRequestId,
            int? userId)
        {
            int initializedItemCount = 0;

            _data.ExecuteCmd(
                "[dbo].[RefundInventoryDispositions_InitializeByRefundRequestId]",
                inputParamMapper: delegate (SqlParameterCollection col)
                {
                    col.AddWithValue("@RefundRequestId", refundRequestId);
                    col.AddWithValue(
                        "@UserId",
                        userId.HasValue ? (object)userId.Value : DBNull.Value);
                },
                singleRecordMapper: delegate (IDataReader reader, short set)
                {
                    if (set == 0)
                    {
                        initializedItemCount =
                            ReadInt32(reader, "InitializedItemCount");
                    }
                });

            return initializedItemCount;
        }

        public RefundInventoryDisposition? GetByRefundRequestId(
            int refundRequestId)
        {
            RefundInventoryDisposition? disposition = null;

            _data.ExecuteCmd(
                "[dbo].[RefundInventoryDispositions_GetByRefundRequestId]",
                inputParamMapper: delegate (SqlParameterCollection col)
                {
                    col.AddWithValue("@RefundRequestId", refundRequestId);
                },
                singleRecordMapper: delegate (IDataReader reader, short set)
                {
                    if (set == 0)
                    {
                        disposition = MapDisposition(reader);
                    }
                    else if (set == 1 && disposition != null)
                    {
                        disposition.Items.Add(MapItem(reader));
                    }
                    else if (set == 2 && disposition != null)
                    {
                        disposition.Actions.Add(MapAction(reader));
                    }
                    else if (set == 3 && disposition != null)
                    {
                        disposition.Events.Add(MapEvent(reader));
                    }
                });

            return disposition;
        }

        public RefundInventoryDispositionAction? GetActionById(long actionId)
        {
            RefundInventoryDispositionAction? action = null;

            _data.ExecuteCmd(
                "[dbo].[RefundInventoryDispositions_Action_GetById]",
                inputParamMapper: delegate (SqlParameterCollection col)
                {
                    col.AddWithValue("@ActionId", actionId);
                },
                singleRecordMapper: delegate (IDataReader reader, short set)
                {
                    if (set == 0)
                    {
                        action = MapAction(reader);
                    }
                });

            return action;
        }

        public RefundInventoryDispositionAction PrepareAction(
            int dispositionItemId,
            string actionType,
            int quantity,
            string reason,
            string idempotencyKey,
            int userId)
        {
            RefundInventoryDispositionAction? action = null;

            _data.ExecuteCmd(
                "[dbo].[RefundInventoryDispositions_Action_Prepare]",
                inputParamMapper: delegate (SqlParameterCollection col)
                {
                    col.AddWithValue("@DispositionItemId", dispositionItemId);
                    col.Add(
                        new SqlParameter("@ActionType", SqlDbType.NVarChar, 40)
                        {
                            Value = actionType.Trim()
                        });
                    col.AddWithValue("@Quantity", quantity);
                    col.Add(
                        new SqlParameter("@Reason", SqlDbType.NVarChar, 2000)
                        {
                            Value = reason.Trim()
                        });
                    col.Add(
                        new SqlParameter("@IdempotencyKey", SqlDbType.NVarChar, 100)
                        {
                            Value = idempotencyKey.Trim()
                        });
                    col.AddWithValue("@UserId", userId);
                },
                singleRecordMapper: delegate (IDataReader reader, short set)
                {
                    if (set == 0)
                    {
                        action = MapAction(reader);
                    }
                });

            return action
                ?? throw new InvalidOperationException(
                    "The held/damaged resolution action was not returned by the database.");
        }

        public RefundInventoryDispositionAction ApplyLocal(
            long actionId,
            int userId)
        {
            RefundInventoryDispositionAction? action = null;

            _data.ExecuteCmd(
                "[dbo].[RefundInventoryDispositions_Action_ApplyLocal]",
                inputParamMapper: delegate (SqlParameterCollection col)
                {
                    col.AddWithValue("@ActionId", actionId);
                    col.AddWithValue("@UserId", userId);
                },
                singleRecordMapper: delegate (IDataReader reader, short set)
                {
                    if (set == 0)
                    {
                        action = MapAction(reader);
                    }
                });

            return action
                ?? throw new InvalidOperationException(
                    "The held/damaged resolution action could not be reloaded after the local commit.");
        }

        public RefundInventoryDispositionAction MarkShopifyResult(
            long actionId,
            bool wasSuccessful,
            string? errorMessage,
            string? detailsJson,
            int? userId)
        {
            RefundInventoryDispositionAction? action = null;

            _data.ExecuteCmd(
                "[dbo].[RefundInventoryDispositions_Action_ShopifyResult_Update]",
                inputParamMapper: delegate (SqlParameterCollection col)
                {
                    col.AddWithValue("@ActionId", actionId);
                    col.AddWithValue("@WasSuccessful", wasSuccessful);
                    col.Add(
                        new SqlParameter("@ErrorMessage", SqlDbType.NVarChar, 2000)
                        {
                            Value = DbValue(errorMessage)
                        });
                    col.Add(
                        new SqlParameter("@DetailsJson", SqlDbType.NVarChar, -1)
                        {
                            Value = DbValue(detailsJson, trim: false)
                        });
                    col.AddWithValue(
                        "@UserId",
                        userId.HasValue ? (object)userId.Value : DBNull.Value);
                },
                singleRecordMapper: delegate (IDataReader reader, short set)
                {
                    if (set == 0)
                    {
                        action = MapAction(reader);
                    }
                });

            return action
                ?? throw new InvalidOperationException(
                    "The Shopify inventory result was saved but the action could not be reloaded.");
        }

        private static RefundInventoryDisposition MapDisposition(
            IDataReader reader)
        {
            return new RefundInventoryDisposition
            {
                RefundRequestId = ReadInt32(reader, "RefundRequestId"),
                RefundFinalizationId = ReadInt32(reader, "RefundFinalizationId"),
                RefundFinalizationStatus =
                    ReadString(reader, "RefundFinalizationStatus") ?? string.Empty,
                DispositionItemCount = ReadInt32(reader, "DispositionItemCount"),
                InitialHoldQuantity = ReadInt32(reader, "InitialHoldQuantity"),
                InitialDamagedQuantity = ReadInt32(reader, "InitialDamagedQuantity"),
                HoldRemainingQuantity = ReadInt32(reader, "HoldRemainingQuantity"),
                DamagedRemainingQuantity = ReadInt32(reader, "DamagedRemainingQuantity"),
                ReleasedToInventoryQuantity = ReadInt32(reader, "ReleasedToInventoryQuantity"),
                ConvertedHoldToDamagedQuantity = ReadInt32(reader, "ConvertedHoldToDamagedQuantity"),
                WrittenOffQuantity = ReadInt32(reader, "WrittenOffQuantity"),
                RetainedForPartsQuantity = ReadInt32(reader, "RetainedForPartsQuantity"),
                DisposedQuantity = ReadInt32(reader, "DisposedQuantity"),
                Status = ReadString(reader, "Status") ?? string.Empty
            };
        }

        private static RefundInventoryDispositionItem MapItem(
            IDataReader reader)
        {
            return new RefundInventoryDispositionItem
            {
                Id = ReadInt32(reader, "Id"),
                RefundFinalizationItemId = ReadInt32(reader, "RefundFinalizationItemId"),
                RefundFinalizationId = ReadInt32(reader, "RefundFinalizationId"),
                RefundRequestId = ReadInt32(reader, "RefundRequestId"),
                RefundRequestItemId = ReadInt32(reader, "RefundRequestItemId"),
                PartId = ReadNullableInt32(reader, "PartId"),
                PartName = ReadString(reader, "PartName"),
                PartNumber = ReadString(reader, "PartNumber"),
                InitialHoldQuantity = ReadInt32(reader, "InitialHoldQuantity"),
                InitialDamagedQuantity = ReadInt32(reader, "InitialDamagedQuantity"),
                HoldRemainingQuantity = ReadInt32(reader, "HoldRemainingQuantity"),
                DamagedRemainingQuantity = ReadInt32(reader, "DamagedRemainingQuantity"),
                ReleasedToInventoryQuantity = ReadInt32(reader, "ReleasedToInventoryQuantity"),
                ConvertedHoldToDamagedQuantity = ReadInt32(reader, "ConvertedHoldToDamagedQuantity"),
                WrittenOffQuantity = ReadInt32(reader, "WrittenOffQuantity"),
                RetainedForPartsQuantity = ReadInt32(reader, "RetainedForPartsQuantity"),
                DisposedQuantity = ReadInt32(reader, "DisposedQuantity"),
                Status = ReadString(reader, "Status") ?? string.Empty,
                DateCreated = ReadDateTime(reader, "DateCreated"),
                DateModified = ReadDateTime(reader, "DateModified"),
                RowVersion = ReadBytes(reader, "RowVersion")
            };
        }

        private static RefundInventoryDispositionAction MapAction(
            IDataReader reader)
        {
            return new RefundInventoryDispositionAction
            {
                Id = ReadInt64(reader, "Id"),
                DispositionItemId = ReadInt32(reader, "DispositionItemId"),
                RefundFinalizationItemId = ReadInt32(reader, "RefundFinalizationItemId"),
                RefundRequestId = ReadInt32(reader, "RefundRequestId"),
                PartId = ReadNullableInt32(reader, "PartId"),
                ActionType = ReadString(reader, "ActionType") ?? string.Empty,
                Quantity = ReadInt32(reader, "Quantity"),
                Reason = ReadString(reader, "Reason") ?? string.Empty,
                Status = ReadString(reader, "Status") ?? string.Empty,
                IdempotencyKey = ReadString(reader, "IdempotencyKey") ?? string.Empty,
                PreparedByUserId = ReadInt32(reader, "PreparedByUserId"),
                PreparedByName = ReadString(reader, "PreparedByName"),
                PreparedAt = ReadDateTime(reader, "PreparedAt"),
                ProcessingStartedAt = ReadNullableDateTime(reader, "ProcessingStartedAt"),
                CompletedAt = ReadNullableDateTime(reader, "CompletedAt"),
                LastError = ReadString(reader, "LastError", trim: false),
                LocalInventoryStatus = ReadString(reader, "LocalInventoryStatus") ?? string.Empty,
                LocalQuantityBefore = ReadNullableInt32(reader, "LocalQuantityBefore"),
                LocalQuantityAfter = ReadNullableInt32(reader, "LocalQuantityAfter"),
                LocalInventoryCommittedAt = ReadNullableDateTime(reader, "LocalInventoryCommittedAt"),
                LocalInventoryCommittedByUserId = ReadNullableInt32(reader, "LocalInventoryCommittedByUserId"),
                LocalInventoryCommittedByName = ReadString(reader, "LocalInventoryCommittedByName"),
                ShopifyInventoryStatus = ReadString(reader, "ShopifyInventoryStatus") ?? string.Empty,
                ShopifyInventoryIdempotencyKey = ReadString(reader, "ShopifyInventoryIdempotencyKey"),
                ShopifyInventoryAttemptCount = ReadInt32(reader, "ShopifyInventoryAttemptCount"),
                ShopifyInventoryLastAttemptAt = ReadNullableDateTime(reader, "ShopifyInventoryLastAttemptAt"),
                ShopifyInventoryLastError = ReadString(reader, "ShopifyInventoryLastError", trim: false),
                ShopifyInventorySyncedAt = ReadNullableDateTime(reader, "ShopifyInventorySyncedAt"),
                DateCreated = ReadDateTime(reader, "DateCreated"),
                DateModified = ReadDateTime(reader, "DateModified"),
                RowVersion = ReadBytes(reader, "RowVersion")
            };
        }

        private static RefundInventoryDispositionEvent MapEvent(
            IDataReader reader)
        {
            return new RefundInventoryDispositionEvent
            {
                Id = ReadInt64(reader, "Id"),
                DispositionItemId = ReadInt32(reader, "DispositionItemId"),
                ActionId = ReadNullableInt64(reader, "ActionId"),
                RefundRequestId = ReadInt32(reader, "RefundRequestId"),
                EventType = ReadString(reader, "EventType") ?? string.Empty,
                Status = ReadString(reader, "Status"),
                Message = ReadString(reader, "Message", trim: false),
                DetailsJson = ReadString(reader, "DetailsJson", trim: false),
                CreatedByUserId = ReadNullableInt32(reader, "CreatedByUserId"),
                CreatedByName = ReadString(reader, "CreatedByName"),
                DateCreated = ReadDateTime(reader, "DateCreated")
            };
        }

        private static int ReadInt32(IDataReader reader, string name)
        {
            int ordinal = reader.GetOrdinal(name);
            return reader.IsDBNull(ordinal) ? 0 : Convert.ToInt32(reader.GetValue(ordinal));
        }

        private static long ReadInt64(IDataReader reader, string name)
        {
            int ordinal = reader.GetOrdinal(name);
            return reader.IsDBNull(ordinal) ? 0L : Convert.ToInt64(reader.GetValue(ordinal));
        }

        private static int? ReadNullableInt32(IDataReader reader, string name)
        {
            int ordinal = reader.GetOrdinal(name);
            return reader.IsDBNull(ordinal) ? null : Convert.ToInt32(reader.GetValue(ordinal));
        }

        private static long? ReadNullableInt64(IDataReader reader, string name)
        {
            int ordinal = reader.GetOrdinal(name);
            return reader.IsDBNull(ordinal) ? null : Convert.ToInt64(reader.GetValue(ordinal));
        }

        private static DateTime ReadDateTime(IDataReader reader, string name)
        {
            int ordinal = reader.GetOrdinal(name);
            return Convert.ToDateTime(reader.GetValue(ordinal));
        }

        private static DateTime? ReadNullableDateTime(IDataReader reader, string name)
        {
            int ordinal = reader.GetOrdinal(name);
            return reader.IsDBNull(ordinal) ? null : Convert.ToDateTime(reader.GetValue(ordinal));
        }

        private static string? ReadString(
            IDataReader reader,
            string name,
            bool trim = true)
        {
            int ordinal = reader.GetOrdinal(name);
            if (reader.IsDBNull(ordinal))
            {
                return null;
            }

            string value = Convert.ToString(reader.GetValue(ordinal)) ?? string.Empty;
            return trim ? value.Trim() : value;
        }

        private static byte[]? ReadBytes(IDataReader reader, string name)
        {
            int ordinal = reader.GetOrdinal(name);
            return reader.IsDBNull(ordinal) ? null : (byte[])reader.GetValue(ordinal);
        }

        private static object DbValue(string? value, bool trim = true)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return DBNull.Value;
            }

            return trim ? value.Trim() : value;
        }
    }
}
