using Microsoft.VisualBasic;
using Site_2024.Web.Api.Models.User;

namespace Site_2024.Web.Api.Models
{
    public class Part
    {
        public int Id { get; set; }
        public string Name { get; set; }
        public Catagory Catagory { get; set; }
        public Make Make { get; set; }
        public string Year { get; set; }
        public string PartNumber {  get; set; }
        public string? Brand { get; set; }
        public string Description { get; set; }
        public decimal Price { get; set; }
        public int Quantity { get; set; }
        public long QuantitySold { get; set; }
        public Location Location { get; set; }
        public string Image {  get; set; }
        public Available Available { get; set; }
        public Condition Condition { get; set; }
        public ShippingPolicy ShippingPolicy { get; set; }
        public DateTime DateCreated { get; set; }
        public DateTime DateModified { get; set; }
        public Site_2024.Web.Api.Models.User.User User { get; set; }
        public string? OtherBox { get; set; }
        public string? AdminNotes { get; set; }
        public long? ShopifyProductId { get; set; }
        public long? ShopifyVariantId { get; set; }
        public long? ShopifyInventoryItemId { get; set; }
        public List<PartCategory> Categories { get; set; } = new();
        public List<PartFitment> Fitments { get; set; } = new();

    }
}
