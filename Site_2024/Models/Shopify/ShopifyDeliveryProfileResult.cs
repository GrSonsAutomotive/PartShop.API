namespace Site_2024.Web.Api.Models.Shopify
{
    public class ShopifyDeliveryProfileResult
    {
        public string Gid { get; set; } = string.Empty;
        public long Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public bool IsDefault { get; set; }
    }
}
