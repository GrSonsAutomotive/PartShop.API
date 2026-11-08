using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Site_2024.Web.Api.Models;
using Site_2024.Web.Api.Requests;
using Site_2024.Web.Api.Responses;
using Site_2024.Web.Api.Services;
using System;
using System.Collections.Generic;

namespace Site_2024.Web.Api.Controllers
{
    [ApiController]
    [Route("api/catagories")]
    public class CatagoryApiController : BaseApiController
    {
        private readonly ICatagoryService _service;

        public CatagoryApiController(ICatagoryService service, ILogger<CatagoryApiController> logger) : base(logger)
        {
            _service = service;
        }

        [HttpGet("all")]
        public ActionResult<ItemResponse<List<Catagory>>> GetAll()
        {
            int code = 200;
            BaseResponse response;

            try
            {
                List<Catagory> list = _service.GetCatagoryAll();

                if (list == null || list.Count == 0)
                {
                    code = 404;
                    response = new ErrorResponse("No categories found.");
                }
                else
                {
                    response = new ItemResponse<List<Catagory>> { Item = list };
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

        [HttpGet("{id:int}")]
        public ActionResult<ItemResponse<Catagory>> GetById(int id)
        {
            int code = 200;
            BaseResponse response;

            try
            {
                Catagory item = _service.GetCatagoryById(id);

                if (item == null)
                {
                    code = 404;
                    response = new ErrorResponse("Not found.");
                }
                else
                {
                    response = new ItemResponse<Catagory> { Item = item };
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

        [HttpPost]
        [Authorize(Policy = "AdminAction")]
        public ActionResult<ItemResponse<int>> Add([FromBody] CatagoryAddRequest model)
        {
            int code = 201;
            BaseResponse response;

            try
            {
                int id = _service.AddCatagory(model);
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
