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
    [Route("api/models")]
    public class ModelApiController : BaseApiController
    {
        private readonly IModelService _service;

        public ModelApiController(IModelService service, ILogger<ModelApiController> logger) : base(logger)
        {
            _service = service;
        }

        [HttpGet("all")]
        public ActionResult<ItemResponse<List<Model>>> GetAll()
        {
            int code = 200;
            BaseResponse response;

            try
            {
                List<Model> list = _service.GetModelsAll();

                if (list == null || list.Count == 0)
                {
                    code = 404;
                    response = new ErrorResponse("No models found.");
                }
                else
                {
                    response = new ItemResponse<List<Model>> { Item = list };
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

        // Drill-down: Models by MakeId
        [HttpGet("make/{makeId:int}")]
        public ActionResult<ItemResponse<List<Model>>> GetByMakeId(int makeId)
        {
            int code = 200;
            BaseResponse response;

            try
            {
                List<Model> list = _service.GetByMakeId(makeId);

                if (list == null || list.Count == 0)
                {
                    code = 404;
                    response = new ErrorResponse("No models found for this make.");
                }
                else
                {
                    response = new ItemResponse<List<Model>> { Item = list };
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
        public ActionResult<ItemResponse<Model>> GetById(int id)
        {
            int code = 200;
            BaseResponse response;

            try
            {
                Model item = _service.GetModelById(id);

                if (item == null)
                {
                    code = 404;
                    response = new ErrorResponse("Not found.");
                }
                else
                {
                    response = new ItemResponse<Model> { Item = item };
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
        public ActionResult<ItemResponse<int>> Add([FromBody] ModelAddRequest model)
        {
            int code = 201;
            BaseResponse response;

            try
            {
                int id = _service.AddModel(model);
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
