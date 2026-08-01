namespace Site_2024.Web.Api.Models
{
    public class ShopifyTagBackfillResult
    {
        public int PartsExamined { get; set; }
        public int ProductsUpdated { get; set; }
        public int ProductsSkipped { get; set; }
        public int ProductsFailed { get; set; }
        public List<string> Errors { get; set; } = new();
    }
}
