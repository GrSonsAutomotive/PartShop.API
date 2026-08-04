using System.ComponentModel.DataAnnotations;

namespace Site_2024.Web.Api.Requests
{
    public class UserLoginRequest
    {
        // New clients send Login. Email remains temporarily supported so the
        // API can be deployed before the frontend without breaking sign-in.
        [MinLength(3)]
        [MaxLength(256)]
        public string? Login { get; set; }

        [MaxLength(256)]
        public string? Email { get; set; }

        [Required]
        [MaxLength(100)]
        public string Password { get; set; }
    }
}
