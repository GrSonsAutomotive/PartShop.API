using System.ComponentModel.DataAnnotations;

namespace Site_2024.Web.Api.Requests
{
    public class PartAddRequest
    {
        [Required, StringLength(128, MinimumLength = 2)]
        public string Name { get; set; }

        [Range(1, int.MaxValue)]
        public int CatagoryId { get; set; } // legacy / primary

        [Range(1, int.MaxValue)]
        public int MakeId { get; set; } // legacy / primary

        [Range(1, int.MaxValue)]
        public int ShippingPolicyId { get; set; }

        [StringLength(50)]
        public string? Year { get; set; } // legacy / optional

        [Required, StringLength(128, MinimumLength = 2)]
        public string PartNumber { get; set; }

        [StringLength(128)]
        public string? Brand { get; set; }

        [Required, StringLength(4000, MinimumLength = 2)]
        public string Description { get; set; }

        [Range(typeof(decimal), "0.01", "99999999.99")]
        public decimal Price { get; set; }

        [Range(0, int.MaxValue)]
        public int Quantity { get; set; } = 1;

        [Range(1, int.MaxValue)]
        public int LocationId { get; set; }

        public string? Image { get; set; }
        public int AvailableId { get; set; }
        public int UserId { get; set; }
        public string? OtherBox { get; set; }
        [StringLength(2000)]
        public string? AdminNotes { get; set; }
        public int? ConditionId { get; set; }

        // NEW
        public List<PartCategoryAddRequest> Categories { get; set; } = new();
        public List<PartFitmentAddRequest> Fitments { get; set; } = new();
    }
}
