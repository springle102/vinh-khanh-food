using Microsoft.AspNetCore.Mvc;
using VinhKhanh.BackendApi.Contracts;
using VinhKhanh.BackendApi.Infrastructure;
using VinhKhanh.BackendApi.Models;

namespace VinhKhanh.BackendApi.Controllers;

[ApiController]
[Route("api/v1/app-usage-events")]
public sealed class AppUsageEventsController(AdminDataRepository repository) : ControllerBase
{
    [HttpPost]
    public ActionResult<ApiResponse<AppUsageEvent>> Create([FromBody] AppUsageEventCreateRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.EventType))
        {
            return BadRequest(ApiResponse<AppUsageEvent>.Fail("EventType is required."));
        }

        try
        {
            var saved = repository.TrackAppUsageEvent(request);
            return Ok(ApiResponse<AppUsageEvent>.Ok(saved));
        }
        catch (ArgumentException exception)
        {
            return BadRequest(ApiResponse<AppUsageEvent>.Fail(exception.Message));
        }
    }
}
