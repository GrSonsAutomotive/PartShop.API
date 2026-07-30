using System;
using System.Collections.Generic;

namespace Site_2024.Models.Domain.RefundRequests
{
    public class RefundFinalization
    {
        public int Id { get; set; }
        public int RefundRequestId { get; set; }
        public string Status { get; set; } = string.Empty;
        public string IdempotencyKey { get; set; } = string.Empty;
        public int PreparedRevision { get; set; }
        public int PreparedByUserId { get; set; }
        public string? PreparedByName { get; set; }
        public DateTime PreparedAt { get; set; }
        public DateTime? ProcessingStartedAt { get; set; }
        public DateTime? ShopifyRequestStartedAt { get; set; }
        public int? IssuedByUserId { get; set; }
        public string? IssuedByName { get; set; }
        public int AttemptCount { get; set; }
        public DateTime? LastAttemptAt { get; set; }
        public string? LastError { get; set; }

        public long ShopifyOrderId { get; set; }
        public string? ShopifyOrderGid { get; set; }
        public string CurrencyCode { get; set; } = string.Empty;
        public bool? SellerErrorSnapshot { get; set; }
        public bool? IsInternationalSnapshot { get; set; }
        public string? ReturnShippingPayerSnapshot { get; set; }

        public decimal MerchandiseRefundAmount { get; set; }
        public decimal TaxRefundAmount { get; set; }
        public decimal MerchandiseTaxRefundAmount { get; set; }
        public decimal ShopifyShippingRefundableAmount { get; set; }
        public decimal OriginalShippingRefundAmount { get; set; }
        public decimal BuyerPaidLabelDeductionAmount { get; set; }
        public decimal AdditionalDeductionAmount { get; set; }
        public string? AdditionalDeductionReason { get; set; }
        public decimal ShopifyMaximumRefundableAmount { get; set; }
        public decimal FinalRefundAmount { get; set; }

        public string? PreparedCalculationHash { get; set; }
        public string? PreparedCalculationJson { get; set; }
        public string? ShopifyPreviewJson { get; set; }
        public DateTime? ShopifyPreviewedAt { get; set; }

        public long? ShopifyRefundId { get; set; }
        public string? ShopifyRefundGid { get; set; }
        public long? ShopifyTransactionId { get; set; }
        public string? ShopifyTransactionGid { get; set; }
        public string? ShopifyTransactionStatus { get; set; }
        public decimal? ActualRefundedAmount { get; set; }
        public string? ShopifyResponseJson { get; set; }
        public DateTime? ShopifySucceededAt { get; set; }

        public string InventoryStatus { get; set; } = string.Empty;
        public int InventoryAttemptCount { get; set; }
        public DateTime? InventoryLastAttemptAt { get; set; }
        public string? InventoryLastError { get; set; }
        public DateTime? InventoryCompletedAt { get; set; }

        public string CompletionEmailStatus { get; set; } = string.Empty;
        public int CompletionEmailAttempts { get; set; }
        public DateTime? CompletionEmailSentAt { get; set; }
        public DateTime? CompletionEmailLastAttemptAt { get; set; }
        public string? CompletionEmailLastError { get; set; }

        public DateTime? CompletedAt { get; set; }
        public DateTime DateCreated { get; set; }
        public DateTime DateModified { get; set; }
        public byte[]? RowVersion { get; set; }

        public List<RefundFinalizationItem> Items { get; set; } =
            new List<RefundFinalizationItem>();

        public List<RefundFinalizationEvent> Events { get; set; } =
            new List<RefundFinalizationEvent>();
    }

    public class RefundFinalizationItem
    {
        public int Id { get; set; }
        public int RefundFinalizationId { get; set; }
        public int RefundRequestItemId { get; set; }
        public int? PartId { get; set; }
        public string? PartName { get; set; }
        public string? PartNumber { get; set; }
        public long ShopifyLineItemId { get; set; }
        public long? ShopifyVariantId { get; set; }
        public int RefundQuantity { get; set; }
        public int QuantityReceivedSnapshot { get; set; }
        public int ShopifyRefundableQuantitySnapshot { get; set; }
        public decimal MerchandiseRefundAmount { get; set; }
        public decimal TaxRefundAmount { get; set; }
        public decimal ItemRefundAmount { get; set; }
        public string CurrencyCode { get; set; } = string.Empty;

        public int RestockQuantitySnapshot { get; set; }
        public int HoldQuantitySnapshot { get; set; }
        public int DamagedQuantitySnapshot { get; set; }
        public int? LocalQuantityAtPrepare { get; set; }

        public string LocalInventoryStatus { get; set; } = string.Empty;
        public int? LocalQuantityBefore { get; set; }
        public int? LocalQuantityAfter { get; set; }
        public DateTime? LocalInventoryCommittedAt { get; set; }
        public int? LocalInventoryCommittedByUserId { get; set; }
        public string? LocalInventoryCommittedByName { get; set; }
        public string? LocalInventoryLastError { get; set; }

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

    public class RefundFinalizationEvent
    {
        public long Id { get; set; }
        public int RefundFinalizationId { get; set; }
        public int RefundRequestId { get; set; }
        public int? RefundFinalizationItemId { get; set; }
        public string EventType { get; set; } = string.Empty;
        public string? Status { get; set; }
        public string? Message { get; set; }
        public string? DetailsJson { get; set; }
        public int? CreatedByUserId { get; set; }
        public string? CreatedByName { get; set; }
        public DateTime DateCreated { get; set; }
    }
}
