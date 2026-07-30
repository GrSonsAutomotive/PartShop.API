using System;
using System.Collections.Generic;

namespace Site_2024.Models.Domain.RefundRequests
{
    public class RefundInventoryDisposition
    {
        public int RefundRequestId { get; set; }
        public int RefundFinalizationId { get; set; }
        public string RefundFinalizationStatus { get; set; } = string.Empty;
        public int DispositionItemCount { get; set; }
        public int InitialHoldQuantity { get; set; }
        public int InitialDamagedQuantity { get; set; }
        public int HoldRemainingQuantity { get; set; }
        public int DamagedRemainingQuantity { get; set; }
        public int ReleasedToInventoryQuantity { get; set; }
        public int ConvertedHoldToDamagedQuantity { get; set; }
        public int WrittenOffQuantity { get; set; }
        public int RetainedForPartsQuantity { get; set; }
        public int DisposedQuantity { get; set; }
        public string Status { get; set; } = string.Empty;

        public List<RefundInventoryDispositionItem> Items { get; set; } =
            new List<RefundInventoryDispositionItem>();

        public List<RefundInventoryDispositionAction> Actions { get; set; } =
            new List<RefundInventoryDispositionAction>();

        public List<RefundInventoryDispositionEvent> Events { get; set; } =
            new List<RefundInventoryDispositionEvent>();
    }

    public class RefundInventoryDispositionItem
    {
        public int Id { get; set; }
        public int RefundFinalizationItemId { get; set; }
        public int RefundFinalizationId { get; set; }
        public int RefundRequestId { get; set; }
        public int RefundRequestItemId { get; set; }
        public int? PartId { get; set; }
        public string? PartName { get; set; }
        public string? PartNumber { get; set; }
        public int InitialHoldQuantity { get; set; }
        public int InitialDamagedQuantity { get; set; }
        public int HoldRemainingQuantity { get; set; }
        public int DamagedRemainingQuantity { get; set; }
        public int ReleasedToInventoryQuantity { get; set; }
        public int ConvertedHoldToDamagedQuantity { get; set; }
        public int WrittenOffQuantity { get; set; }
        public int RetainedForPartsQuantity { get; set; }
        public int DisposedQuantity { get; set; }
        public string Status { get; set; } = string.Empty;
        public DateTime DateCreated { get; set; }
        public DateTime DateModified { get; set; }
        public byte[]? RowVersion { get; set; }
    }

    public class RefundInventoryDispositionAction
    {
        public long Id { get; set; }
        public int DispositionItemId { get; set; }
        public int RefundFinalizationItemId { get; set; }
        public int RefundRequestId { get; set; }
        public int? PartId { get; set; }
        public string ActionType { get; set; } = string.Empty;
        public int Quantity { get; set; }
        public string Reason { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public string IdempotencyKey { get; set; } = string.Empty;
        public int PreparedByUserId { get; set; }
        public string? PreparedByName { get; set; }
        public DateTime PreparedAt { get; set; }
        public DateTime? ProcessingStartedAt { get; set; }
        public DateTime? CompletedAt { get; set; }
        public string? LastError { get; set; }
        public string LocalInventoryStatus { get; set; } = string.Empty;
        public int? LocalQuantityBefore { get; set; }
        public int? LocalQuantityAfter { get; set; }
        public DateTime? LocalInventoryCommittedAt { get; set; }
        public int? LocalInventoryCommittedByUserId { get; set; }
        public string? LocalInventoryCommittedByName { get; set; }
        public string ShopifyInventoryStatus { get; set; } = string.Empty;
        public string? ShopifyInventoryIdempotencyKey { get; set; }
        public int ShopifyInventoryAttemptCount { get; set; }
        public DateTime? ShopifyInventoryLastAttemptAt { get; set; }
        public string? ShopifyInventoryLastError { get; set; }
        public DateTime? ShopifyInventorySyncedAt { get; set; }
        public DateTime DateCreated { get; set; }
        public DateTime DateModified { get; set; }
        public byte[]? RowVersion { get; set; }
    }

    public class RefundInventoryDispositionEvent
    {
        public long Id { get; set; }
        public int DispositionItemId { get; set; }
        public long? ActionId { get; set; }
        public int RefundRequestId { get; set; }
        public string EventType { get; set; } = string.Empty;
        public string? Status { get; set; }
        public string? Message { get; set; }
        public string? DetailsJson { get; set; }
        public int? CreatedByUserId { get; set; }
        public string? CreatedByName { get; set; }
        public DateTime DateCreated { get; set; }
    }
}
