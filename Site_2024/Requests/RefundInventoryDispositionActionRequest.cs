using System.ComponentModel.DataAnnotations;

namespace Site_2024.Models.Requests.RefundRequests
{
    public class RefundInventoryDispositionActionRequest
    {
        [Required]
        [StringLength(40)]
        public string ActionType { get; set; } = string.Empty;

        [Range(1, int.MaxValue)]
        public int Quantity { get; set; }

        [Required]
        [StringLength(2000)]
        public string Reason { get; set; } = string.Empty;

        [Required]
        [StringLength(100)]
        public string IdempotencyKey { get; set; } = string.Empty;
    }
}
