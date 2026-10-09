using System.Collections.Generic;

namespace Site_2024.Web.Api.Models
{
    // Read-only snapshots of Shopify's 2026-07 composable collection model.
    // We never infer collection ownership or shipping eligibility from names alone.
    public class ShopifyShippingCollectionInfo
    {
        public string CollectionGid { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public string Handle { get; set; } = string.Empty;
        public int ProductCount { get; set; }
        public List<ShopifyShippingCollectionSourceInfo> Sources { get; set; } = new();
        public List<string> ShippingClassTags { get; set; } = new();
    }

    public class ShopifyShippingCollectionSourceInfo
    {
        public string SourceGid { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public string SourceType { get; set; } = string.Empty;
        public string InclusionMatchType { get; set; } = string.Empty;
        public string TargetType { get; set; } = string.Empty;
        public int InclusionConditionCount { get; set; }
        public bool HasExplicitSelections { get; set; }
        public bool HasExclusions { get; set; }
        public List<ShopifyShippingCollectionTagCondition> TagConditions { get; set; } = new();
    }

    public class ShopifyShippingCollectionTagCondition
    {
        public string Relation { get; set; } = string.Empty;
        public string MatchType { get; set; } = string.Empty;
        public List<string> Values { get; set; } = new();
    }

    public class ShopifyShippingCollectionPage
    {
        public List<ShopifyShippingCollectionInfo> Collections { get; set; } = new();
        public bool HasNextPage { get; set; }
        public string? EndCursor { get; set; }
    }

    public class ShopifyShippingCollectionAdoptionRequest
    {
        public string CollectionGid { get; set; } = string.Empty;
        // Require the administrator to confirm the exact title shown by Shopify.
        public string ExpectedTitle { get; set; } = string.Empty;
    }
}
