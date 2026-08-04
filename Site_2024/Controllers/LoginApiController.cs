using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Site_2024.Web.Api.Interfaces;
using Site_2024.Web.Api.Models.User;
using Site_2024.Web.Api.Requests;
using Site_2024.Web.Api.Responses;
using Site_2024.Web.Api.Services;
using System.Data.SqlClient;

namespace Site_2024.Web.Api.Controllers
{
    [Route("api/login")]
    [ApiController]
    public class LoginApiController : BaseApiController
    {
        private readonly IUserService _service;
        private readonly IAuthenticationService<IUserAuthData> _authService;

        public LoginApiController(
            IUserService service,
            IAuthenticationService<IUserAuthData> authService,
            ILogger<LoginApiController> logger) : base(logger)
        {
            _service = service;
            _authService = authService;
        }

        [HttpPost("register")]
        [Authorize(Policy = "UserAdmin")]
        public ActionResult<ItemResponse<int>> Register(
            [FromBody] UserRegisterRequest model)
        {
            int code = 201;
            BaseResponse response;

            try
            {
                int id = _service.Create(model);
                response = new ItemResponse<int> { Item = id };
            }
            catch (SqlException ex) when (ex.Number == 2601 || ex.Number == 2627 || ex.Number == 50024 || ex.Number == 50025)
            {
                code = 409;
                Logger.LogWarning(ex, "Duplicate username or email during user creation.");
                response = new ErrorResponse("That username or email is already in use.");
            }
            catch (Exception ex)
            {
                code = 500;
                Logger.LogError(ex, "Unable to create user.");
                response = new ErrorResponse("Unable to create user.");
            }

            return StatusCode(code, response);
        }

        [HttpPost("login")]
        [AllowAnonymous]
        public async Task<ActionResult<ItemResponse<AuthenticatedUser>>> Login(
            [FromBody] UserLoginRequest model)
        {
            int code = 200;
            BaseResponse response;

            try
            {
                string login = !string.IsNullOrWhiteSpace(model.Login)
                    ? model.Login.Trim()
                    : model.Email?.Trim();

                if (string.IsNullOrWhiteSpace(login))
                {
                    code = 400;
                    response = new ErrorResponse("Username is required.");
                    return StatusCode(code, response);
                }

                bool success = await _service.LogInAsync(
                    login,
                    model.Password);

                if (!success)
                {
                    code = 401;
                    response = new ErrorResponse("Invalid username or password.");
                }
                else
                {
                    IUserAuthData currentUser = _authService.GetCurrentUser();

                    response = new ItemResponse<AuthenticatedUser>
                    {
                        Item = ToSafeUser(currentUser)
                    };
                }
            }
            catch (Exception ex)
            {
                code = 500;
                Logger.LogError(ex, "Unable to log in.");
                response = new ErrorResponse("Unable to log in at this time.");
            }

            return StatusCode(code, response);
        }


        [HttpPost("change-password")]
        [Authorize]
        public async Task<ActionResult<SuccessResponse>> ChangePassword(
            [FromBody] UserChangePasswordRequest model)
        {
            int code = 200;
            BaseResponse response;

            try
            {
                IUserAuthData currentUser = _authService.GetCurrentUser();

                if (currentUser == null)
                {
                    return Unauthorized(new ErrorResponse("Not logged in."));
                }

                bool changed = _service.ChangePassword(currentUser.Id, model);

                if (!changed)
                {
                    code = 400;
                    response = new ErrorResponse("The current password is incorrect.");
                }
                else
                {
                    await _authService.LogOutAsync();
                    response = new SuccessResponse();
                }
            }
            catch (Exception ex)
            {
                code = 500;
                Logger.LogError(ex, "Unable to change password.");
                response = new ErrorResponse("Unable to change password at this time.");
            }

            return StatusCode(code, response);
        }

        [HttpGet("me")]
        public ActionResult<ItemResponse<AuthenticatedUser>> GetCurrentUser()
        {
            int code = 200;
            BaseResponse response;

            try
            {
                IUserAuthData user = _authService.GetCurrentUser();

                if (user == null)
                {
                    code = 401;
                    response = new ErrorResponse("Not logged in.");
                }
                else
                {
                    response = new ItemResponse<AuthenticatedUser>
                    {
                        Item = ToSafeUser(user)
                    };
                }
            }
            catch (Exception ex)
            {
                code = 500;
                Logger.LogError(ex, "Unable to read the current user.");
                response = new ErrorResponse(
                    "Unable to read the current user at this time.");
            }

            return StatusCode(code, response);
        }

        [HttpGet("current")]
        [Authorize(Policy = "UserAdmin")]
        public ActionResult<ItemResponse<User>> GetUserByEmail(
            [FromQuery] string email)
        {
            int code = 200;
            BaseResponse response;

            try
            {
                User user = _service.GetUserByEmail(email);

                if (user == null)
                {
                    code = 404;
                    response = new ErrorResponse("Not found.");
                }
                else
                {
                    response = new ItemResponse<User> { Item = user };
                }
            }
            catch (Exception ex)
            {
                code = 500;
                Logger.LogError(ex, "Unable to load the requested user.");
                response = new ErrorResponse(
                    "Unable to load the requested user at this time.");
            }

            return StatusCode(code, response);
        }

        [HttpGet("logout")]
        public async Task<ActionResult<SuccessResponse>> Logout()
        {
            int code = 200;
            BaseResponse response;

            try
            {
                if (_authService.IsLoggedIn())
                {
                    await _authService.LogOutAsync();
                }

                response = new SuccessResponse();
            }
            catch (Exception ex)
            {
                code = 500;
                Logger.LogError(ex, "Unable to log out.");
                response = new ErrorResponse("Unable to log out at this time.");
            }

            return StatusCode(code, response);
        }

        private static AuthenticatedUser ToSafeUser(IUserAuthData user)
        {
            if (user == null)
            {
                return null;
            }

            return new AuthenticatedUser
            {
                Id = user.Id,
                Name = user.Name,
                Username = user.Username,
                Email = user.Email,
                RoleName = user.RoleName,
                MustChangePassword = user.MustChangePassword
            };
        }
    }
}
