using System;
using System.Collections.Generic;

namespace Site_2024.Web.Api.Models
{
    public class ShopifyShippingTagSourcePlan
    {
        public int PolicyId { get; set; }
        public string PolicyName { get; set; } = string.Empty;
        public string CollectionGid { get; set; } = string.Empty;
        public string CollectionTitle { get; set; } = string.Empty;
        public int ProductCount { get; set; }
        public string ShippingClassTag { get; set; } = string.Empty;
        public List<string> ExistingSourceGids { get; set; } = new();
        public bool AlreadyConfigured { get; set; }
        public bool HasConflictingShippingTagRules { get; set; }
        public bool RequiresSourceCreation => !AlreadyConfigured && !HasConflictingShippingTagRules;
    }

    public class ShopifyShippingTagSourceAddRequest
    {
        public string ExpectedTitle { get; set; } = string.Empty;
        public int ExpectedProductCount { get; set; } = -1;
        public List<string> ExpectedSourceGids { get; set; } = new();
        // Exact explicit confirmation is required on each attempt.
        public string Confirmation { get; set; } = string.Empty;
    }

    public class ShopifyShippingTagSourceAddResult
    {
        public int PolicyId { get; set; }
        public string CollectionGid { get; set; } = string.Empty;
        public string ShippingClassTag { get; set; } = string.Empty;
        public bool SourceCreated { get; set; }
        public int ProductCountBefore { get; set; }
        public int ProductCountAfter { get; set; }
        public bool? JobDone { get; set; }
        public string? JobGid { get; set; }
        public List<string> PreservedSourceGids { get; set; } = new();
        public ShopifyShippingCollectionInfo? CollectionAfter { get; set; }
    }

    public class ShopifyCollectionSourceCreateResult
    {
        public ShopifyShippingCollectionInfo Collection { get; set; } = new();
        public string? JobGid { get; set; }
        public bool? JobDone { get; set; }
    }
}
