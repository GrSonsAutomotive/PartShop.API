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
        // Separate, off-by-default gate for removing individual manual selections.
        // Does not authorize automatic shipping-policy reassignment.
        public bool AllowMarketDrivenShippingCollectionMembershipWrites { get; set; } = false;
        // Entirely separate and off by default. Authorizes a controlled
        // unpublish -> tag sync -> collection verify -> SQL patch -> republish.
        // Never enable for production before recovery/volume testing.
        public bool AllowMarketDrivenShippingReassignmentWrites { get; set; } = false;
        // Separate DEV-only gate for safely resuming an interrupted migration.
        public bool AllowMarketDrivenShippingRecoveryWrites { get; set; } = false;
        // Async pilot: schema switch is enabled only AFTER the separate DEV SQL migration.
        public bool AsyncShippingJobsSchemaReady { get; set; } = false;
        // Separate write permission. Never enable for production without review.
        public bool AllowMarketDrivenShippingAsyncReassignmentWrites { get; set; } = false;
        // Background processing runs independently of browser/session and queue gate.
        public bool EnableMarketDrivenShippingAsyncWorker { get; set; } = false;
        // Shipping jobs are locked to the DEV store unless an explicit production
        // store match AND a separate production release flag have been configured.
        // These are independent of the queue/worker/publishing feature flags.
        public string AsyncShippingAuthorizedShopDomain { get; set; } =
            "gr-sons-shipping-dev.myshopify.com";
        public bool AllowProductionAsyncShipping { get; set; } = false;

        public bool IsAsyncShippingStoreAuthorized()
        {
            const string devStore = "gr-sons-shipping-dev.myshopify.com";
            if (string.IsNullOrWhiteSpace(ShopDomain) ||
                string.IsNullOrWhiteSpace(AsyncShippingAuthorizedShopDomain) ||
                !string.Equals(ShopDomain.Trim(),
                    AsyncShippingAuthorizedShopDomain.Trim(),
                    StringComparison.OrdinalIgnoreCase))
                return false;

            return string.Equals(ShopDomain.Trim(), devStore,
                       StringComparison.OrdinalIgnoreCase)
                || AllowProductionAsyncShipping;
        }
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
