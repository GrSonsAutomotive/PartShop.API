using System;
using System.Collections.Generic;

namespace Site_2024.Web.Api.Models
{
    public class ReturnEligibilityEvaluation
    {
        public int RefundRequestId { get; set; }
        public bool HasMatchedItems { get; set; }
        public bool CustomerEmailMatches { get; set; }
        public DateTime? DeliveredAt { get; set; }
        public DateTime? ReturnWindowEndsAt { get; set; }
        public bool IsWithinReturnWindow { get; set; }
        public bool IsInternational { get; set; }
        public string? DestinationCountryCode { get; set; }
        public int DuplicateRequestCount { get; set; }
        public bool HasPartsNotWorkingItems { get; set; }
        public bool HasUnknownConditionItems { get; set; }
        public bool RequiresPolicyOverride { get; set; }
        public bool CanApproveWithoutOverride { get; set; }
        public string EligibilityStatus { get; set; } = "ManualReview";
        public string Summary { get; set; } = string.Empty;
        public List<ReturnEligibilityIssue> Issues { get; set; } =
            new List<ReturnEligibilityIssue>();
    }

    public class ReturnEligibilityIssue
    {
        public string Code { get; set; } = string.Empty;
        public string Severity { get; set; } = "Warning";
        public string Message { get; set; } = string.Empty;
        public bool RequiresOverride { get; set; }
    }
}
