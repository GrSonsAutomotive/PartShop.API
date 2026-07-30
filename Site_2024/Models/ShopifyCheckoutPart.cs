namespace Site_2024.Web.Api.Models
{
    public class ShopifyCheckoutPart
    {
        public int PartId { get; set; }
        public string PartName { get; set; } = string.Empty;
        public string? PartNumber { get; set; }
        public decimal Price { get; set; }
        public int Quantity { get; set; }
        public int AvailableId { get; set; }
        public string AvailableStatus { get; set; } = string.Empty;
        public int? ShippingPolicyId { get; set; }
        public string ShippingPolicyName { get; set; } = string.Empty;
        public bool AllowsOnlineCheckout { get; set; } = true;
        public long? ShopifyProductId { get; set; }
        public long? ShopifyVariantId { get; set; }
        public long? ShopifyOrderId { get; set; }
        public DateTime? SoldOnUtc { get; set; }
    }
}
