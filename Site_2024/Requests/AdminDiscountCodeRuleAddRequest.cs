using System.ComponentModel.DataAnnotations;

namespace Site_2024.Web.Api.Requests
{
    public class AdminDiscountCodeRuleAddRequest
    {
        [Required]
        [StringLength(50)]
        public string RuleType { get; set; } = string.Empty;

        public int? SourceId { get; set; }

        [Required]
        [StringLength(200)]
        public string RuleValue { get; set; } = string.Empty;

        public int SortOrder { get; set; }
    }
}
