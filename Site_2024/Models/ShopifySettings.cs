namespace Site_2024.Web.Api.Models
{
    public class ShopifySettings
    {
        public string ShopDomain { get; set; } = string.Empty;
        public string ClientId { get; set; } = string.Empty;
        public string ClientSecret { get; set; } = string.Empty;
        public string ApiVersion { get; set; } = "2026-04";
        public string RedirectUri { get; set; } = string.Empty;
        public string DefaultVendor { get; set; } = "GR&Sons";
        public string DefaultProductType { get; set; } = "Used Auto Part";
        public bool CreateProductsAsDraft { get; set; } = true;
        public string DefaultLocationGid { get; set; } = string.Empty;
        public string OnlineStorePublicationGid { get; set; } = string.Empty;
        public string PublicApiBaseUrl { get; set; } = string.Empty;
        public DateTimeOffset? ProductionCutoverUtc { get; set; }
    }
}
