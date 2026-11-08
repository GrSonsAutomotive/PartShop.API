using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Site_2024.Web.Api.Services;

namespace Site_2024.Web.Api.Controllers;

[ApiController]
[Authorize(Policy = "ReferenceDataDelete")]
public sealed class ReferenceDeletionController : ControllerBase
{
    private readonly ReferenceDeletionService service;
    public ReferenceDeletionController(ReferenceDeletionService service) => this.service = service;

    [HttpGet("api/catagories/{id:int}/delete-check")]
    public IActionResult CategoryPreview(int id) => Run("Category", id, null, false);
    [HttpDelete("api/catagories/{id:int}")]
    public IActionResult CategoryDelete(int id) => Run("Category", id, null, true);
    [HttpGet("api/makes/{id:int}/delete-check")]
    public IActionResult MakePreview(int id) => Run("Make", id, null, false);
    [HttpDelete("api/makes/{id:int}")]
    public IActionResult MakeDelete(int id) => Run("Make", id, null, true);
    [HttpGet("api/models/{id:int}/delete-check")]
    public IActionResult ModelPreview(int id) => Run("Model", id, null, false);
    [HttpDelete("api/models/{id:int}")]
    public IActionResult ModelDelete(int id) => Run("Model", id, null, true);
    [HttpGet("api/makes/manufacturer/delete-check")]
    public IActionResult ManufacturerPreview([FromQuery] string company) => Run("Manufacturer", null, company, false);
    [HttpDelete("api/makes/manufacturer")]
    public IActionResult ManufacturerDelete([FromQuery] string company) => Run("Manufacturer", null, company, true);

    private IActionResult Run(string kind, int? id, string? company, bool delete)
    {
        if (id <= 0 || (kind == "Manufacturer" && (string.IsNullOrWhiteSpace(company) || company.Length > 128)))
            return BadRequest(new { errors = new[] { "A valid reference identifier is required." } });
        ReferenceDeletionResult result;
        try
        {
            result = service.Execute(kind, id, company?.Trim(), delete);
        }
        catch (ReferenceDeletionBusyException exception)
        {
            Response.Headers["Retry-After"] = "1";
            return StatusCode(StatusCodes.Status503ServiceUnavailable, new
            {
                errors = new[] { exception.Message }, code = "ReferenceDataBusy", retryable = true
            });
        }
        if (!result.Exists) return NotFound(new { errors = new[] { "Reference record not found." }, item = result });
        if (delete && !result.Deleted)
            return Conflict(new { errors = new[] { "Deletion blocked by existing references. No records were deleted." }, item = result });
        return Ok(new { item = result, isSuccessful = true });
    }
}
