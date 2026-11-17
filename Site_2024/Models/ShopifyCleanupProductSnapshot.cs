namespace Site_2024.Web.Api.Models
{
    public sealed class ShopifyCleanupProductSnapshot
    {
        public bool Exists { get; set; }
        public string? ProductGid { get; set; }
        public string? Status { get; set; }
        public List<string> Tags { get; set; } = new();
    }
}
