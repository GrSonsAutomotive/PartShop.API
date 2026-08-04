using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace Site_2024.Models.Requests.RefundRequests
{
    public class RefundRequestAddRequest
    {
        public Guid? ClientSubmissionId { get; set; }
        [Range(1, int.MaxValue)]
        public int? PartId { get; set; }

        [RegularExpression(@"^\d+$", ErrorMessage = "Shopify Order Id must contain numbers only.")]
        [StringLength(19)]
        public string? ShopifyOrderId { get; set; }

        [StringLength(100)]
        public string? OrderNumber { get; set; }

        [EmailAddress]
        [StringLength(256)]
        public string? CustomerEmail { get; set; }

        [StringLength(500)]
        public string? RequestedPartName { get; set; }

        [Range(1, 999)]
        public int? RequestedQuantity { get; set; }

        [Range(1, int.MaxValue)]
        public int? ReturnReasonId { get; set; }

        [Required]
        [StringLength(500, MinimumLength = 1)]
        public string Reason { get; set; } = string.Empty;

        [StringLength(4000)]
        public string? Notes { get; set; }

        public List<RefundRequestItemAddRequest> Items { get; set; } =
            new List<RefundRequestItemAddRequest>();

        public List<RefundRequestPhotoAddRequest> Photos { get; set; } =
            new List<RefundRequestPhotoAddRequest>();
    }

    public class RefundRequestItemAddRequest
    {
        [Required]
        [Range(1, int.MaxValue)]
        public int PartId { get; set; }

        public long? ShopifyLineItemId { get; set; }

        [Range(1, int.MaxValue)]
        public int Quantity { get; set; } = 1;

        [StringLength(4000)]
        public string? ItemNotes { get; set; }
    }

    public class RefundRequestPhotoAddRequest
    {
        public int? RefundRequestItemId { get; set; }

        [Required]
        [StringLength(500)]
        public string Url { get; set; } = string.Empty;

        [StringLength(260)]
        public string? OriginalFileName { get; set; }

        [StringLength(100)]
        public string? ContentType { get; set; }

        public int SortOrder { get; set; } = 0;
    }
}
