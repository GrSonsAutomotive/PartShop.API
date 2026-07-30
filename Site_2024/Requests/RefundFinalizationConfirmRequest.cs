using System.ComponentModel.DataAnnotations;

namespace Site_2024.Models.Requests.RefundRequests
{
    public class RefundFinalizationConfirmRequest
    {
        [Required]
        public bool ConfirmMoneyMovement { get; set; }

        [Required]
        [StringLength(30)]
        public string ConfirmationText { get; set; } = string.Empty;
    }
}
