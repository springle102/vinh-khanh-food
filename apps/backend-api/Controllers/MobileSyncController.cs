using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using VinhKhanh.BackendApi.Contracts;
using VinhKhanh.BackendApi.Infrastructure;
using VinhKhanh.Core.Mobile;

namespace VinhKhanh.BackendApi.Controllers;

[ApiController]
[Route("api/mobile")]
public sealed class MobileSyncController(
    AdminDataRepository repository,
    MobileOfflinePackageBuilder mobileOfflinePackageBuilder,
    ILogger<MobileSyncController> logger) : ControllerBase
{
    [HttpGet("bootstrap-package-version")]
    public ActionResult<ApiResponse<MobileDatasetVersionResponse>> GetBootstrapPackageVersion()
    {
        var syncState = repository.GetSyncState();
        var imageUrls = repository.GetMediaAssets()
            .Where(item => string.Equals(item.Type, "image", StringComparison.OrdinalIgnoreCase))
            .Select(item => item.Url)
            .Concat(repository.GetFoodItems().Select(item => item.ImageUrl))
            .Concat(repository.GetRoutes().Select(item => item.CoverImageUrl))
            .Where(item => !string.IsNullOrWhiteSpace(item))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var response = new MobileDatasetVersionResponse(
            syncState.Version,
            syncState.GeneratedAt,
            syncState.LastChangedAt,
            repository.GetPois().Count(item => string.Equals(item.Status, "published", StringComparison.OrdinalIgnoreCase)),
            repository.GetRoutes().Count(item => item.IsActive),
            repository.GetAudioGuides().Count(AudioGuideCatalog.IsReadyForPlayback),
            imageUrls.Count);

        Response.Headers["X-Data-Version"] = syncState.Version;
        return Ok(ApiResponse<MobileDatasetVersionResponse>.Ok(response));
    }

    [HttpGet("offline-package")]
    public async Task<ActionResult<ApiResponse<AdminBootstrapResponse>>> GetOfflinePackage(
        [FromQuery] string? languageCode,
        CancellationToken cancellationToken)
    {
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        var offlinePackage = await mobileOfflinePackageBuilder.BuildAsync(languageCode, cancellationToken);
        Response.Headers["X-Data-Version"] = offlinePackage.SyncState?.Version ?? string.Empty;
        Response.Headers["X-Offline-Package-Language"] = PremiumAccessCatalog.NormalizeLanguageCode(languageCode);
        Response.Headers["X-Offline-Package-Translations"] = offlinePackage.Translations.Count.ToString();
        Response.Headers["X-Offline-Package-AudioGuides"] = offlinePackage.AudioGuides.Count.ToString();

        logger.LogInformation(
            "[OfflinePackage] Responded with mobile offline package. requestedLanguage={RequestedLanguage}; version={Version}; translations={TranslationCount}; audioGuides={AudioGuideCount}; elapsedMs={ElapsedMs}",
            languageCode,
            offlinePackage.SyncState?.Version,
            offlinePackage.Translations.Count,
            offlinePackage.AudioGuides.Count,
            stopwatch.Elapsed.TotalMilliseconds.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture));

        return Ok(ApiResponse<AdminBootstrapResponse>.Ok(offlinePackage));
    }

    [HttpPost("sync/logs")]
    public ActionResult<ApiResponse<MobileUsageEventSyncResponse>> SyncLogs(
        [FromBody] MobileUsageEventSyncRequest request)
    {
        logger.LogInformation(
            "[AnalyticsApi] Received mobile usage event sync batch. eventCount={EventCount}",
            request.Events.Count);
        if (request.Events.Count == 0)
        {
            return Ok(ApiResponse<MobileUsageEventSyncResponse>.Ok(
                new MobileUsageEventSyncResponse(0, 0, [])));
        }

        var results = new List<MobileUsageEventSyncResult>();
        foreach (var item in request.Events.Take(200))
        {
            try
            {
                var normalizedEventType = MobileUsageEventTypes.Normalize(item.EventType);
                if (string.IsNullOrWhiteSpace(item.IdempotencyKey) ||
                    string.IsNullOrWhiteSpace(normalizedEventType))
                {
                    results.Add(new MobileUsageEventSyncResult(
                        item.IdempotencyKey,
                        Accepted: false,
                        ServerEventId: null,
                        ErrorMessage: "invalid_event"));
                    continue;
                }

                var saved = repository.TrackAppUsageEvent(new AppUsageEventCreateRequest(
                    normalizedEventType,
                    item.PoiId,
                    item.LanguageCode,
                    item.Platform,
                    item.SessionId,
                    item.Source,
                    item.Metadata,
                    item.DurationInSeconds,
                    item.OccurredAt,
                    item.IdempotencyKey));

                results.Add(new MobileUsageEventSyncResult(
                    item.IdempotencyKey,
                    Accepted: true,
                    ServerEventId: saved.Id,
                    ErrorMessage: null));
            }
            catch (Exception exception)
            {
                results.Add(new MobileUsageEventSyncResult(
                    item.IdempotencyKey,
                    Accepted: false,
                    ServerEventId: null,
                    ErrorMessage: exception.Message));
            }
        }

        var acceptedCount = results.Count(item => item.Accepted);
        var response = new MobileUsageEventSyncResponse(
            acceptedCount,
            results.Count - acceptedCount,
            results);
        logger.LogInformation(
            "[AnalyticsApi] Processed mobile usage event sync batch. accepted={AcceptedCount}; rejected={RejectedCount}",
            response.AcceptedCount,
            response.RejectedCount);

        return Ok(ApiResponse<MobileUsageEventSyncResponse>.Ok(response));
    }
}
