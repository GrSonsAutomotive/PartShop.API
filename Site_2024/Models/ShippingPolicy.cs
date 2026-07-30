namespace Site_2024.Web.Api.Models
{
    public class ShippingPolicy
    {
        public int Id { get; set; }
        public string Name { get; set; }
        public long? ShopifyProfileId { get; set; }
        public bool IsActive { get; set; }
        public bool AllowsOnlineCheckout { get; set; } = true;
        public DateTime DateCreated { get; set; }
        public DateTime DateModified { get; set; }
    }
}
