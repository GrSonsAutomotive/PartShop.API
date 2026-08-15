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
    [Route("api/makes")]
    public class MakesApiController : BaseApiController
    {
        private readonly IMakeService _service;

        public MakesApiController(IMakeService service, ILogger<MakesApiController> logger) : base(logger)
        {
            _service = service;
        }

        [HttpGet("all")]
        public ActionResult<ItemResponse<List<Make>>> GetAll()
        {
            int code = 200;
            BaseResponse response;

            try
            {
                List<Make> list = _service.GetMakesAll();

                if (list == null || list.Count == 0)
                {
                    code = 404;
                    response = new ErrorResponse("No makes found.");
                }
                else
                {
                    response = new ItemResponse<List<Make>> { Item = list };
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

        [HttpGet("companies/all")]
        public ActionResult<ItemResponse<List<Make>>> GetAllCompanies()
        {
            int code = 200;
            BaseResponse response;

            try
            {
                List<Make> list = _service.GetMakesAllCompanies();

                if (list == null || list.Count == 0)
                {
                    code = 404;
                    response = new ErrorResponse("No makes found.");
                }
                else
                {
                    response = new ItemResponse<List<Make>> { Item = list };
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
        public ActionResult<ItemResponse<Make>> GetById(int id)
        {
            int code = 200;
            BaseResponse response;

            try
            {
                Make item = _service.GetMakeById(id);

                if (item == null)
                {
                    code = 404;
                    response = new ErrorResponse("Not found.");
                }
                else
                {
                    response = new ItemResponse<Make> { Item = item };
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
        public ActionResult<ItemResponse<int>> Add([FromBody] MakeAddRequest model)
        {
            int code = 201;
            BaseResponse response;

            try
            {
                int id = _service.AddMake(model);
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

        [HttpPost("make-model")]
        [Authorize(Policy = "AdminAction")]
        public ActionResult<ItemResponse<int>> AddMakeModel([FromBody] MakeModelAdminCreateRequest model)
        {
            int code = 201;
            BaseResponse response;

            try
            {
                if (model == null)
                {
                    return BadRequest(new ErrorResponse("Make/model payload is required."));
                }

                model.Company = model.Company?.Trim();
                model.ModelName = model.ModelName?.Trim();

                if (string.IsNullOrWhiteSpace(model.Company) || string.IsNullOrWhiteSpace(model.ModelName))
                {
                    return BadRequest(new ErrorResponse("Make and model are required."));
                }

                int id = _service.AddMakeModel(model);
                response = new ItemResponse<int> { Item = id };
            }
            catch (Exception ex)
            {
                code = 400;
                base.Logger.LogError(ex, "Failed to create make/model combination.");
                response = new ErrorResponse(ex.Message);
            }

            return StatusCode(code, response);
        }

        [HttpDelete("{id:int}")]
        [Authorize(Policy = "AdminAction")]
        public ActionResult<SuccessResponse> Delete(int id)
        {
            int code = 200;
            BaseResponse response;

            try
            {
                _service.DeleteMake(id);
                response = new SuccessResponse();
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