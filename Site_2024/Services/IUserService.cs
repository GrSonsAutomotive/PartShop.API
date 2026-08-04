using Site_2024.Web.Api.Models.User;
using Site_2024.Web.Api.Requests;

namespace Site_2024.Web.Api.Services
{
    public interface IUserService
    {
        int Create(UserRegisterRequest model);
        Task<bool> LogInAsync(string login, string password);
        int GetUserIdByEmail(string email);
        User GetUserByEmail(string email);
        bool ChangePassword(int userId, UserChangePasswordRequest model);
    }
}
