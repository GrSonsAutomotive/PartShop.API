using System.ComponentModel.DataAnnotations;

namespace Site_2024.Web.Api.Requests
{
    public class MakeModelAdminCreateRequest
    {
        [Required]
        [StringLength(128, MinimumLength = 2)]
        public string Company { get; set; }

        [Required]
        [StringLength(128, MinimumLength = 1)]
        public string ModelName { get; set; }
    }
}
