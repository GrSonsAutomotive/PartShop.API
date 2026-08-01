using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Site_2024.Web.Api.Constructors;
using Site_2024.Web.Api.Models;
using Site_2024.Web.Api.Responses;
using Site_2024.Web.Api.Services;

namespace Site_2024.Web.Api.Controllers
{
    [Route("api/discounts")]
    [ApiController]
    [AllowAnonymous]
    public class DiscountsApiController : BaseApiController
    {
        private readonly IAdminDiscountCodeService _discountService;

        public DiscountsApiController(
            IAdminDiscountCodeService discountService,
            ILogger<DiscountsApiController> logger) : base(logger)
        {
            _discountService = discountService;
        }

        [HttpGet("active-banner")]
        public ActionResult<ItemResponse<PublicSaleBanner?>> GetActiveBanner()
        {
            int code = 200;
            BaseResponse response;

            try
            {
                PublicSaleBanner? banner =
                    _discountService.GetActiveSiteBanner(DateTime.UtcNow);

                response = new ItemResponse<PublicSaleBanner?>
                {
                    Item = banner
                };
            }
            catch (Exception ex)
            {
                code = 500;
                response = new ErrorResponse("The active sale banner could not be loaded.");
                Logger.LogError(ex, "Active sale banner lookup failed.");
            }

            return StatusCode(code, response);
        }
    }
}
