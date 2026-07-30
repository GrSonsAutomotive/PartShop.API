using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Http;

namespace Site_2024.Models.Requests.RefundRequests
{
    public class RefundRequestCustomerSubmitRequest
    {
        [Required]
        [StringLength(100)]
        public string OrderNumber { get; set; } = string.Empty;

        [Required]
        [EmailAddress]
        [StringLength(256)]
        public string CustomerEmail { get; set; } = string.Empty;

        [Required]
        [StringLength(500, MinimumLength = 2)]
        public string RequestedPartName { get; set; } = string.Empty;

        [Range(1, 999)]
        public int RequestedQuantity { get; set; } = 1;

        [Required]
        [Range(1, int.MaxValue)]
        public int ReturnReasonId { get; set; }

        [StringLength(4000)]
        public string? Notes { get; set; }

        public List<IFormFile> Photos { get; set; } =
            new List<IFormFile>();
    }
}
