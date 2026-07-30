using System;
using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Http;

namespace Site_2024.Models.Requests.RefundRequests
{
    public class RefundRequestReturnLabelRequest
    {
        [Required]
        public IFormFile LabelPdf { get; set; } = null!;

        [Required]
        [StringLength(100)]
        public string Carrier { get; set; } = string.Empty;

        [Required]
        [StringLength(200)]
        public string TrackingNumber { get; set; } = string.Empty;

        [Range(typeof(decimal), "0", "100000")]
        public decimal? LabelCost { get; set; }

        [StringLength(2000)]
        public string? Notes { get; set; }
    }

    public class RefundRequestReturnTrackingRequest
    {
        [Required]
        [StringLength(100)]
        public string Carrier { get; set; } = string.Empty;

        [Required]
        [StringLength(200)]
        public string TrackingNumber { get; set; } = string.Empty;

        public DateTime? ShippedAt { get; set; }

        [StringLength(2000)]
        public string? Notes { get; set; }
    }

    public class RefundRequestReturnDeliveredRequest
    {
        public DateTime? DeliveredAt { get; set; }

        [StringLength(2000)]
        public string? Notes { get; set; }
    }
}
