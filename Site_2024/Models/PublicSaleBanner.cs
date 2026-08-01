namespace Site_2024.Web.Api.Models
{
    public class PublicSaleBanner
    {
        public int Id { get; set; }
        public string? Code { get; set; }
        public string? Headline { get; set; }
        public string? Message { get; set; }
        public string? LinkText { get; set; }
        public string? LinkUrl { get; set; }
        public DateTime? StartsAtUtc { get; set; }
        public DateTime? EndsAtUtc { get; set; }
    }
}
