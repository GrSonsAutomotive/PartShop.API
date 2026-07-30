using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;
using System.Linq;
using System.Text.Json;
using Site_2024.Models.Domain.RefundRequests;
using Site_2024.Models.Requests.RefundRequests;
using Site_2024.Web.Api.Constructors;
using Site_2024.Web.Api.Extensions;
using Site_2024.Web.Api.Interfaces;
using Site_2024.Web.Api.Models;

namespace Site_2024.Web.Api.Services
{
    public class RefundRequestService : IRefundRequestService
    {
        private readonly IDataProvider _data;

        public RefundRequestService(IDataProvider data)
        {
            _data = data;
        }

        public int Add(RefundRequestAddRequest model, int? userId)
        {
            if (model == null)
            {
                throw new ArgumentNullException(nameof(model));
            }

            bool hasReference =
                model.PartId.HasValue
                || !string.IsNullOrWhiteSpace(model.ShopifyOrderId)
                || !string.IsNullOrWhiteSpace(model.OrderNumber);

            if (!hasReference)
            {
                throw new InvalidOperationException(
                    "Enter a Part Id, Shopify Order Id, or Order Number so the request can be located later.");
            }

            int id = 0;
            const string procName = "[dbo].[RefundRequests_Insert]";

            _data.ExecuteCmd(procName,
                inputParamMapper: delegate (SqlParameterCollection col)
                {
                    AddCommonParams(model, col);

                    col.AddWithValue(
                        "@CreatedByUserId",
                        userId.HasValue ? userId.Value : DBNull.Value
                    );

                    SqlParameter idOut = new SqlParameter("@Id", SqlDbType.Int)
                    {
                        Direction = ParameterDirection.Output,
                        Value = 0
                    };

                    col.Add(idOut);
                },
                singleRecordMapper: delegate (IDataReader reader, short set)
                {
                    if (set == 0)
                    {
                        id = Convert.ToInt32(reader["Id"]);
                    }
                });

            if (id <= 0)
            {
                throw new Exception("RefundRequests_Insert did not return a valid RefundRequest Id.");
            }

            if (model.Items != null)
            {
                foreach (RefundRequestItemAddRequest item in model.Items)
                {
                    if (item == null)
                    {
                        continue;
                    }

                    bool isDefaultPrimaryItem = model.PartId.HasValue
                        && item.PartId == model.PartId.Value
                        && item.ShopifyLineItemId == null
                        && item.Quantity == 1
                        && string.IsNullOrWhiteSpace(item.ItemNotes);

                    if (!isDefaultPrimaryItem)
                    {
                        AddItem(id, item);
                    }
                }
            }

            if (model.Photos != null)
            {
                foreach (RefundRequestPhotoAddRequest photo in model.Photos)
                {
                    if (photo != null && !string.IsNullOrWhiteSpace(photo.Url))
                    {
                        AddPhoto(id, photo);
                    }
                }
            }

            return id;
        }

        public RefundRequest? GetById(int id)
        {
            RefundRequest? refundRequest = null;
            const string procName = "[dbo].[RefundRequests_GetById]";

            _data.ExecuteCmd(procName,
                inputParamMapper: delegate (SqlParameterCollection col)
                {
                    col.AddWithValue("@Id", id);
                },
                singleRecordMapper: delegate (IDataReader reader, short set)
                {
                    int startingIndex = 0;

                    if (set == 0)
                    {
                        refundRequest = MapRefundRequest(reader, ref startingIndex);
                    }
                    else if (set == 1 && refundRequest != null)
                    {
                        RefundRequestItem item = MapRefundRequestItem(reader, ref startingIndex);
                        refundRequest.Items.Add(item);
                    }
                    else if (set == 2 && refundRequest != null)
                    {
                        RefundRequestPhoto photo = MapRefundRequestPhoto(reader, ref startingIndex);
                        refundRequest.Photos.Add(photo);
                    }
                    else if (set == 3 && refundRequest != null)
                    {
                        RefundRequestShippingEvent shippingEvent =
                            MapRefundRequestShippingEvent(
                                reader,
                                ref startingIndex);

                        refundRequest.ShippingEvents.Add(shippingEvent);
                    }
                    else if (set == 4 && refundRequest != null)
                    {
                        RefundRequestInspectionEvent inspectionEvent =
                            MapRefundRequestInspectionEvent(
                                reader,
                                ref startingIndex);

                        refundRequest.InspectionEvents.Add(inspectionEvent);
                    }
                });

            if (refundRequest != null)
            {
                refundRequest.ItemCount = refundRequest.Items.Count;
                refundRequest.PhotoCount = refundRequest.Photos.Count;
            }

            return refundRequest;
        }

        public Paged<RefundRequest>? GetPaginated(int pageIndex, int pageSize, RefundRequestSearchRequest model)
        {
            Paged<RefundRequest>? pagedList = null;
            List<RefundRequest>? list = null;
            int totalCount = 0;

            const string procName = "[dbo].[RefundRequests_GetPaginated]";

            _data.ExecuteCmd(procName,
                inputParamMapper: delegate (SqlParameterCollection col)
                {
                    col.AddWithValue("@PageIndex", pageIndex);
                    col.AddWithValue("@PageSize", pageSize);
                    col.AddWithValue("@Status", string.IsNullOrWhiteSpace(model?.Status) ? DBNull.Value : model.Status);
                    col.AddWithValue("@PartId", model?.PartId.HasValue == true ? model.PartId.Value : DBNull.Value);
                    col.AddWithValue("@ShopifyOrderId", model?.ShopifyOrderId.HasValue == true ? model.ShopifyOrderId.Value : DBNull.Value);
                    col.AddWithValue("@OrderNumber", string.IsNullOrWhiteSpace(model?.OrderNumber) ? DBNull.Value : model.OrderNumber);
                    col.AddWithValue("@CustomerEmail", string.IsNullOrWhiteSpace(model?.CustomerEmail) ? DBNull.Value : model.CustomerEmail);
                },
                singleRecordMapper: delegate (IDataReader reader, short set)
                {
                    int startingIndex = 0;
                    RefundRequest refundRequest = MapRefundRequestForPaged(reader, ref startingIndex);

                    if (totalCount == 0)
                    {
                        totalCount = reader.GetSafeInt32(startingIndex++);
                    }

                    list ??= new List<RefundRequest>();
                    list.Add(refundRequest);
                });

            if (list != null)
            {
                pagedList = new Paged<RefundRequest>(list, pageIndex, pageSize, totalCount);
            }

            return pagedList;
        }

        public List<ReturnReason> GetReasons()
        {
            List<ReturnReason> list = new List<ReturnReason>();
            const string procName = "[dbo].[ReturnReasons_SelectAll]";

            _data.ExecuteCmd(procName,
                inputParamMapper: null,
                singleRecordMapper: delegate (IDataReader reader, short set)
                {
                    int startingIndex = 0;
                    list.Add(MapReturnReason(reader, ref startingIndex));
                });

            return list;
        }

        public List<ReturnStatus> GetStatuses()
        {
            List<ReturnStatus> list = new List<ReturnStatus>();
            const string procName = "[dbo].[ReturnStatuses_SelectAll]";

            _data.ExecuteCmd(procName,
                inputParamMapper: null,
                singleRecordMapper: delegate (IDataReader reader, short set)
                {
                    int startingIndex = 0;
                    list.Add(MapReturnStatus(reader, ref startingIndex));
                });

            return list;
        }

        public int AddItem(int refundRequestId, RefundRequestItemAddRequest model)
        {
            int id = 0;
            const string procName = "[dbo].[RefundRequestItems_Insert]";

            _data.ExecuteNonQuery(procName,
                inputParamMapper: delegate (SqlParameterCollection col)
                {
                    col.AddWithValue("@RefundRequestId", refundRequestId);
                    col.AddWithValue("@PartId", model.PartId);
                    col.AddWithValue("@ShopifyLineItemId", model.ShopifyLineItemId.HasValue ? model.ShopifyLineItemId.Value : DBNull.Value);
                    col.AddWithValue("@Quantity", model.Quantity <= 0 ? 1 : model.Quantity);
                    col.AddWithValue("@ItemNotes", string.IsNullOrWhiteSpace(model.ItemNotes) ? DBNull.Value : model.ItemNotes);

                    SqlParameter idOut = new SqlParameter("@Id", SqlDbType.Int)
                    {
                        Direction = ParameterDirection.Output
                    };
                    col.Add(idOut);
                },
                returnParameters: delegate (SqlParameterCollection returnCollection)
                {
                    object oId = returnCollection["@Id"].Value;
                    int.TryParse(oId.ToString(), out id);
                });

            return id;
        }

        public int AddPhoto(int refundRequestId, RefundRequestPhotoAddRequest model)
        {
            int id = 0;
            const string procName = "[dbo].[RefundRequestPhotos_Insert]";

            _data.ExecuteNonQuery(procName,
                inputParamMapper: delegate (SqlParameterCollection col)
                {
                    col.AddWithValue("@RefundRequestId", refundRequestId);
                    col.AddWithValue("@RefundRequestItemId", model.RefundRequestItemId.HasValue ? model.RefundRequestItemId.Value : DBNull.Value);
                    col.AddWithValue("@Url", model.Url);
                    col.AddWithValue("@OriginalFileName", string.IsNullOrWhiteSpace(model.OriginalFileName) ? DBNull.Value : model.OriginalFileName);
                    col.AddWithValue("@ContentType", string.IsNullOrWhiteSpace(model.ContentType) ? DBNull.Value : model.ContentType);
                    col.AddWithValue("@SortOrder", model.SortOrder);

                    SqlParameter idOut = new SqlParameter("@Id", SqlDbType.Int)
                    {
                        Direction = ParameterDirection.Output
                    };
                    col.Add(idOut);
                },
                returnParameters: delegate (SqlParameterCollection returnCollection)
                {
                    object oId = returnCollection["@Id"].Value;
                    int.TryParse(oId.ToString(), out id);
                });

            return id;
        }


        public void ReplaceMatchedShopifyItems(
            int refundRequestId,
            ShopifyOrderSummary order,
            List<RefundRequestShopifyItemSelectionRequest> selections)
        {
            if (refundRequestId <= 0)
            {
                throw new InvalidOperationException(
                    "A valid refund request is required.");
            }

            if (order == null || order.ShopifyOrderId <= 0)
            {
                throw new InvalidOperationException(
                    "A valid Shopify order is required.");
            }

            if (selections == null || selections.Count == 0)
            {
                throw new InvalidOperationException(
                    "Select at least one order item.");
            }

            List<object> itemSnapshots = new List<object>();

            foreach (
                RefundRequestShopifyItemSelectionRequest selection
                in selections)
            {
                if (!long.TryParse(
                        selection.ShopifyLineItemId,
                        out long lineItemId)
                    ||
                    lineItemId <= 0)
                {
                    throw new InvalidOperationException(
                        "One of the selected Shopify line items is invalid.");
                }

                ShopifyOrderLineItemSummary? lineItem =
                    order.LineItems.FirstOrDefault(
                        item =>
                            item.ShopifyLineItemId ==
                            lineItemId);

                if (lineItem == null)
                {
                    throw new InvalidOperationException(
                        $"Shopify line item {lineItemId} was not found on the order.");
                }

                if (selection.Quantity <= 0
                    ||
                    selection.Quantity > lineItem.Quantity)
                {
                    throw new InvalidOperationException(
                        $"Return quantity for {lineItem.Title} must be between 1 and {lineItem.Quantity}.");
                }

                itemSnapshots.Add(
                    new
                    {
                        shopifyLineItemId =
                            lineItem.ShopifyLineItemId,
                        partId =
                            lineItem.LocalPart?.PartId,
                        productTitle =
                            lineItem.Title,
                        sku =
                            lineItem.Sku,
                        quantity =
                            selection.Quantity,
                        quantityPurchased =
                            lineItem.Quantity,
                        unitPrice =
                            lineItem.UnitPrice,
                        currencyCode =
                            lineItem.CurrencyCode,
                        shopifyVariantId =
                            lineItem.ShopifyVariantId,
                        shopifyProductId =
                            lineItem.ShopifyProductId,
                        imageUrl =
                            lineItem.LocalPart?.ImageUrl
                            ?? lineItem.ShopifyImageUrl,
                        conditionName =
                            lineItem.LocalPart?.ConditionName,
                        isPartsNotWorking =
                            lineItem.LocalPart?.IsPartsNotWorking
                            ?? false
                    });
            }

            string itemsJson =
                JsonSerializer.Serialize(itemSnapshots);

            const string procName =
                "[dbo].[RefundRequestItems_ReplaceFromShopifyOrder]";

            _data.ExecuteNonQuery(
                procName,
                inputParamMapper:
                    delegate (SqlParameterCollection col)
                    {
                        col.AddWithValue(
                            "@RefundRequestId",
                            refundRequestId);

                        col.AddWithValue(
                            "@ShopifyOrderId",
                            order.ShopifyOrderId);

                        col.AddWithValue(
                            "@OrderNumber",
                            order.Name);

                        col.AddWithValue(
                            "@ItemsJson",
                            itemsJson);
                    },
                returnParameters: null);
        }

        public List<RefundRequestDuplicateConflict>
            GetDuplicateConflicts(int refundRequestId)
        {
            List<RefundRequestDuplicateConflict> list =
                new List<RefundRequestDuplicateConflict>();

            const string procName =
                "[dbo].[RefundRequests_GetDuplicateLineItemConflicts]";

            _data.ExecuteCmd(
                procName,
                inputParamMapper:
                    delegate (SqlParameterCollection col)
                    {
                        col.AddWithValue(
                            "@RefundRequestId",
                            refundRequestId);
                    },
                singleRecordMapper:
                    delegate (IDataReader reader, short set)
                    {
                        int index = 0;

                        list.Add(
                            new RefundRequestDuplicateConflict
                            {
                                RefundRequestId =
                                    reader.GetSafeInt32(index++),
                                Status =
                                    reader.GetSafeString(index++),
                                ShopifyLineItemId =
                                    reader.GetSafeInt64(index++)
                            });
                    });

            return list;
        }

        public void ApplyDecision(
            int id,
            RefundRequestDecisionRequest model,
            ReturnEligibilityEvaluation eligibility,
            int userId)
        {
            const string procName =
                "[dbo].[RefundRequests_ApplyDecision]";

            string summary =
                eligibility?.Summary ?? string.Empty;

            _data.ExecuteNonQuery(
                procName,
                inputParamMapper:
                    delegate (SqlParameterCollection col)
                    {
                        col.AddWithValue("@Id", id);
                        col.AddWithValue(
                            "@Decision",
                            model.Decision);
                        col.AddWithValue(
                            "@ResolvedByUserId",
                            userId);
                        col.AddWithValue(
                            "@ShopifyDeliveredAt",
                            eligibility?.DeliveredAt.HasValue == true
                                ? eligibility.DeliveredAt.Value
                                : DBNull.Value);
                        col.AddWithValue(
                            "@ReturnWindowEndsAt",
                            eligibility?.ReturnWindowEndsAt.HasValue == true
                                ? eligibility.ReturnWindowEndsAt.Value
                                : DBNull.Value);
                        col.AddWithValue(
                            "@EligibilityStatus",
                            eligibility?.EligibilityStatus
                                ?? "ManualReview");
                        col.AddWithValue(
                            "@EligibilitySummary",
                            string.IsNullOrWhiteSpace(summary)
                                ? DBNull.Value
                                : summary);
                        col.AddWithValue(
                            "@CustomerEmailMatched",
                            eligibility?.CustomerEmailMatches == true);
                        col.AddWithValue(
                            "@IsInternational",
                            eligibility?.IsInternational == true);
                        col.AddWithValue(
                            "@DestinationCountryCode",
                            string.IsNullOrWhiteSpace(
                                eligibility?.DestinationCountryCode)
                                ? DBNull.Value
                                : eligibility.DestinationCountryCode);
                        col.AddWithValue(
                            "@SellerError",
                            model.SellerError.HasValue
                                ? model.SellerError.Value
                                : DBNull.Value);
                        col.AddWithValue(
                            "@ReturnShippingPayer",
                            string.IsNullOrWhiteSpace(
                                model.ReturnShippingPayer)
                                ? DBNull.Value
                                : model.ReturnShippingPayer);
                        col.AddWithValue(
                            "@CustomerInstructions",
                            string.IsNullOrWhiteSpace(
                                model.CustomerInstructions)
                                ? DBNull.Value
                                : model.CustomerInstructions);
                        col.AddWithValue(
                            "@AdminNotes",
                            string.IsNullOrWhiteSpace(model.AdminNotes)
                                ? DBNull.Value
                                : model.AdminNotes);
                        col.AddWithValue(
                            "@DenialReason",
                            string.IsNullOrWhiteSpace(
                                model.DenialReason)
                                ? DBNull.Value
                                : model.DenialReason);
                        col.AddWithValue(
                            "@PolicyOverrideUsed",
                            model.UsePolicyOverride);
                        col.AddWithValue(
                            "@PolicyOverrideReason",
                            string.IsNullOrWhiteSpace(
                                model.PolicyOverrideReason)
                                ? DBNull.Value
                                : model.PolicyOverrideReason);
                    },
                returnParameters: null);
        }

        public void MarkDecisionEmailResult(
            int id,
            bool wasSent,
            string? errorMessage)
        {
            const string procName =
                "[dbo].[RefundRequests_DecisionEmailResult_Update]";

            _data.ExecuteNonQuery(
                procName,
                inputParamMapper:
                    delegate (SqlParameterCollection col)
                    {
                        col.AddWithValue("@Id", id);
                        col.AddWithValue("@WasSent", wasSent);
                        col.AddWithValue(
                            "@ErrorMessage",
                            string.IsNullOrWhiteSpace(errorMessage)
                                ? DBNull.Value
                                : errorMessage.Trim());
                    },
                returnParameters: null);
        }

        public void SaveReturnLabel(
            int id,
            string storedFilePath,
            string originalFileName,
            string contentType,
            RefundRequestReturnLabelRequest model,
            int userId)
        {
            const string procName =
                "[dbo].[RefundRequests_ReturnLabel_Save]";

            _data.ExecuteNonQuery(
                procName,
                inputParamMapper:
                    delegate (SqlParameterCollection col)
                    {
                        col.AddWithValue("@Id", id);
                        col.AddWithValue(
                            "@LabelFilePath",
                            storedFilePath);
                        col.AddWithValue(
                            "@LabelOriginalFileName",
                            originalFileName);
                        col.AddWithValue(
                            "@LabelContentType",
                            contentType);
                        col.AddWithValue(
                            "@Carrier",
                            model.Carrier.Trim());
                        col.AddWithValue(
                            "@TrackingNumber",
                            model.TrackingNumber.Trim());
                        col.AddWithValue(
                            "@LabelCost",
                            model.LabelCost.HasValue
                                ? model.LabelCost.Value
                                : DBNull.Value);
                        col.AddWithValue(
                            "@Notes",
                            string.IsNullOrWhiteSpace(model.Notes)
                                ? DBNull.Value
                                : model.Notes.Trim());
                        col.AddWithValue("@UserId", userId);
                    },
                returnParameters: null);
        }

        public void MarkReturnLabelEmailResult(
            int id,
            bool wasSent,
            string? errorMessage)
        {
            const string procName =
                "[dbo].[RefundRequests_ReturnLabelEmailResult_Update]";

            _data.ExecuteNonQuery(
                procName,
                inputParamMapper:
                    delegate (SqlParameterCollection col)
                    {
                        col.AddWithValue("@Id", id);
                        col.AddWithValue("@WasSent", wasSent);
                        col.AddWithValue(
                            "@ErrorMessage",
                            string.IsNullOrWhiteSpace(errorMessage)
                                ? DBNull.Value
                                : errorMessage.Trim());
                    },
                returnParameters: null);
        }

        public void UpdateReturnTracking(
            int id,
            RefundRequestReturnTrackingRequest model,
            int userId)
        {
            const string procName =
                "[dbo].[RefundRequests_ReturnTracking_Update]";

            _data.ExecuteNonQuery(
                procName,
                inputParamMapper:
                    delegate (SqlParameterCollection col)
                    {
                        col.AddWithValue("@Id", id);
                        col.AddWithValue("@Carrier", model.Carrier.Trim());
                        col.AddWithValue(
                            "@TrackingNumber",
                            model.TrackingNumber.Trim());
                        col.AddWithValue(
                            "@ShippedAt",
                            model.ShippedAt.HasValue
                                ? model.ShippedAt.Value
                                : DBNull.Value);
                        col.AddWithValue(
                            "@Notes",
                            string.IsNullOrWhiteSpace(model.Notes)
                                ? DBNull.Value
                                : model.Notes.Trim());
                        col.AddWithValue("@UserId", userId);
                    },
                returnParameters: null);
        }

        public void MarkReturnDelivered(
            int id,
            RefundRequestReturnDeliveredRequest model,
            int userId)
        {
            const string procName =
                "[dbo].[RefundRequests_ReturnDelivered_Update]";

            _data.ExecuteNonQuery(
                procName,
                inputParamMapper:
                    delegate (SqlParameterCollection col)
                    {
                        col.AddWithValue("@Id", id);
                        col.AddWithValue(
                            "@DeliveredAt",
                            model.DeliveredAt.HasValue
                                ? model.DeliveredAt.Value
                                : DBNull.Value);
                        col.AddWithValue(
                            "@Notes",
                            string.IsNullOrWhiteSpace(model.Notes)
                                ? DBNull.Value
                                : model.Notes.Trim());
                        col.AddWithValue("@UserId", userId);
                    },
                returnParameters: null);
        }

        public void MarkItemReceived(
            int id,
            RefundRequestMarkReceivedRequest model,
            int userId)
        {
            const string procName =
                "[dbo].[RefundRequests_ItemReceived_Update]";

            _data.ExecuteNonQuery(
                procName,
                inputParamMapper:
                    delegate (SqlParameterCollection col)
                    {
                        col.AddWithValue("@Id", id);
                        col.AddWithValue(
                            "@ReceivedAt",
                            model.ReceivedAt.HasValue
                                ? model.ReceivedAt.Value
                                : DBNull.Value);
                        col.AddWithValue(
                            "@Notes",
                            string.IsNullOrWhiteSpace(model.Notes)
                                ? DBNull.Value
                                : model.Notes.Trim());
                        col.AddWithValue("@UserId", userId);
                    },
                returnParameters: null);
        }

        public void CompleteInspection(
            int id,
            RefundRequestCompleteInspectionRequest model,
            int userId)
        {
            string itemsJson =
                JsonSerializer.Serialize(
                    model.Items.Select(
                        item => new
                        {
                            refundRequestItemId =
                                item.RefundRequestItemId,
                            quantityReceived =
                                item.QuantityReceived,
                            isSameItem =
                                item.IsSameItem,
                            isComplete =
                                item.IsComplete,
                            isAltered =
                                item.IsAltered,
                            hasNewDamage =
                                item.HasNewDamage,
                            inspectionNotes =
                                item.InspectionNotes,
                            restockQuantity =
                                item.RestockQuantity,
                            holdQuantity =
                                item.HoldQuantity,
                            damagedQuantity =
                                item.DamagedQuantity
                        }));

            const string procName =
                "[dbo].[RefundRequests_Inspection_Complete]";

            _data.ExecuteNonQuery(
                procName,
                inputParamMapper:
                    delegate (SqlParameterCollection col)
                    {
                        col.AddWithValue("@Id", id);
                        col.AddWithValue(
                            "@InspectionSummary",
                            model.InspectionSummary.Trim());
                        col.AddWithValue("@ItemsJson", itemsJson);
                        col.AddWithValue("@UserId", userId);
                    },
                returnParameters: null);
        }

        public void UpdateStatus(int id, RefundRequestUpdateStatusRequest model, int userId)
        {
            const string procName = "[dbo].[RefundRequests_UpdateStatus]";

            _data.ExecuteNonQuery(procName,
                inputParamMapper: delegate (SqlParameterCollection col)
                {
                    col.AddWithValue("@Id", id);
                    col.AddWithValue("@Status", model.Status);
                    col.AddWithValue("@Notes", string.IsNullOrWhiteSpace(model.Notes) ? DBNull.Value : model.Notes);
                    col.AddWithValue("@ResolvedByUserId", userId);
                    col.AddWithValue("@AdminNotes", string.IsNullOrWhiteSpace(model.AdminNotes) ? DBNull.Value : model.AdminNotes);
                    col.AddWithValue("@DenialReason", string.IsNullOrWhiteSpace(model.DenialReason) ? DBNull.Value : model.DenialReason);
                },
                returnParameters: null);
        }

        private static RefundRequest MapRefundRequest(IDataReader reader, ref int startingIndex)
        {
            RefundRequest model = new RefundRequest();

            model.Id = reader.GetSafeInt32(startingIndex++);
            model.PartId = reader.GetSafeInt32Nullable(startingIndex++);
            model.PartName = reader.GetSafeString(startingIndex++);
            model.PartNumber = reader.GetSafeString(startingIndex++);
            model.Price = reader.GetSafeDecimal(startingIndex++);
            model.PartShopifyOrderId = reader.GetSafeInt64Nullable(startingIndex++);
            model.ShopifyOrderId = reader.GetSafeInt64Nullable(startingIndex++);
            model.Reason = reader.GetSafeString(startingIndex++);
            model.Notes = reader.GetSafeString(startingIndex++);
            model.Status = reader.GetSafeString(startingIndex++);
            model.StatusId = reader.GetSafeInt32Nullable(startingIndex++);
            model.StatusName = reader.GetSafeString(startingIndex++);
            model.OrderNumber = reader.GetSafeString(startingIndex++);
            model.CustomerEmail = reader.GetSafeString(startingIndex++);
            model.RequestedPartName = reader.GetSafeString(startingIndex++);
            model.RequestedQuantity = reader.GetSafeInt32Nullable(startingIndex++);
            model.ReturnReasonId = reader.GetSafeInt32Nullable(startingIndex++);
            model.ReturnReasonName = reader.GetSafeString(startingIndex++);
            model.RequiresNotes = reader.GetSafeBool(startingIndex++);
            model.RequiresPhotos = reader.GetSafeBool(startingIndex++);
            model.AdminNotes = reader.GetSafeString(startingIndex++);
            model.DenialReason = reader.GetSafeString(startingIndex++);
            model.DateCreated = reader.GetSafeDateTime(startingIndex++);
            model.DateModified = reader.GetSafeDateTime(startingIndex++);
            model.CreatedByUserId = reader.GetSafeInt32Nullable(startingIndex++);
            model.CreatedByName = reader.GetSafeString(startingIndex++);
            model.ResolvedByUserId = reader.GetSafeInt32Nullable(startingIndex++);
            model.ResolvedByName = reader.GetSafeString(startingIndex++);
            model.ResolvedDate = reader.GetSafeDateTimeNullable(startingIndex++);
            model.ShopifyDeliveredAt = reader.GetSafeDateTimeNullable(startingIndex++);
            model.ReturnWindowEndsAt = reader.GetSafeDateTimeNullable(startingIndex++);
            model.EligibilityStatus = reader.GetSafeString(startingIndex++);
            model.EligibilitySummary = reader.GetSafeString(startingIndex++);
            model.EligibilityCheckedAt = reader.GetSafeDateTimeNullable(startingIndex++);
            model.CustomerEmailMatched = reader.GetSafeBoolNullable(startingIndex++);
            model.IsInternational = reader.GetSafeBoolNullable(startingIndex++);
            model.DestinationCountryCode = reader.GetSafeString(startingIndex++);
            model.SellerError = reader.GetSafeBoolNullable(startingIndex++);
            model.ReturnShippingPayer = reader.GetSafeString(startingIndex++);
            model.CustomerInstructions = reader.GetSafeString(startingIndex++);
            model.ApprovalExpiresAt = reader.GetSafeDateTimeNullable(startingIndex++);
            model.PolicyOverrideUsed = reader.GetSafeBool(startingIndex++);
            model.PolicyOverrideReason = reader.GetSafeString(startingIndex++);
            model.DecisionEmailStatus = reader.GetSafeString(startingIndex++);
            model.DecisionEmailSentAt = reader.GetSafeDateTimeNullable(startingIndex++);
            model.DecisionEmailLastAttemptAt = reader.GetSafeDateTimeNullable(startingIndex++);
            model.DecisionEmailLastError = reader.GetSafeString(startingIndex++);
            model.DecisionEmailAttempts = reader.GetSafeInt32(startingIndex++);
            model.ReturnLogisticsStatus = reader.GetSafeString(startingIndex++);
            model.ReturnLabelSource = reader.GetSafeString(startingIndex++);
            model.ReturnLabelUrl = reader.GetSafeString(startingIndex++);
            model.ReturnLabelFilePath = reader.GetSafeString(startingIndex++);
            model.ReturnLabelOriginalFileName = reader.GetSafeString(startingIndex++);
            model.ReturnLabelContentType = reader.GetSafeString(startingIndex++);
            model.ReturnCarrier = reader.GetSafeString(startingIndex++);
            model.ReturnTrackingNumber = reader.GetSafeString(startingIndex++);
            model.ReturnLabelCost = reader.GetSafeDecimalNullable(startingIndex++);
            model.ReturnLabelCreatedAt = reader.GetSafeDateTimeNullable(startingIndex++);
            model.ReturnLabelCreatedByUserId = reader.GetSafeInt32Nullable(startingIndex++);
            model.ReturnLabelSentAt = reader.GetSafeDateTimeNullable(startingIndex++);
            model.ReturnShippedAt = reader.GetSafeDateTimeNullable(startingIndex++);
            model.ReturnDeliveredAt = reader.GetSafeDateTimeNullable(startingIndex++);
            model.ReturnShippingNotes = reader.GetSafeString(startingIndex++);
            model.ReturnLabelEmailStatus = reader.GetSafeString(startingIndex++);
            model.ReturnLabelEmailSentAt = reader.GetSafeDateTimeNullable(startingIndex++);
            model.ReturnLabelEmailLastAttemptAt = reader.GetSafeDateTimeNullable(startingIndex++);
            model.ReturnLabelEmailLastError = reader.GetSafeString(startingIndex++);
            model.ReturnLabelEmailAttempts = reader.GetSafeInt32(startingIndex++);
            model.ReturnTrackingLastUpdatedAt = reader.GetSafeDateTimeNullable(startingIndex++);
            model.ReturnTrackingLastUpdatedByUserId = reader.GetSafeInt32Nullable(startingIndex++);
            model.ItemReceivedAt = reader.GetSafeDateTimeNullable(startingIndex++);
            model.ItemReceivedByUserId = reader.GetSafeInt32Nullable(startingIndex++);
            model.ItemReceivedByName = reader.GetSafeString(startingIndex++);
            model.ItemReceivedNotes = reader.GetSafeString(startingIndex++);
            model.InspectionStatus = reader.GetSafeString(startingIndex++);
            model.InspectionCompletedAt = reader.GetSafeDateTimeNullable(startingIndex++);
            model.InspectedByUserId = reader.GetSafeInt32Nullable(startingIndex++);
            model.InspectedByName = reader.GetSafeString(startingIndex++);
            model.InspectionSummary = reader.GetSafeString(startingIndex++);
            model.ReadyForRefundAt = reader.GetSafeDateTimeNullable(startingIndex++);

            return model;
        }

        private static RefundRequest MapRefundRequestForPaged(IDataReader reader, ref int startingIndex)
        {
            RefundRequest model = new RefundRequest();

            model.Id = reader.GetSafeInt32(startingIndex++);
            model.PartId = reader.GetSafeInt32Nullable(startingIndex++);
            model.PartName = reader.GetSafeString(startingIndex++);
            model.PartNumber = reader.GetSafeString(startingIndex++);
            model.Price = reader.GetSafeDecimal(startingIndex++);
            model.ShopifyOrderId = reader.GetSafeInt64Nullable(startingIndex++);
            model.Reason = reader.GetSafeString(startingIndex++);
            model.Status = reader.GetSafeString(startingIndex++);
            model.StatusId = reader.GetSafeInt32Nullable(startingIndex++);
            model.StatusName = reader.GetSafeString(startingIndex++);
            model.OrderNumber = reader.GetSafeString(startingIndex++);
            model.CustomerEmail = reader.GetSafeString(startingIndex++);
            model.RequestedPartName = reader.GetSafeString(startingIndex++);
            model.RequestedQuantity = reader.GetSafeInt32Nullable(startingIndex++);
            model.ReturnReasonId = reader.GetSafeInt32Nullable(startingIndex++);
            model.ReturnReasonName = reader.GetSafeString(startingIndex++);
            model.ItemCount = reader.GetSafeInt32(startingIndex++);
            model.PhotoCount = reader.GetSafeInt32(startingIndex++);
            model.DateCreated = reader.GetSafeDateTime(startingIndex++);
            model.DateModified = reader.GetSafeDateTime(startingIndex++);
            model.CreatedByUserId = reader.GetSafeInt32Nullable(startingIndex++);
            model.CreatedByName = reader.GetSafeString(startingIndex++);
            model.ResolvedByUserId = reader.GetSafeInt32Nullable(startingIndex++);
            model.ResolvedByName = reader.GetSafeString(startingIndex++);
            model.ResolvedDate = reader.GetSafeDateTimeNullable(startingIndex++);
            model.EligibilityStatus = reader.GetSafeString(startingIndex++);
            model.ApprovalExpiresAt = reader.GetSafeDateTimeNullable(startingIndex++);
            model.ReturnLogisticsStatus = reader.GetSafeString(startingIndex++);
            model.ReturnCarrier = reader.GetSafeString(startingIndex++);
            model.ReturnTrackingNumber = reader.GetSafeString(startingIndex++);
            model.ReturnShippedAt = reader.GetSafeDateTimeNullable(startingIndex++);
            model.ReturnDeliveredAt = reader.GetSafeDateTimeNullable(startingIndex++);
            model.InspectionStatus = reader.GetSafeString(startingIndex++);
            model.ItemReceivedAt = reader.GetSafeDateTimeNullable(startingIndex++);
            model.InspectionCompletedAt = reader.GetSafeDateTimeNullable(startingIndex++);
            model.ReadyForRefundAt = reader.GetSafeDateTimeNullable(startingIndex++);

            return model;
        }

        private static RefundRequestItem MapRefundRequestItem(
            IDataReader reader,
            ref int startingIndex)
        {
            RefundRequestItem model =
                new RefundRequestItem();

            model.Id =
                reader.GetSafeInt32(startingIndex++);
            model.RefundRequestId =
                reader.GetSafeInt32(startingIndex++);
            model.PartId =
                reader.GetSafeInt32Nullable(startingIndex++);
            model.PartName =
                reader.GetSafeString(startingIndex++);
            model.PartNumber =
                reader.GetSafeString(startingIndex++);
            model.Price =
                reader.GetSafeDecimal(startingIndex++);
            model.Image =
                reader.GetSafeString(startingIndex++);
            model.ShopifyLineItemId =
                reader.GetSafeInt64Nullable(startingIndex++);
            model.Quantity =
                reader.GetSafeInt32(startingIndex++);
            model.ItemNotes =
                reader.GetSafeString(startingIndex++);
            model.DateCreated =
                reader.GetSafeDateTime(startingIndex++);

            model.ProductTitle =
                reader.GetSafeString(startingIndex++);
            model.Sku =
                reader.GetSafeString(startingIndex++);
            model.UnitPrice =
                reader.GetSafeDecimalNullable(startingIndex++);
            model.CurrencyCode =
                reader.GetSafeString(startingIndex++);
            model.QuantityPurchased =
                reader.GetSafeInt32Nullable(startingIndex++);
            model.ShopifyVariantId =
                reader.GetSafeInt64Nullable(startingIndex++);
            model.ShopifyProductId =
                reader.GetSafeInt64Nullable(startingIndex++);
            model.ImageUrl =
                reader.GetSafeString(startingIndex++);
            model.ConditionName =
                reader.GetSafeString(startingIndex++);
            model.IsPartsNotWorking =
                reader.GetSafeBool(startingIndex++);
            model.QuantityReceived =
                reader.GetSafeInt32Nullable(startingIndex++);
            model.IsSameItem =
                reader.GetSafeBoolNullable(startingIndex++);
            model.IsComplete =
                reader.GetSafeBoolNullable(startingIndex++);
            model.IsAltered =
                reader.GetSafeBoolNullable(startingIndex++);
            model.HasNewDamage =
                reader.GetSafeBoolNullable(startingIndex++);
            model.InspectionNotes =
                reader.GetSafeString(startingIndex++);
            model.InventoryDisposition =
                reader.GetSafeString(startingIndex++);
            model.ProposedRestockQuantity =
                reader.GetSafeInt32Nullable(startingIndex++);
            model.RestockQuantity =
                reader.GetSafeInt32Nullable(startingIndex++);
            model.HoldQuantity =
                reader.GetSafeInt32Nullable(startingIndex++);
            model.DamagedQuantity =
                reader.GetSafeInt32Nullable(startingIndex++);
            model.InspectionCompletedAt =
                reader.GetSafeDateTimeNullable(startingIndex++);
            model.InspectedByUserId =
                reader.GetSafeInt32Nullable(startingIndex++);
            model.InspectedByName =
                reader.GetSafeString(startingIndex++);

            return model;
        }

        private static RefundRequestShippingEvent
            MapRefundRequestShippingEvent(
                IDataReader reader,
                ref int startingIndex)
        {
            RefundRequestShippingEvent model =
                new RefundRequestShippingEvent();

            model.Id = reader.GetSafeInt32(startingIndex++);
            model.RefundRequestId = reader.GetSafeInt32(startingIndex++);
            model.EventType = reader.GetSafeString(startingIndex++);
            model.LogisticsStatus = reader.GetSafeString(startingIndex++);
            model.Carrier = reader.GetSafeString(startingIndex++);
            model.TrackingNumber = reader.GetSafeString(startingIndex++);
            model.LabelUrl = reader.GetSafeString(startingIndex++);
            model.LabelCost = reader.GetSafeDecimalNullable(startingIndex++);
            model.Notes = reader.GetSafeString(startingIndex++);
            model.CreatedByUserId = reader.GetSafeInt32Nullable(startingIndex++);
            model.CreatedByName = reader.GetSafeString(startingIndex++);
            model.DateCreated = reader.GetSafeDateTime(startingIndex++);

            return model;
        }

        private static RefundRequestInspectionEvent
            MapRefundRequestInspectionEvent(
                IDataReader reader,
                ref int startingIndex)
        {
            RefundRequestInspectionEvent model =
                new RefundRequestInspectionEvent();

            model.Id = reader.GetSafeInt32(startingIndex++);
            model.RefundRequestId = reader.GetSafeInt32(startingIndex++);
            model.RefundRequestItemId = reader.GetSafeInt32Nullable(startingIndex++);
            model.EventType = reader.GetSafeString(startingIndex++);
            model.QuantityReceived = reader.GetSafeInt32Nullable(startingIndex++);
            model.InventoryDisposition = reader.GetSafeString(startingIndex++);
            model.RestockQuantity = reader.GetSafeInt32Nullable(startingIndex++);
            model.HoldQuantity = reader.GetSafeInt32Nullable(startingIndex++);
            model.DamagedQuantity = reader.GetSafeInt32Nullable(startingIndex++);
            model.Notes = reader.GetSafeString(startingIndex++);
            model.CreatedByUserId = reader.GetSafeInt32Nullable(startingIndex++);
            model.CreatedByName = reader.GetSafeString(startingIndex++);
            model.DateCreated = reader.GetSafeDateTime(startingIndex++);

            return model;
        }

        private static RefundRequestPhoto MapRefundRequestPhoto(IDataReader reader, ref int startingIndex)
        {
            RefundRequestPhoto model = new RefundRequestPhoto();

            model.Id = reader.GetSafeInt32(startingIndex++);
            model.RefundRequestId = reader.GetSafeInt32(startingIndex++);
            model.RefundRequestItemId = reader.GetSafeInt32Nullable(startingIndex++);
            model.Url = reader.GetSafeString(startingIndex++);
            model.OriginalFileName = reader.GetSafeString(startingIndex++);
            model.ContentType = reader.GetSafeString(startingIndex++);
            model.SortOrder = reader.GetSafeInt32(startingIndex++);
            model.DateCreated = reader.GetSafeDateTime(startingIndex++);

            return model;
        }

        private static ReturnReason MapReturnReason(IDataReader reader, ref int startingIndex)
        {
            ReturnReason model = new ReturnReason();

            model.Id = reader.GetSafeInt32(startingIndex++);
            model.Name = reader.GetSafeString(startingIndex++);
            model.RequiresNotes = reader.GetSafeBool(startingIndex++);
            model.RequiresPhotos = reader.GetSafeBool(startingIndex++);
            model.IsActive = reader.GetSafeBool(startingIndex++);
            model.SortOrder = reader.GetSafeInt32(startingIndex++);
            model.DateCreated = reader.GetSafeDateTime(startingIndex++);
            model.DateModified = reader.GetSafeDateTime(startingIndex++);

            return model;
        }

        private static ReturnStatus MapReturnStatus(IDataReader reader, ref int startingIndex)
        {
            ReturnStatus model = new ReturnStatus();

            model.Id = reader.GetSafeInt32(startingIndex++);
            model.Name = reader.GetSafeString(startingIndex++);
            model.IsTerminal = reader.GetSafeBool(startingIndex++);
            model.SortOrder = reader.GetSafeInt32(startingIndex++);
            model.DateCreated = reader.GetSafeDateTime(startingIndex++);
            model.DateModified = reader.GetSafeDateTime(startingIndex++);

            return model;
        }

        private static void AddCommonParams(RefundRequestAddRequest model, SqlParameterCollection col)
        {
            long? shopifyOrderId = ParseShopifyOrderId(model.ShopifyOrderId);

            col.AddWithValue(
                "@PartId",
                model.PartId.HasValue ? model.PartId.Value : DBNull.Value);

            col.AddWithValue(
                "@ShopifyOrderId",
                shopifyOrderId.HasValue ? shopifyOrderId.Value : DBNull.Value);

            col.AddWithValue("@Reason", model.Reason);
            col.AddWithValue("@Notes", string.IsNullOrWhiteSpace(model.Notes) ? DBNull.Value : model.Notes);
            col.AddWithValue("@OrderNumber", string.IsNullOrWhiteSpace(model.OrderNumber) ? DBNull.Value : model.OrderNumber);
            col.AddWithValue("@CustomerEmail", string.IsNullOrWhiteSpace(model.CustomerEmail) ? DBNull.Value : model.CustomerEmail);
            col.AddWithValue("@RequestedPartName", string.IsNullOrWhiteSpace(model.RequestedPartName) ? DBNull.Value : model.RequestedPartName);
            col.AddWithValue("@RequestedQuantity", model.RequestedQuantity.HasValue ? model.RequestedQuantity.Value : DBNull.Value);
            col.AddWithValue("@ReturnReasonId", model.ReturnReasonId.HasValue ? model.ReturnReasonId.Value : DBNull.Value);
        }

        private static long? ParseShopifyOrderId(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return null;
            }

            if (!long.TryParse(value.Trim(), out long result) || result <= 0)
            {
                throw new InvalidOperationException(
                    "Shopify Order Id must be a valid positive number.");
            }

            return result;
        }
    }
}
