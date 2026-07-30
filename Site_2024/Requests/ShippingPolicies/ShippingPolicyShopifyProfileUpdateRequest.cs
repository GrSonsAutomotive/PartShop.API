using System.ComponentModel.DataAnnotations;

namespace Site_2024.Web.Api.Requests.ShippingPolicies
{
    public class ShippingPolicyShopifyProfileUpdateRequest
    {
        [Range(1, long.MaxValue)]
        public long ShopifyProfileId { get; set; }
    }
}
