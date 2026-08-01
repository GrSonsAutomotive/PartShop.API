namespace Site_2024.Web.Api.Models
{
    public class AdminDiscountCodeRule
    {
        public int Id { get; set; }
        public int AdminDiscountCodeId { get; set; }
        public string? RuleType { get; set; }
        public int? SourceId { get; set; }
        public string? RuleValue { get; set; }
        public string? ShopifyTag { get; set; }
        public string? RuleOperator { get; set; }
        public int SortOrder { get; set; }
        public DateTime DateCreated { get; set; }
        public DateTime DateModified { get; set; }
    }
}
