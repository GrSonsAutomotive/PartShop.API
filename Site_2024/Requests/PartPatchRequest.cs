namespace Site_2024.Web.Api.Requests
{
    public sealed class PartPatchRequest
    {
        public string? Name { get; set; }
        public string? PartNumber { get; set; }
        public string? Brand { get; set; }
        public decimal? Price { get; set; }
        public int? AvailableId { get; set; }
        public int? Quantity { get; set; }
        public string? Description { get; set; }
        public string? Image { get; set; }
        public int? LocationId { get; set; }
        public string? OtherBox { get; set; }
        public string? AdminNotes { get; set; }
        public string? Year { get; set; }
        public int? ConditionId { get; set; }
        public int? ShippingPolicyId { get; set; }

        // When supplied, these collections replace the part's complete set of
        // categories / fitments. The first entry remains the legacy primary
        // category / primary make-model used by older queries and screens.
        public List<PartCategoryAddRequest>? Categories { get; set; }
        public List<PartFitmentAddRequest>? Fitments { get; set; }
    }
}
