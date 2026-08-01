using Microsoft.AspNetCore.Mvc;
using System.Collections.Generic;

namespace Site_2024.Web.Api.Requests
{
    public class CustomerSearchRequest
    {
        public string? q { get; set; }

        // Legacy spelling used throughout the existing database/service layer.
        // Keep it so current C# code and stored procedure parameter mapping do not
        // need to change.
        public int? CatagoryId { get; set; }

        // Correct public query-string spelling used by the React application:
        // ?categoryId=24
        // Assigning this property also populates the legacy CatagoryId property
        // consumed by PartService.SearchCustomer().
        [FromQuery(Name = "categoryId")]
        public int? CategoryId
        {
            get => CatagoryId;
            set => CatagoryId = value;
        }


        // Multi-select customer filters. ASP.NET Core binds repeated query
        // parameters such as ?categoryIds=12&categoryIds=13 into these lists.
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
    }
}
