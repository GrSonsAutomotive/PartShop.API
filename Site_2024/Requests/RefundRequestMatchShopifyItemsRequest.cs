using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace Site_2024.Models.Requests.RefundRequests
{
    public class RefundRequestMatchShopifyItemsRequest
    {
        [Required]
        [MinLength(1)]
        public List<RefundRequestShopifyItemSelectionRequest> Items
            { get; set; } =
                new List<RefundRequestShopifyItemSelectionRequest>();
    }

    public class RefundRequestShopifyItemSelectionRequest
    {
        [Required]
        [RegularExpression(
            @"^\d+$",
            ErrorMessage =
                "Shopify Line Item Id must contain numbers only.")]
        [StringLength(19)]
        public string ShopifyLineItemId { get; set; } =
            string.Empty;

        [Range(1, 999)]
        public int Quantity { get; set; } = 1;
    }
}
