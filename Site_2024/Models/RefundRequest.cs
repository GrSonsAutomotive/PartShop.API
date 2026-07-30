using System;
using System.Collections.Generic;

namespace Site_2024.Models.Domain.RefundRequests
{
    public class RefundRequest
    {
        public int Id { get; set; }
        public int? PartId { get; set; }
        public string? PartName { get; set; }
        public string? PartNumber { get; set; }
        public decimal Price { get; set; }
        public long? PartShopifyOrderId { get; set; }
        public long? ShopifyOrderId { get; set; }
        public string? Reason { get; set; }
        public string? Notes { get; set; }
        public string? Status { get; set; }
        public int? StatusId { get; set; }
        public string? StatusName { get; set; }
        public string? OrderNumber { get; set; }
        public string? CustomerEmail { get; set; }
        public string? RequestedPartName { get; set; }
        public int? RequestedQuantity { get; set; }
        public int? ReturnReasonId { get; set; }
        public string? ReturnReasonName { get; set; }
        public bool RequiresNotes { get; set; }
        public bool RequiresPhotos { get; set; }
        public string? AdminNotes { get; set; }
        public string? DenialReason { get; set; }
        public int ItemCount { get; set; }
        public int PhotoCount { get; set; }
        public DateTime DateCreated { get; set; }
        public DateTime DateModified { get; set; }
        public int? CreatedByUserId { get; set; }
        public string? CreatedByName { get; set; }
        public int? ResolvedByUserId { get; set; }
        public string? ResolvedByName { get; set; }
        public DateTime? ResolvedDate { get; set; }
        public DateTime? ShopifyDeliveredAt { get; set; }
        public DateTime? ReturnWindowEndsAt { get; set; }
        public string? EligibilityStatus { get; set; }
        public string? EligibilitySummary { get; set; }
        public DateTime? EligibilityCheckedAt { get; set; }
        public bool? CustomerEmailMatched { get; set; }
        public bool? IsInternational { get; set; }
        public string? DestinationCountryCode { get; set; }
        public bool? SellerError { get; set; }
        public string? ReturnShippingPayer { get; set; }
        public string? CustomerInstructions { get; set; }
        public DateTime? ApprovalExpiresAt { get; set; }
        public bool PolicyOverrideUsed { get; set; }
        public string? PolicyOverrideReason { get; set; }
        public string? DecisionEmailStatus { get; set; }
        public DateTime? DecisionEmailSentAt { get; set; }
        public DateTime? DecisionEmailLastAttemptAt { get; set; }
        public string? DecisionEmailLastError { get; set; }
        public int DecisionEmailAttempts { get; set; }

        public string? ReturnLogisticsStatus { get; set; }
        public string? ReturnLabelSource { get; set; }
        public string? ReturnLabelUrl { get; set; }
        public string? ReturnLabelFilePath { get; set; }
        public string? ReturnLabelOriginalFileName { get; set; }
        public string? ReturnLabelContentType { get; set; }
        public string? ReturnCarrier { get; set; }
        public string? ReturnTrackingNumber { get; set; }
        public decimal? ReturnLabelCost { get; set; }
        public DateTime? ReturnLabelCreatedAt { get; set; }
        public int? ReturnLabelCreatedByUserId { get; set; }
        public DateTime? ReturnLabelSentAt { get; set; }
        public DateTime? ReturnShippedAt { get; set; }
        public DateTime? ReturnDeliveredAt { get; set; }
        public string? ReturnShippingNotes { get; set; }
        public string? ReturnLabelEmailStatus { get; set; }
        public DateTime? ReturnLabelEmailSentAt { get; set; }
        public DateTime? ReturnLabelEmailLastAttemptAt { get; set; }
        public string? ReturnLabelEmailLastError { get; set; }
        public int ReturnLabelEmailAttempts { get; set; }
        public DateTime? ReturnTrackingLastUpdatedAt { get; set; }
        public int? ReturnTrackingLastUpdatedByUserId { get; set; }

        public DateTime? ItemReceivedAt { get; set; }
        public int? ItemReceivedByUserId { get; set; }
        public string? ItemReceivedByName { get; set; }
        public string? ItemReceivedNotes { get; set; }
        public string? InspectionStatus { get; set; }
        public DateTime? InspectionCompletedAt { get; set; }
        public int? InspectedByUserId { get; set; }
        public string? InspectedByName { get; set; }
        public string? InspectionSummary { get; set; }
        public DateTime? ReadyForRefundAt { get; set; }

        public List<RefundRequestItem> Items { get; set; } = new List<RefundRequestItem>();
        public List<RefundRequestPhoto> Photos { get; set; } = new List<RefundRequestPhoto>();
        public List<RefundRequestShippingEvent> ShippingEvents { get; set; } = new List<RefundRequestShippingEvent>();
        public List<RefundRequestInspectionEvent> InspectionEvents { get; set; } = new List<RefundRequestInspectionEvent>();
    }

    public class RefundRequestItem
    {
        public int Id { get; set; }
        public int RefundRequestId { get; set; }
        public int? PartId { get; set; }
        public string? PartName { get; set; }
        public string? PartNumber { get; set; }
        public decimal Price { get; set; }
        public string? Image { get; set; }
        public long? ShopifyLineItemId { get; set; }
        public int Quantity { get; set; }
        public string? ItemNotes { get; set; }
        public DateTime DateCreated { get; set; }

        public string? ProductTitle { get; set; }
        public string? Sku { get; set; }
        public decimal? UnitPrice { get; set; }
        public string? CurrencyCode { get; set; }
        public int? QuantityPurchased { get; set; }
        public long? ShopifyVariantId { get; set; }
        public long? ShopifyProductId { get; set; }
        public string? ImageUrl { get; set; }
        public string? ConditionName { get; set; }
        public bool IsPartsNotWorking { get; set; }

        public int? QuantityReceived { get; set; }
        public bool? IsSameItem { get; set; }
        public bool? IsComplete { get; set; }
        public bool? IsAltered { get; set; }
        public bool? HasNewDamage { get; set; }
        public string? InspectionNotes { get; set; }
        public string? InventoryDisposition { get; set; }
        public int? ProposedRestockQuantity { get; set; }
        public int? RestockQuantity { get; set; }
        public int? HoldQuantity { get; set; }
        public int? DamagedQuantity { get; set; }
        public DateTime? InspectionCompletedAt { get; set; }
        public int? InspectedByUserId { get; set; }
        public string? InspectedByName { get; set; }
    }

    public class RefundRequestPhoto
    {
        public int Id { get; set; }
        public int RefundRequestId { get; set; }
        public int? RefundRequestItemId { get; set; }
        public string? Url { get; set; }
        public string? OriginalFileName { get; set; }
        public string? ContentType { get; set; }
        public int SortOrder { get; set; }
        public DateTime DateCreated { get; set; }
    }

    public class RefundRequestShippingEvent
    {
        public int Id { get; set; }
        public int RefundRequestId { get; set; }
        public string? EventType { get; set; }
        public string? LogisticsStatus { get; set; }
        public string? Carrier { get; set; }
        public string? TrackingNumber { get; set; }
        public string? LabelUrl { get; set; }
        public decimal? LabelCost { get; set; }
        public string? Notes { get; set; }
        public int? CreatedByUserId { get; set; }
        public string? CreatedByName { get; set; }
        public DateTime DateCreated { get; set; }
    }

    public class RefundRequestInspectionEvent
    {
        public int Id { get; set; }
        public int RefundRequestId { get; set; }
        public int? RefundRequestItemId { get; set; }
        public string? EventType { get; set; }
        public int? QuantityReceived { get; set; }
        public string? InventoryDisposition { get; set; }
        public int? RestockQuantity { get; set; }
        public int? HoldQuantity { get; set; }
        public int? DamagedQuantity { get; set; }
        public string? Notes { get; set; }
        public int? CreatedByUserId { get; set; }
        public string? CreatedByName { get; set; }
        public DateTime DateCreated { get; set; }
    }

    public class ReturnReason
    {
        public int Id { get; set; }
        public string? Name { get; set; }
        public bool RequiresNotes { get; set; }
        public bool RequiresPhotos { get; set; }
        public bool IsActive { get; set; }
        public int SortOrder { get; set; }
        public DateTime DateCreated { get; set; }
        public DateTime DateModified { get; set; }
    }

    public class ReturnStatus
    {
        public int Id { get; set; }
        public string? Name { get; set; }
        public bool IsTerminal { get; set; }
        public int SortOrder { get; set; }
        public DateTime DateCreated { get; set; }
        public DateTime DateModified { get; set; }
    }
}
