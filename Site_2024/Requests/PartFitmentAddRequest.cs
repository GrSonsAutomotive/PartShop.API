using System.ComponentModel.DataAnnotations;

namespace Site_2024.Web.Api.Requests
{
    public class PartFitmentAddRequest
    {
        [Range(1, int.MaxValue)]
        public int MakeId { get; set; }

        [Range(1900, 3000)]
        public int? YearStart { get; set; }

        [Range(1900, 3000)]
        public int? YearEnd { get; set; }
    }
}
