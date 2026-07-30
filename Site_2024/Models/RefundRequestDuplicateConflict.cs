namespace Site_2024.Web.Api.Models
{
    public class RefundRequestDuplicateConflict
    {
        public int RefundRequestId { get; set; }
        public string? Status { get; set; }
        public long ShopifyLineItemId { get; set; }
    }
}
