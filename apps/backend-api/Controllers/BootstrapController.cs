using Microsoft.AspNetCore.Mvc;
using VinhKhanh.BackendApi.Contracts;
using VinhKhanh.BackendApi.Infrastructure;
using VinhKhanh.BackendApi.Models;

namespace VinhKhanh.BackendApi.Controllers;

[ApiController]
[Route("api/v1")]
public sealed class BootstrapController(
    AdminDataRepository repository,
    AdminRequestContextResolver adminRequestContextResolver,
    BootstrapLocalizationService bootstrapLocalizationService,
    ResponseUrlNormalizer responseUrlNormalizer,
    ILogger<BootstrapController> logger) : ControllerBase
{
    [HttpGet("bootstrap")]
    public async Task<ActionResult<ApiResponse<AdminBootstrapResponse>>> GetBootstrap(
        [FromQuery] string? customerUserId,
        [FromQuery] string? languageCode,
        CancellationToken cancellationToken)
    {
        var admin = adminRequestContextResolver.TryGetCurrentAdmin();
        var bootstrap = repository.GetBootstrap(admin, customerUserId);
        if (admin is null)
        {
            bootstrap = await bootstrapLocalizationService.ApplyAutoTranslationsAsync(
                bootstrap,
                languageCode,
                cancellationToken);
        }

        bootstrap = responseUrlNormalizer.Normalize(bootstrap);
        if (bootstrap.SyncState is not null)
        {
            Response.Headers["X-Data-Version"] = bootstrap.SyncState.Version;
        }

        logger.LogDebug(
            "Bootstrap served for adminUserId={AdminUserId}, role={Role}, deprecatedCustomerUserId={CustomerUserId}, languageCode={LanguageCode}, version={Version}",
            admin?.UserId,
            admin?.Role,
            customerUserId,
            languageCode,
            bootstrap.SyncState?.Version);

        return Ok(ApiResponse<AdminBootstrapResponse>.Ok(bootstrap));
    }

    [HttpGet("sync-state")]
    public ActionResult<ApiResponse<DataSyncState>> GetSyncState()
    {
        var syncState = repository.GetSyncState();
        Response.Headers["X-Data-Version"] = syncState.Version;
        return Ok(ApiResponse<DataSyncState>.Ok(syncState));
    }

    [HttpGet("dashboard/summary")]
    public ActionResult<ApiResponse<DashboardSummaryResponse>> GetDashboardSummary()
        => Ok(ApiResponse<DashboardSummaryResponse>.Ok(
            repository.GetDashboardSummary(adminRequestContextResolver.RequireAuthenticatedAdmin())));

    [HttpGet("categories")]
    public ActionResult<ApiResponse<IReadOnlyList<PoiCategory>>> GetCategories()
        => Ok(ApiResponse<IReadOnlyList<PoiCategory>>.Ok(repository.GetCategories()));

    [HttpGet("customer-users")]
    public ActionResult<ApiResponse<string>> GetCustomerUsers()
        => StatusCode(StatusCodes.Status410Gone, ApiResponse<string>.Fail("Customer account management has been deprecated from the public Android app."));

    [HttpGet("analytics/usage-events")]
    public ActionResult<ApiResponse<IReadOnlyList<AppUsageEvent>>> GetUsageEvents()
        => Ok(ApiResponse<IReadOnlyList<AppUsageEvent>>.Ok(
            repository.GetAppUsageEvents(adminRequestContextResolver.RequireAuthenticatedAdmin())));

    [HttpGet("analytics/view-logs")]
    public ActionResult<ApiResponse<IReadOnlyList<ViewLog>>> GetViewLogs()
        => Ok(ApiResponse<IReadOnlyList<ViewLog>>.Ok(
            repository.GetViewLogs(adminRequestContextResolver.RequireAuthenticatedAdmin())));

    [HttpGet("analytics/audio-listen-logs")]
    public ActionResult<ApiResponse<IReadOnlyList<AudioListenLog>>> GetAudioListenLogs()
        => Ok(ApiResponse<IReadOnlyList<AudioListenLog>>.Ok(
            repository.GetAudioListenLogs(adminRequestContextResolver.RequireAuthenticatedAdmin())));
}
