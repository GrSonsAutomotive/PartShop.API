using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Serialization;

namespace Site_2024.Web.Api.Models.Shopify
{
    public class ShopifyRefundExecutionResult
    {
        public long ShopifyRefundId { get; set; }
        public string ShopifyRefundGid { get; set; } = string.Empty;
        public decimal ActualRefundedAmount { get; set; }
        public string CurrencyCode { get; set; } = string.Empty;
        public List<ShopifyRefundTransactionResult> Transactions { get; set; } =
            new List<ShopifyRefundTransactionResult>();
        public string RawResponseJson { get; set; } = string.Empty;

        [JsonIgnore]
        public ShopifyRefundTransactionResult? PrimaryTransaction =>
            Transactions.FirstOrDefault();

        [JsonIgnore]
        public bool IsFinanciallySuccessful
        {
            get
            {
                if (ActualRefundedAmount == 0m)
                {
                    return true;
                }

                return Transactions.Count > 0
                    && Transactions.All(transaction =>
                        string.Equals(
                            transaction.Status,
                            "SUCCESS",
                            StringComparison.OrdinalIgnoreCase));
            }
        }

        [JsonIgnore]
        public bool HasFailedTransaction =>
            Transactions.Any(transaction =>
                string.Equals(transaction.Status, "FAILURE", StringComparison.OrdinalIgnoreCase)
                || string.Equals(transaction.Status, "ERROR", StringComparison.OrdinalIgnoreCase));
    }

    public class ShopifyRefundTransactionResult
    {
        public long? ShopifyTransactionId { get; set; }
        public string? ShopifyTransactionGid { get; set; }
        public string? Status { get; set; }
        public string? Kind { get; set; }
        public string? Gateway { get; set; }
        public decimal Amount { get; set; }
        public string CurrencyCode { get; set; } = string.Empty;
    }

    public class ShopifyInventoryQuantityCommitResult
    {
        public long InventoryItemId { get; set; }
        public string InventoryItemGid { get; set; } = string.Empty;
        public string LocationGid { get; set; } = string.Empty;
        public int PreviousQuantity { get; set; }
        public int NewQuantity { get; set; }
        public int? QuantityAfterChange { get; set; }
        public int? Delta { get; set; }
        public string IdempotencyKey { get; set; } = string.Empty;
        public string ReferenceDocumentUri { get; set; } = string.Empty;
    }

    internal class ShopifyPreparedSuggestedTransaction
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
