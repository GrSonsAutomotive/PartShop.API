namespace Site_2024.Web.Api.Models
{
    public class ShopifyOrderSummary
    {
        public string ShopifyOrderGid { get; set; } = string.Empty;
        public long ShopifyOrderId { get; set; }
        public int OrderNumber { get; set; }
        public string Name { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; }
        public string? DisplayFinancialStatus { get; set; }
        public string? DisplayFulfillmentStatus { get; set; }
        public decimal TotalPrice { get; set; }
        public string? CurrencyCode { get; set; }
        public DateTime? DeliveredAt { get; set; }
        public string? DestinationCountryCode { get; set; }
        public bool IsInternational { get; set; }

        // Pulled live from Shopify for display only. Do not persist this in Site_2024.
        public string? CustomerDisplayName { get; set; }
        public string? CustomerEmail { get; set; }

        public List<ShopifyOrderLineItemSummary> LineItems { get; set; } = new List<ShopifyOrderLineItemSummary>();
    }
}
