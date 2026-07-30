using Site_2024.Web.Api.Models;
using Site_2024.Web.Api.Requests.ShippingPolicies;

namespace Site_2024.Web.Api.Services
{
    public interface IShippingPoliciesService
    {
        int Add(ShippingPolicyAddRequest model, int userId);
        List<ShippingPolicy> GetAll();
        void UpdateShopifyProfileId(int id, long shopifyProfileId);
    }
}