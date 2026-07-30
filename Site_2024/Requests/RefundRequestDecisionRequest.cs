using System.ComponentModel.DataAnnotations;

namespace Site_2024.Models.Requests.RefundRequests
{
    public class RefundRequestDecisionRequest
    {
        [Required]
        [RegularExpression(
            "^(Approve|Deny)$",
            ErrorMessage = "Decision must be Approve or Deny.")]
        public string Decision { get; set; } = string.Empty;

        [StringLength(30)]
        public string? ReturnShippingPayer { get; set; }

        public bool? SellerError { get; set; }

        [StringLength(4000)]
        public string? CustomerInstructions { get; set; }

        [StringLength(4000)]
        public string? AdminNotes { get; set; }

        [StringLength(4000)]
        public string? DenialReason { get; set; }

        public bool UsePolicyOverride { get; set; }

        [StringLength(4000)]
        public string? PolicyOverrideReason { get; set; }
    }
}
