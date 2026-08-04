namespace Site_2024.Web.Api.Models.User
{
    public class AuthenticatedUser
    {
        public int Id { get; set; }
        public string Name { get; set; }
        public string Username { get; set; }
        public string Email { get; set; }
        public string RoleName { get; set; }
        public bool MustChangePassword { get; set; }
    }
}
