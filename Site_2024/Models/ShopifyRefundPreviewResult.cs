using System;
using System.Collections.Generic;

namespace Site_2024.Web.Api.Models
{
    public class ShopifyRefundPreviewOptions
    {
        public bool IncludeOriginalShippingRefund { get; set; }
        public decimal AdditionalDeductionAmount { get; set; }
        public string? AdditionalDeductionReason { get; set; }
    }

    public class ShopifyRefundPreviewResult
    {
        public int RefundRequestId { get; set; }
        public long ShopifyOrderId { get; set; }
        public string ShopifyOrderGid { get; set; } = string.Empty;
        public string OrderName { get; set; } = string.Empty;
        public bool OrderIsRefundable { get; set; }

        public string CurrencyCode { get; set; } = string.Empty;
        public string ShopCurrencyCode { get; set; } = string.Empty;

        public bool SellerError { get; set; }
        public bool IsInternational { get; set; }
        public string? ReturnShippingPayer { get; set; }
        public bool OriginalShippingRequested { get; set; }
        public bool OriginalShippingAllowed { get; set; }

        public decimal MerchandiseSubtotalBeforeDiscountAmount { get; set; }
        public decimal MerchandiseDiscountAmount { get; set; }
        public decimal CartDiscountAmount { get; set; }
        public decimal MerchandiseRefundAmount { get; set; }
        public decimal TaxRefundAmount { get; set; }
        public decimal ShopifySuggestedItemRefundAmount { get; set; }

        public decimal ShopifyShippingBaseRefundableAmount { get; set; }
        public decimal ShopifyShippingTaxRefundableAmount { get; set; }
        public decimal ShopifyShippingRefundableAmount { get; set; }
        public decimal OriginalShippingRefundAmount { get; set; }

        public decimal BuyerPaidLabelDeductionAmount { get; set; }
        public decimal AdditionalDeductionAmount { get; set; }
        public string? AdditionalDeductionReason { get; set; }

        public decimal ShopifyMaximumRefundableAmount { get; set; }
        public decimal FinalRefundAmount { get; set; }
        public DateTime PreviewedAtUtc { get; set; }

        // Persist this exact payload with the prepared calculation in Step 34C.
        public string ShopifyPreviewJson { get; set; } = string.Empty;

        public List<ShopifyRefundPreviewLineItem> Items { get; set; } =
            new List<ShopifyRefundPreviewLineItem>();

        public List<ShopifyRefundSuggestedTransaction> SuggestedTransactions
            { get; set; } =
                new List<ShopifyRefundSuggestedTransaction>();
    }

    public class ShopifyRefundPreviewLineItem
    {
        public int RefundRequestItemId { get; set; }
        public int? PartId { get; set; }
        public string? PartName { get; set; }
        public string? PartNumber { get; set; }

        public long ShopifyLineItemId { get; set; }
        public string ShopifyLineItemGid { get; set; } = string.Empty;
        public string ShopifyTitle { get; set; } = string.Empty;
        public string? ShopifySku { get; set; }

        public int QuantityPurchased { get; set; }
        public int ShopifyRefundableQuantity { get; set; }
        public int QuantityReceived { get; set; }
        public int QuantityToRefund { get; set; }

        public int RestockQuantity { get; set; }
        public int HoldQuantity { get; set; }
        public int DamagedQuantity { get; set; }

        public decimal ShopifySubtotalAmount { get; set; }
        public decimal ShopifyTaxAmount { get; set; }
        public decimal ShopifyTotalAmount { get; set; }
        public string CurrencyCode { get; set; } = string.Empty;
    }

    public class ShopifyRefundSuggestedTransaction
    {
        public string Kind { get; set; } = string.Empty;
        public string? Gateway { get; set; }
        public string? FormattedGateway { get; set; }
        public string? AccountNumber { get; set; }

        public decimal Amount { get; set; }
        public decimal? MaximumRefundableAmount { get; set; }
        public string CurrencyCode { get; set; } = string.Empty;

        public long? ParentTransactionId { get; set; }
        public string? ParentTransactionGid { get; set; }
        public string? ParentTransactionStatus { get; set; }
        public string? ParentTransactionKind { get; set; }
        public string? ParentTransactionGateway { get; set; }
    }
}
