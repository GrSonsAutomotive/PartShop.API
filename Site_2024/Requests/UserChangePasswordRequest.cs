using System.ComponentModel.DataAnnotations;

namespace Site_2024.Web.Api.Requests
{
    public class UserChangePasswordRequest
    {
        [Required]
        [DataType(DataType.Password)]
        [MaxLength(100)]
        public string CurrentPassword { get; set; }

        [Required]
        [DataType(DataType.Password)]
        [RegularExpression("^(?=.*[a-z])(?=.*[A-Z])(?=.*\\d)(?=.*[@$!%*?&])[A-Za-z\\d@$!%*?&]{8,}$",
            ErrorMessage = "Password must contain at least 8 characters with an uppercase letter, lowercase letter, a number, and a symbol.")]
        [MaxLength(100)]
        public string NewPassword { get; set; }

        [Required]
        [Compare("NewPassword", ErrorMessage = "Passwords do not match.")]
        public string ConfirmNewPassword { get; set; }
    }
}
