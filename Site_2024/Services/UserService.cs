using Site_2024.Web.Api.Constructors;
using Site_2024.Web.Api.Interfaces;
using System.Data;
using System.Data.SqlClient;
using Site_2024.Web.Api.Extensions;
using Site_2024.Web.Api.Requests;
using Site_2024.Web.Api.Models.User;

namespace Site_2024.Web.Api.Services
{
    public class UserService : IUserService
    {
        private readonly IAuthenticationService<IUserAuthData> _authenticationService;
        private readonly IDataProvider _dataProvider;

        public UserService(
            IAuthenticationService<IUserAuthData> authenticationService,
            IDataProvider dataProvider)
        {
            _authenticationService = authenticationService;
            _dataProvider = dataProvider;
        }

        public int Create(UserRegisterRequest model)
        {
            int userId = 0;
            string hashedPassword = BCrypt.Net.BCrypt.HashPassword(model.Password);
            const string procName = "[dbo].[User_Insert]";

            _dataProvider.ExecuteCmd(
                procName,
                inputParamMapper: col =>
                {
                    col.AddWithValue("@Username", model.Username.Trim());
                    col.AddWithValue("@Name", model.Name.Trim());
                    col.AddWithValue("@Email", model.Email.Trim());
                    col.AddWithValue("@PasswordHash", hashedPassword);
                    col.AddWithValue("@RoleId", model.RoleId);
                },
                singleRecordMapper: (reader, set) =>
                {
                    userId = reader.GetSafeInt32(0);
                });

            return userId;
        }

        public async Task<bool> LogInAsync(string login, string password)
        {
            const string procName = "[dbo].[User_GetByLogin]";
            IUserAuthData user = null;

            _dataProvider.ExecuteCmd(
                procName,
                inputParamMapper: paramCollection =>
                {
                    paramCollection.AddWithValue("@Login", login.Trim());
                },
                singleRecordMapper: (reader, set) =>
                {
                    user = new UserAuthData
                    {
                        Id = reader.GetSafeInt32(0),
                        Name = reader.GetSafeString(1),
                        Username = reader.GetSafeString(2),
                        Email = reader.GetSafeString(3),
                        PasswordHash = reader.GetSafeString(4),
                        RoleId = reader.GetSafeInt32(6),
                        RoleName = reader.GetSafeString(7),
                        IsActive = reader.GetSafeBool(8),
                        MustChangePassword = reader.GetSafeBool(9)
                    };
                });

            if (user == null ||
                !user.IsActive ||
                string.IsNullOrWhiteSpace(user.PasswordHash) ||
                !BCrypt.Net.BCrypt.Verify(password, user.PasswordHash))
            {
                return false;
            }

            await _authenticationService.LogInAsync(user);
            return true;
        }


        public bool ChangePassword(int userId, UserChangePasswordRequest model)
        {
            const string getProcName = "[dbo].[User_GetPasswordById]";
            string currentHash = null;

            _dataProvider.ExecuteCmd(
                getProcName,
                inputParamMapper: col =>
                {
                    col.AddWithValue("@UserId", userId);
                },
                singleRecordMapper: (reader, set) =>
                {
                    currentHash = reader.GetSafeString(0);
                });

            if (string.IsNullOrWhiteSpace(currentHash) ||
                !BCrypt.Net.BCrypt.Verify(model.CurrentPassword, currentHash))
            {
                return false;
            }

            string newHash = BCrypt.Net.BCrypt.HashPassword(model.NewPassword);
            const string updateProcName = "[dbo].[User_UpdatePassword]";

            _dataProvider.ExecuteNonQuery(
                updateProcName,
                inputParamMapper: col =>
                {
                    col.AddWithValue("@UserId", userId);
                    col.AddWithValue("@PasswordHash", newHash);
                });

            return true;
        }

        public int GetUserIdByEmail(string email)
        {
            int userId = 0;
            const string procName = "[dbo].[User_GetByEmail]";

            _dataProvider.ExecuteCmd(
                procName,
                inputParamMapper: paramCollection =>
                {
                    paramCollection.AddWithValue("@Email", email);
                },
                singleRecordMapper: (reader, set) =>
                {
                    userId = reader.GetInt32(0);
                });

            return userId;
        }

        public User GetUserByEmail(string email)
        {
            const string procName = "[dbo].[User_GetByEmailCookie]";
            User user = null;

            _dataProvider.ExecuteCmd(
                procName,
                inputParamMapper: col =>
                {
                    col.AddWithValue("@Email", email);
                },
                singleRecordMapper: (reader, set) =>
                {
                    int startingIndex = 0;
                    user = MapSingleUser(reader, ref startingIndex);
                });

            return user;
        }

        private static User MapSingleUser(IDataReader reader, ref int startingIndex)
        {
            var user = new User
            {
                Role = new Role()
            };

            user.Id = reader.GetSafeInt32(startingIndex++);
            user.Name = reader.GetSafeString(startingIndex++);
            user.Email = reader.GetSafeString(startingIndex++);
            user.DateCreated = reader.GetSafeDateTime(startingIndex++);
            user.Role.Id = reader.GetSafeInt32(startingIndex++);
            user.Role.Name = reader.GetSafeString(startingIndex++);

            return user;
        }
    }
}
