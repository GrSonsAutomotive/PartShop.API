namespace Site_2024.Web.Api.Models
{
    public class ShopifyReturnOrderLookupResult
    {
        public ShopifyOrderSummary Order { get; set; } =
            new ShopifyOrderSummary();

        public string? RequestedEmail { get; set; }

        public bool CustomerEmailMatches { get; set; }
    }
}
