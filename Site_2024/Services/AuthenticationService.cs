using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Site_2024.Web.Api.Interfaces;
using Site_2024.Web.Api.Models.User;
using System.Security.Claims;

namespace Site_2024.Web.Api.Services
{
    public class AuthenticationService : IAuthenticationService<IUserAuthData>
    {
        private const string UsernameClaim = "site_username";
        private const string IsActiveClaim = "site_is_active";
        private const string MustChangePasswordClaim = "site_must_change_password";

        private readonly IHttpContextAccessor _httpContextAccessor;

        public AuthenticationService(IHttpContextAccessor httpContextAccessor)
        {
            _httpContextAccessor = httpContextAccessor;
        }

        public async Task LogInAsync(IUserAuthData user)
        {
            var claims = new List<Claim>
            {
                new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
                new Claim(ClaimTypes.Name, user.Name ?? string.Empty),
                new Claim(ClaimTypes.Email, user.Email ?? string.Empty),
                new Claim(UsernameClaim, user.Username ?? string.Empty),
                new Claim(IsActiveClaim, user.IsActive ? "true" : "false"),
                new Claim(MustChangePasswordClaim, user.MustChangePassword ? "true" : "false")
            };

            if (!string.IsNullOrWhiteSpace(user.RoleName))
            {
                claims.Add(new Claim(ClaimTypes.Role, CanonicalizeRole(user.RoleName)));
            }

            var identity = new ClaimsIdentity(
                claims,
                CookieAuthenticationDefaults.AuthenticationScheme);

            var principal = new ClaimsPrincipal(identity);

            var props = new AuthenticationProperties
            {
                IsPersistent = true,
                IssuedUtc = DateTime.UtcNow,
                ExpiresUtc = DateTime.UtcNow.AddHours(1),
                AllowRefresh = true
            };

            HttpContext httpContext = _httpContextAccessor.HttpContext!;

            await httpContext.SignInAsync(
                CookieAuthenticationDefaults.AuthenticationScheme,
                principal,
                props);

            httpContext.User = principal;
        }

        public async Task LogOutAsync()
        {
            await _httpContextAccessor.HttpContext!.SignOutAsync(
                CookieAuthenticationDefaults.AuthenticationScheme);
        }

        public bool IsLoggedIn()
        {
            return _httpContextAccessor.HttpContext?.User?.Identity?.IsAuthenticated == true;
        }

        public IUserAuthData GetCurrentUser()
        {
            var context = _httpContextAccessor.HttpContext;

            if (context?.User?.Identity?.IsAuthenticated != true)
            {
                return null;
            }

            string idClaim = context.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            string nameClaim = context.User.FindFirst(ClaimTypes.Name)?.Value;
            string usernameClaim = context.User.FindFirst(UsernameClaim)?.Value;
            string emailClaim = context.User.FindFirst(ClaimTypes.Email)?.Value;
            string roleClaim = context.User.FindFirst(ClaimTypes.Role)?.Value;
            string activeClaim = context.User.FindFirst(IsActiveClaim)?.Value;
            string mustChangeClaim = context.User.FindFirst(MustChangePasswordClaim)?.Value;

            if (!int.TryParse(idClaim, out int userId))
            {
                return null;
            }

            return new UserAuthData
            {
                Id = userId,
                Name = nameClaim,
                Username = usernameClaim,
                Email = emailClaim,
                RoleName = roleClaim,
                IsActive = string.Equals(activeClaim, "true", StringComparison.OrdinalIgnoreCase),
                MustChangePassword = string.Equals(mustChangeClaim, "true", StringComparison.OrdinalIgnoreCase)
            };
        }

        private static string CanonicalizeRole(string roleName)
        {
            return roleName switch
            {
                "Admin Low" => "AdminLow",
                "Admin High" => "AdminHigh",
                _ => roleName.Replace(" ", "")
            };
        }
    }
}
