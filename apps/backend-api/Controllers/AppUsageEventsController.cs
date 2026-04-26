using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using VinhKhanh.BackendApi.Contracts;
using VinhKhanh.BackendApi.Infrastructure;
using VinhKhanh.BackendApi.Models;

namespace VinhKhanh.BackendApi.Controllers;

[ApiController]
[Route("api/v1/app-usage-events")]
public sealed class AppUsageEventsController(
    AdminDataRepository repository,
    ILogger<AppUsageEventsController> logger) : ControllerBase
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
            logger.LogInformation(
                "[AnalyticsApi] Received app usage event. type={EventType}; poiId={PoiId}; language={LanguageCode}; source={Source}; key={IdempotencyKey}",
                request.EventType,
                request.PoiId ?? "none",
                request.LanguageCode ?? "none",
                request.Source ?? "none",
                request.IdempotencyKey ?? "none");
            var saved = repository.TrackAppUsageEvent(request);
            return Ok(ApiResponse<AppUsageEvent>.Ok(saved));
        }
        catch (ArgumentException exception)
        {
            return BadRequest(ApiResponse<AppUsageEvent>.Fail(exception.Message));
        }
    }
}
