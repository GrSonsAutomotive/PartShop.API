using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Site_2024.Web.Api.Interfaces;
using Site_2024.Web.Api.Models;
using Site_2024.Web.Api.Models.User;
using Site_2024.Web.Api.Requests.ShippingPolicies;
using Site_2024.Web.Api.Responses;
using Site_2024.Web.Api.Services;
using System;
using System.Collections.Generic;

namespace Site_2024.Web.Api.Controllers
{
    [Route("api/shippingpolicies")]
    [ApiController]
    public class ShippingPoliciesApiController : BaseApiController
    {
        private readonly IShippingPoliciesService _service;
        private readonly IAuthenticationService<IUserAuthData> _authService;

        public ShippingPoliciesApiController(IShippingPoliciesService service, IAuthenticationService<IUserAuthData> authService, ILogger<ShippingPoliciesApiController> logger)
            : base(logger)
        {
            _service = service;
            _authService = authService;
        }

        [HttpGet("all")]
        [AllowAnonymous] // or [Authorize] if you want it locked down
        public ActionResult<ItemResponse<List<ShippingPolicy>>> GetAll()
        {
            int code = 200;
            BaseResponse response = null;

            try
            {
                List<ShippingPolicy> list = _service.GetAll();

                if (list == null || list.Count == 0)
                {
                    code = 404;
                    response = new ErrorResponse("No shipping policies found.");
                }
                else
                {
                    response = new ItemResponse<List<ShippingPolicy>> { Item = list };
                }
            }
            catch (Exception ex)
            {
                code = 500;
                base.Logger.LogError(ex.ToString());
                response = new ErrorResponse(ex.Message);
            }

            return StatusCode(code, response);
        }

        [HttpGet("shopify/profiles")]
        [Authorize(Policy = "AdminAction")]
        public async Task<ActionResult<ItemResponse<List<Site_2024.Web.Api.Models.Shopify.ShopifyDeliveryProfileResult>>>> GetShopifyProfiles(
            [FromServices] IShopifyAdminService shopifyAdminService)
        {
            try
            {
                var profiles = await shopifyAdminService.GetDeliveryProfilesAsync();
                return Ok(new ItemResponse<List<Site_2024.Web.Api.Models.Shopify.ShopifyDeliveryProfileResult>>
                {
                    Item = profiles
                });
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "Failed to load Shopify delivery profiles.");
                return StatusCode(500, new ErrorResponse(ex.Message));
            }
        }

        [HttpPut("{id:int}/shopify-profile")]
        [Authorize(Policy = "AdminAction")]
        public ActionResult<BaseResponse> UpdateShopifyProfile(
            int id,
            [FromBody] ShippingPolicyShopifyProfileUpdateRequest model)
        {
            try
            {
                _service.UpdateShopifyProfileId(id, model.ShopifyProfileId);
                return Ok(new SuccessResponse());
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "Failed to map ShippingPolicy {ShippingPolicyId} to Shopify profile.", id);
                return StatusCode(500, new ErrorResponse(ex.Message));
            }
        }

        [HttpPost]
        [Authorize(Policy = "AdminAction")]
        public ActionResult<ItemResponse<int>> Create(ShippingPolicyAddRequest model)
        {
            int code = 201;
            BaseResponse response = null;

            try
            {
                var user = _authService.GetCurrentUser();
                int id = _service.Add(model, user.Id);

                response = new ItemResponse<int> { Item = id };
            }
            catch (Exception ex)
            {
                code = 500;
                base.Logger.LogError(ex.ToString());
                response = new ErrorResponse(ex.Message);
            }

            return StatusCode(code, response);
        }
    }
}

