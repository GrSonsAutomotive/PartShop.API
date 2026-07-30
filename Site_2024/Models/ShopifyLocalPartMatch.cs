namespace Site_2024.Web.Api.Models
{
    public class ShopifyLocalPartMatch
    {
        public int PartId { get; set; }
        public string? PartName { get; set; }
        public string? PartNumber { get; set; }
        public string? ImageUrl { get; set; }

        public List<string> ImageUrls { get; set; } =
            new List<string>();

        public int AvailableId { get; set; }
        public string? AvailableStatus { get; set; }
        public string? SiteName { get; set; }
        public string? AreaName { get; set; }
        public string? AisleName { get; set; }
        public string? ShelfName { get; set; }
        public string? SectionName { get; set; }
        public string? BoxName { get; set; }
        public string? OtherBox { get; set; }
        public long ShopifyVariantId { get; set; }
        public long? ShopifyOrderId { get; set; }
        public DateTime? SoldOnUtc { get; set; }
        public int Quantity { get; set; }
        public int? ConditionId { get; set; }
        public string? ConditionName { get; set; }

        public bool IsPartsNotWorking =>
            string.Equals(
                ConditionName?.Trim(),
                "Parts / Not Working",
                StringComparison.OrdinalIgnoreCase);

        public string LocationCode
        {
            get
            {
                List<string> parts = new List<string>();

                AddLocationPart(parts, SiteName, "S");
                AddLocationPart(parts, AreaName, "A");
                AddLocationPart(parts, AisleName, "AI");
                AddLocationPart(parts, ShelfName, "SH");
                AddLocationPart(parts, SectionName, "SE");
                AddLocationPart(parts, BoxName, "BX");

                if (!string.IsNullOrWhiteSpace(OtherBox))
                {
                    parts.Add($"OTHER-{OtherBox.Trim()}");
                }

                return string.Join("-", parts);
            }
        }

        private static void AddLocationPart(List<string> parts, string? value, string prefix)
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                parts.Add($"{prefix}{value.Trim()}");
            }
        }
    }
}
