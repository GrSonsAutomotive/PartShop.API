namespace Site_2024.Web.Api.Models
{
    public class PartFitment
    {
        public int Id { get; set; }
        public int PartId { get; set; }
        public int MakeId { get; set; }
        public string Company { get; set; }
        public int ModelId { get; set; }
        public string ModelName { get; set; }
        public int? YearStart { get; set; }
        public int? YearEnd { get; set; }
    }
}
