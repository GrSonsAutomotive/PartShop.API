using System.ComponentModel.DataAnnotations;

namespace Site_2024.Web.Api.Requests
{
    public class AdminDiscountCodeAddRequest
    {
        [Required]
        [StringLength(100, MinimumLength = 3)]
        public string Code { get; set; } = string.Empty;

        [StringLength(200)]
        public string? Title { get; set; }

        [Required]
        [StringLength(50)]
        public string DiscountType { get; set; } = string.Empty;
        // Percentage or FixedAmount

        [Required]
        [Range(0.01, 1000000)]
        public decimal DiscountValue { get; set; }

        [Required]
        [StringLength(50)]
        public string AppliesToType { get; set; } = string.Empty;
        // General, Product, Variant, Part, or CollectionRule

        public int? PartId { get; set; }
        public long? ShopifyProductId { get; set; }
        public long? ShopifyVariantId { get; set; }

        public bool MatchAllRules { get; set; } = true;
        public bool AutoMaintainEligibility { get; set; } = true;
        public List<AdminDiscountCodeRuleAddRequest> Rules { get; set; } = new();

        [EmailAddress]
        [StringLength(256)]
        public string? CustomerEmail { get; set; }

        public DateTime? StartsAtUtc { get; set; }
        public DateTime? EndsAtUtc { get; set; }

        [Range(1, int.MaxValue)]
        public int UsageLimit { get; set; } = 1;

        public bool OncePerCustomer { get; set; } = true;

        [StringLength(2000)]
        public string? AdminNotes { get; set; }

        public bool ShowSiteBanner { get; set; }

        [StringLength(200)]
        public string? BannerHeadline { get; set; }

        [StringLength(500)]
        public string? BannerMessage { get; set; }

        [StringLength(100)]
        public string? BannerLinkText { get; set; }

        [StringLength(500)]
        public string? BannerLinkUrl { get; set; }

        [Range(0, 1000000)]
        public int BannerPriority { get; set; }
    }
}
