namespace Site_2024.Web.Api.Models
{
    public class ShopifyShippingReassignmentJob
    {
        public Guid JobId { get; set; }
        public int PartId { get; set; }
        public long ShopifyProductId { get; set; }
        public int FromPolicyId { get; set; }
        public int ToPolicyId { get; set; }
        public int RequestedByUserId { get; set; }
        public bool OriginalPublishedOnOnlineStore { get; set; }
        public string Status { get; set; } = "Queued";
        public bool IsOpen { get; set; }
        public int CheckCount { get; set; }
        public int FailureCount { get; set; }
        public bool? OldCollectionContainsProduct { get; set; }
        public bool? NewCollectionContainsProduct { get; set; }
        public DateTime NextCheckUtc { get; set; }
        public DateTime CreatedUtc { get; set; }
        public DateTime UpdatedUtc { get; set; }
        public DateTime? CompletedUtc { get; set; }
        public string? LastError { get; set; }
        public Guid? LeaseOwner { get; set; }
    }
}
