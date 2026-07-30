using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace Site_2024.Models.Requests.RefundRequests
{
    public class RefundRequestMarkReceivedRequest
    {
        public DateTime? ReceivedAt { get; set; }

        [StringLength(2000)]
        public string? Notes { get; set; }
    }

    public class RefundRequestCompleteInspectionRequest
    {
        [Required]
        [StringLength(4000, MinimumLength = 3)]
        public string InspectionSummary { get; set; } = string.Empty;

        [Required]
        [MinLength(1)]
        public List<RefundRequestItemInspectionRequest> Items
            { get; set; } =
                new List<RefundRequestItemInspectionRequest>();
    }

    public class RefundRequestItemInspectionRequest
    {
        [Range(1, int.MaxValue)]
        public int RefundRequestItemId { get; set; }

        [Range(0, 999)]
        public int QuantityReceived { get; set; }

        public bool IsSameItem { get; set; }
        public bool IsComplete { get; set; }
        public bool IsAltered { get; set; }
        public bool HasNewDamage { get; set; }

        [StringLength(2000)]
        public string? InspectionNotes { get; set; }

        [Range(0, 999)]
        public int RestockQuantity { get; set; }

        [Range(0, 999)]
        public int HoldQuantity { get; set; }

        [Range(0, 999)]
        public int DamagedQuantity { get; set; }
    }
}
