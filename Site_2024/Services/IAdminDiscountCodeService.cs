using Site_2024.Web.Api.Constructors;
using Site_2024.Web.Api.Models;
using Site_2024.Web.Api.Requests;

namespace Site_2024.Web.Api.Services
{
    public interface IAdminDiscountCodeService
    {
        int Add(AdminDiscountCodeAddRequest model, int? userId);
        void Deactivate(int id, AdminDiscountCodeDeactivateRequest model, int? userId);
        AdminDiscountCode? GetById(int id);
        PublicSaleBanner? GetActiveSiteBanner(DateTime utcNow);
        List<AdminDiscountCodeRule> GetRulesByDiscountId(int discountId);
        Paged<AdminDiscountCode>? GetPaginated(
            int pageIndex,
            int pageSize,
            AdminDiscountCodeSearchRequest model);
        void MarkShopifyCreated(int id, AdminDiscountCodeShopifyCreatedRequest model);
        void MarkCollectionCreated(int id, string shopifyCollectionGid, string? shopifyCollectionHandle);
        void MarkCollectionSync(int id, string status, string? error = null);
        void MarkError(int id, string adminNotes);
    }
}
