namespace Site_2024.Web.Api.Models
{
    public class AdminDiscountCode
    {
        public int Id { get; set; }

        public string? Code { get; set; }
        public string? Title { get; set; }

        public string? DiscountType { get; set; }
        public decimal DiscountValue { get; set; }

        public string? AppliesToType { get; set; }

        public int? PartId { get; set; }
        public string? PartName { get; set; }
        public string? PartNumber { get; set; }

        public long? ShopifyProductId { get; set; }
        public long? ShopifyVariantId { get; set; }

        public string? CustomerEmail { get; set; }

        public DateTime? StartsAtUtc { get; set; }
        public DateTime? EndsAtUtc { get; set; }

        public int UsageLimit { get; set; }
        public bool OncePerCustomer { get; set; }

        public string? ShopifyDiscountGid { get; set; }

        public string? ShopifyCollectionGid { get; set; }
        public string? ShopifyCollectionHandle { get; set; }
        public bool MatchAllRules { get; set; } = true;
        public bool AutoMaintainEligibility { get; set; }
        public DateTime? LastCollectionSyncUtc { get; set; }
        public string? LastCollectionSyncStatus { get; set; }
        public string? LastCollectionSyncError { get; set; }
        public int RuleCount { get; set; }
        public string? RuleSummary { get; set; }
        public List<AdminDiscountCodeRule> Rules { get; set; } = new();

        public string? Status { get; set; }
        public int UsageCount { get; set; }

        public string? AdminNotes { get; set; }

        public bool ShowSiteBanner { get; set; }
        public string? BannerHeadline { get; set; }
        public string? BannerMessage { get; set; }
        public string? BannerLinkText { get; set; }
        public string? BannerLinkUrl { get; set; }
        public int BannerPriority { get; set; }

        public int? CreatedByUserId { get; set; }
        public string? CreatedByName { get; set; }

        public int? DeactivatedByUserId { get; set; }
        public string? DeactivatedByName { get; set; }

        public DateTime DateCreated { get; set; }
        public DateTime DateModified { get; set; }
        public DateTime? DeactivatedDateUtc { get; set; }

        public int TotalCount { get; set; }
    }
}
