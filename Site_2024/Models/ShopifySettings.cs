namespace Site_2024.Web.Api.Models
{
    public class ShopifySettings
    {
        public string ShopDomain { get; set; } = string.Empty;
        public string ClientId { get; set; } = string.Empty;
        public string ClientSecret { get; set; } = string.Empty;
        public string ApiVersion { get; set; } = "2026-04";

        // OFF by default. This does not create Markets rates or migrate Shopify
        // settings. Enable only after a separate Markets test-store validation.
        public bool AllowMarketDrivenShippingPublishing { get; set; } = false;
        // Independently gated: editing Shopify collection membership sources can
        // affect existing products' checkout rates. Opt in per environment.
        public bool AllowMarketDrivenShippingCollectionSourceWrites { get; set; } = false;
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
