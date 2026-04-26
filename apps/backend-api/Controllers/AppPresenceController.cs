using Microsoft.AspNetCore.Mvc;
using VinhKhanh.BackendApi.Contracts;
using VinhKhanh.BackendApi.Infrastructure;

namespace VinhKhanh.BackendApi.Controllers;

[ApiController]
[Route("api/app-presence")]
public sealed class AppPresenceController(
    AdminDataRepository repository,
    ILogger<AppPresenceController> logger) : ControllerBase
{
    [HttpPost("heartbeat")]
    public ActionResult<ApiResponse<AppPresenceResponse>> Heartbeat([FromBody] AppPresenceHeartbeatRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.ClientId))
        {
            return BadRequest(ApiResponse<AppPresenceResponse>.Fail("ClientId is required."));
        }

        try
        {
            var presence = repository.UpsertAppPresence(request);
            logger.LogDebug(
                "[Presence] Heartbeat accepted. clientId={ClientId}; platform={Platform}; appVersion={AppVersion}",
                presence.ClientId,
                presence.Platform,
                presence.AppVersion);

            return Ok(ApiResponse<AppPresenceResponse>.Ok(
                new AppPresenceResponse(presence.ClientId, presence.LastSeenAtUtc, IsOnline: true)));
        }
        catch (ArgumentException exception)
        {
            return BadRequest(ApiResponse<AppPresenceResponse>.Fail(exception.Message));
        }
    }

    [HttpPost("offline")]
    public ActionResult<ApiResponse<AppPresenceResponse>> Offline([FromBody] AppPresenceHeartbeatRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.ClientId))
        {
            return BadRequest(ApiResponse<AppPresenceResponse>.Fail("ClientId is required."));
        }

        try
        {
            var presence = repository.MarkAppPresenceOffline(request);
            return Ok(ApiResponse<AppPresenceResponse>.Ok(
                new AppPresenceResponse(presence.ClientId, presence.LastSeenAtUtc, IsOnline: false)));
        }
        catch (ArgumentException exception)
        {
            return BadRequest(ApiResponse<AppPresenceResponse>.Fail(exception.Message));
        }
    }
}
