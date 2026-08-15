using Site_2024.Web.Api.Models;
using Site_2024.Web.Api.Requests;

namespace Site_2024.Web.Api.Services
{
    public interface IMakeService
    {
        int AddMake(MakeAddRequest make);
        int AddMakeModel(MakeModelAdminCreateRequest model);
        void DeleteMake(int id);
        Make GetMakeById(int id);
        List<Make> GetMakesAll();
        List<Make> GetMakesAllCompanies();
        void UpdateMake(MakeUpdateRequest make);
    }
}