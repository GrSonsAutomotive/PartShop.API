using System;
using System.ComponentModel.DataAnnotations;

namespace Site_2024.Web.Api.Requests
{
    public class ContactEmailRequest
    {
        [Required]
        public Guid? ClientSubmissionId { get; set; }
        [Required]
        [StringLength(50)]
        public string InquiryType { get; set; }

        [Required]
        [StringLength(128, MinimumLength = 2)]
        public string Name { get; set; }

        [Required]
        [EmailAddress]
        [StringLength(256)]
        public string Email { get; set; }

        [StringLength(50)]
        public string Phone { get; set; }

        [Required]
        [StringLength(200, MinimumLength = 2)]
        public string Subject { get; set; }

        [Required]
        [StringLength(4000, MinimumLength = 10)]
        public string Message { get; set; }

        [Range(1, int.MaxValue)]
        public int? PartId { get; set; }
    }
}
