using Microsoft.AspNetCore.Mvc;
using System.Collections.Generic;

namespace Site_2024.Web.Api.Requests
{
    public class PartSearchRequest
    {
        public string? q { get; set; }

        // Preserve the existing database spelling while accepting the corrected
        // public query-string name used by React.
        public int? CatagoryId { get; set; }

        [FromQuery(Name = "categoryId")]
        public int? CategoryId
        {
            get => CatagoryId;
            set => CatagoryId = value;
        }

        // Repeated query parameters bind into these lists:
        // ?categoryIds=12&categoryIds=13
        // ?conditionIds=1&conditionIds=2
        [FromQuery(Name = "categoryIds")]
        public List<int> CategoryIds { get; set; } = new();

        [FromQuery(Name = "conditionIds")]
        public List<int> ConditionIds { get; set; } = new();

        public int? MakeId { get; set; }
        public int? ModelId { get; set; }
        public string? Year { get; set; }
        public int? ConditionId { get; set; }
        public int? AvailableId { get; set; }
        public decimal? PriceMin { get; set; }
        public decimal? PriceMax { get; set; }
        public int? SiteId { get; set; }
        public int? BoxId { get; set; }
    }
}
