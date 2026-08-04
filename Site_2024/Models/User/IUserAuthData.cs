namespace Site_2024.Web.Api.Models.User
{
    public interface IUserAuthData
    {
        int Id { get; set; }
        string Name { get; set; }
        string Username { get; set; }
        string Email { get; set; }
        string PasswordHash { get; set; }
        int RoleId { get; set; }
        string RoleName { get; set; }
        bool IsActive { get; set; }
        bool MustChangePassword { get; set; }
    }
}
