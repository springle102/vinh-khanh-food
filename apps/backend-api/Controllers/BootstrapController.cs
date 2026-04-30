using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;
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
    IMemoryCache memoryCache,
    ILogger<BootstrapController> logger) : ControllerBase
{
    private static readonly TimeSpan PublicBootstrapCacheTtl = TimeSpan.FromSeconds(45);

    [HttpGet("bootstrap")]
    [HttpGet("/api/mobile/bootstrap")]
    public async Task<ActionResult<ApiResponse<AdminBootstrapResponse>>> GetBootstrap(
        [FromQuery] string? languageCode,
        [FromQuery] string? scope,
        CancellationToken cancellationToken)
    {
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        var admin = adminRequestContextResolver.TryGetCurrentAdmin();
        var syncState = repository.GetSyncState();
        var normalizedScope = NormalizeBootstrapScope(scope);
        var normalizedLanguageCode = PremiumAccessCatalog.NormalizeLanguageCode(languageCode);
        if (string.IsNullOrWhiteSpace(normalizedLanguageCode))
        {
            normalizedLanguageCode = "vi";
        }

        var publicCacheKey = admin is null
            ? $"public-bootstrap:{normalizedScope}:{normalizedLanguageCode}:{syncState.Version}"
            : string.Empty;
        if (admin is null &&
            memoryCache.TryGetValue(publicCacheKey, out AdminBootstrapResponse? cachedBootstrap) &&
            cachedBootstrap is not null)
        {
            var cachedNormalized = responseUrlNormalizer.Normalize(cachedBootstrap);
            Response.Headers["X-Data-Version"] = cachedNormalized.SyncState?.Version ?? syncState.Version;
            Response.Headers["X-Bootstrap-Scope"] = normalizedScope;
            Response.Headers["X-Bootstrap-Cache"] = "hit";
            logger.LogInformation(
                "[BootstrapPerf] cache=hit; scope={Scope}; languageCode={LanguageCode}; version={Version}; elapsedMs={ElapsedMs}",
                normalizedScope,
                normalizedLanguageCode,
                syncState.Version,
                stopwatch.Elapsed.TotalMilliseconds.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture));
            return Ok(ApiResponse<AdminBootstrapResponse>.Ok(cachedNormalized));
        }

        var bootstrap = repository.GetBootstrap(admin);
        bootstrap = bootstrap with { SyncState = bootstrap.SyncState ?? syncState };
        if (admin is null && IsMapBootstrapScope(normalizedScope))
        {
            bootstrap = TrimForPublicMapBootstrap(bootstrap);
        }

        if (admin is null)
        {
            bootstrap = await bootstrapLocalizationService.ApplyAutoTranslationsAsync(
                bootstrap,
                normalizedLanguageCode,
                cancellationToken);
        }

        if (admin is null)
        {
            memoryCache.Set(
                publicCacheKey,
                bootstrap,
                new MemoryCacheEntryOptions
                {
                    AbsoluteExpirationRelativeToNow = PublicBootstrapCacheTtl,
                    Size = EstimateBootstrapCacheSize(bootstrap)
                });
        }

        bootstrap = responseUrlNormalizer.Normalize(bootstrap);
        if (bootstrap.SyncState is not null)
        {
            Response.Headers["X-Data-Version"] = bootstrap.SyncState.Version;
        }
        Response.Headers["X-Bootstrap-Scope"] = normalizedScope;
        Response.Headers["X-Bootstrap-Cache"] = admin is null ? "miss" : "bypass-admin";

        logger.LogInformation(
            "[MobileBootstrap] settings loaded. enabledLanguagesCount={EnabledLanguagesCount}; contactExists={ContactExists}; syncVersion={Version}",
            bootstrap.Settings.SupportedLanguages.Count,
            HasContactSettings(bootstrap.Settings),
            bootstrap.SyncState?.Version ?? syncState.Version);

        logger.LogInformation(
            "[BootstrapPerf] cache={Cache}; scope={Scope}; adminUserId={AdminUserId}; role={Role}; languageCode={LanguageCode}; version={Version}; pois={PoiCount}; foodItems={FoodItemCount}; promotions={PromotionCount}; audioGuides={AudioGuideCount}; elapsedMs={ElapsedMs}",
            admin is null ? "miss" : "bypass-admin",
            normalizedScope,
            admin?.UserId,
            admin?.Role,
            normalizedLanguageCode,
            bootstrap.SyncState?.Version,
            bootstrap.Pois.Count,
            bootstrap.FoodItems.Count,
            bootstrap.Promotions.Count,
            bootstrap.AudioGuides.Count,
            stopwatch.Elapsed.TotalMilliseconds.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture));

        return Ok(ApiResponse<AdminBootstrapResponse>.Ok(bootstrap));
    }

    [HttpGet("sync-state")]
    public ActionResult<ApiResponse<DataSyncState>> GetSyncState()
    {
        var syncState = repository.GetSyncState();
        Response.Headers["X-Data-Version"] = syncState.Version;
        return Ok(ApiResponse<DataSyncState>.Ok(syncState));
    }

    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    [HttpGet("dashboard/summary")]
    public ActionResult<ApiResponse<DashboardSummaryResponse>> GetDashboardSummary()
    {
        Response.Headers.CacheControl = "no-store, no-cache, must-revalidate, proxy-revalidate";
        Response.Headers.Pragma = "no-cache";
        Response.Headers.Expires = "0";

        return Ok(ApiResponse<DashboardSummaryResponse>.Ok(
            repository.GetDashboardSummary(adminRequestContextResolver.RequireAuthenticatedAdmin())));
    }

    [HttpGet("categories")]
    public ActionResult<ApiResponse<IReadOnlyList<PoiCategory>>> GetCategories()
        => Ok(ApiResponse<IReadOnlyList<PoiCategory>>.Ok(repository.GetCategories()));

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

    private static string NormalizeBootstrapScope(string? scope)
    {
        var normalized = string.IsNullOrWhiteSpace(scope)
            ? "full"
            : scope.Trim().ToLowerInvariant();
        return normalized is "map" or "light" or "mobile-map"
            ? "map"
            : "full";
    }

    private static bool IsMapBootstrapScope(string scope)
        => string.Equals(scope, "map", StringComparison.OrdinalIgnoreCase);

    private static AdminBootstrapResponse TrimForPublicMapBootstrap(AdminBootstrapResponse bootstrap)
    {
        var publishedPoiIds = bootstrap.Pois
            .Where(IsPublishedPoi)
            .Select(item => item.Id)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        return bootstrap with
        {
            Users = [],
            Pois = bootstrap.Pois
                .Where(item => publishedPoiIds.Contains(item.Id))
                .ToList(),
            Translations = bootstrap.Translations
                .Where(item =>
                    publishedPoiIds.Contains(item.EntityId) ||
                    string.Equals(item.EntityType, "route", StringComparison.OrdinalIgnoreCase))
                .ToList(),
            AudioGuides = [],
            FoodItems = [],
            Promotions = [],
            MediaAssets = bootstrap.MediaAssets
                .Where(item =>
                    string.Equals(item.Type, "image", StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(item.EntityType, "poi", StringComparison.OrdinalIgnoreCase) &&
                    publishedPoiIds.Contains(item.EntityId))
                .ToList(),
            UsageEvents = [],
            ViewLogs = [],
            AudioListenLogs = [],
            AuditLogs = []
        };
    }

    private static bool IsPublishedPoi(Poi poi)
        => string.Equals(poi.Status, "published", StringComparison.OrdinalIgnoreCase) &&
           poi.IsActive;

    private static bool HasContactSettings(SystemSetting settings)
        => !string.IsNullOrWhiteSpace(settings.AppName) ||
           !string.IsNullOrWhiteSpace(settings.SupportPhone) ||
           !string.IsNullOrWhiteSpace(settings.SupportEmail) ||
           !string.IsNullOrWhiteSpace(settings.ContactAddress) ||
           !string.IsNullOrWhiteSpace(settings.SupportInstructions) ||
           !string.IsNullOrWhiteSpace(settings.SupportHours);

    private static int EstimateBootstrapCacheSize(AdminBootstrapResponse bootstrap)
        => Math.Max(
            1,
            bootstrap.Categories.Count +
            bootstrap.Pois.Count +
            bootstrap.Translations.Count +
            bootstrap.MediaAssets.Count +
            bootstrap.Routes.Count +
            bootstrap.FoodItems.Count +
            bootstrap.Promotions.Count +
            bootstrap.AudioGuides.Count);
}
