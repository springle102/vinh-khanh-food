using System.Text.Json;
using Microsoft.Extensions.Logging;
using VinhKhanh.MobileApp.Helpers;
using VinhKhanh.MobileApp.Models;

namespace VinhKhanh.MobileApp.Services;

public sealed partial class FoodStreetApiDataService
{
    private async Task<bool> TryLoadLocalDatasetAsync(string requestedLanguageCode)
    {
        if (_bootstrapSource is not null || _localDatasetLoadAttempted)
        {
            return _bootstrapSource is not null;
        }

        _localDatasetLoadAttempted = true;

        MobileBootstrapCache? cache;
        try
        {
            cache = await _mobileDatasetRepository.LoadBootstrapEnvelopeAsync();
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "[OfflineDb] Failed to load local SQLite bootstrap cache.");
            return false;
        }

        if (cache is null || string.IsNullOrWhiteSpace(cache.EnvelopeJson))
        {
            _logger.LogInformation("[OfflineDb] Local SQLite bootstrap cache is empty.");
            return false;
        }

        var envelope = JsonSerializer.Deserialize<ApiEnvelope<AdminBootstrapDto>>(
            cache.EnvelopeJson,
            JsonOptions);
        if (envelope?.Success != true || envelope.Data is null)
        {
            _logger.LogWarning("[OfflineDb] Local SQLite bootstrap cache is invalid.");
            return false;
        }

        _bootstrapSource = envelope.Data;
        _bootstrapSourceLanguageCode = AppLanguage.NormalizeCode(requestedLanguageCode);
        _syncState = envelope.Data.SyncState;
        // A cached/offline snapshot is only a fallback source. Keep sync-check stale so
        // the next online load can still verify whether the backend DB has newer POI data.

        _logger.LogInformation(
            "[OfflineDb] Bootstrap loaded from local SQLite. requestedLanguage={Language}; version={Version}; source={Source}; pois={PoiCount}; routes={RouteCount}",
            requestedLanguageCode,
            cache.DatasetVersion,
            cache.InstallationSource,
            envelope.Data.Pois.Count,
            envelope.Data.Routes.Count);

        return TryRebuildBootstrapSnapshotFromCache(requestedLanguageCode, "local-sqlite");
    }

    private IReadOnlyList<TourCatalogItem> BuildFallbackPublishedTours()
    {
        var fallbackSnapshot = CreateFallbackBootstrapSnapshot();
        if (fallbackSnapshot.Routes.Count == 0)
        {
            return [];
        }

        var poiLookup = fallbackSnapshot.Pois.ToDictionary(item => item.Id, StringComparer.OrdinalIgnoreCase);
        return fallbackSnapshot.Routes
            .Select(route => CreateTourCatalogItem(route, poiLookup))
            .ToList();
    }

    private TourPlan? BuildFallbackTourPlan(string? tourId, IReadOnlyCollection<string>? completedPoiIds)
        => TryBuildTourPlanFromSnapshot(CreateFallbackBootstrapSnapshot(), tourId, completedPoiIds);

    private BootstrapSnapshot CreateFallbackBootstrapSnapshot()
    {
        var pois = BuildLocalizedFallbackPois();
        var poiDetails = FallbackPois
            .Select(item => BuildFallbackPoiDetail(item.Id))
            .Where(item => item is not null)
            .ToDictionary(item => item!.Id, item => item!, StringComparer.OrdinalIgnoreCase);
        var fallbackRoute = BuildFallbackRouteSnapshot();

        return new BootstrapSnapshot(
            pois,
            FallbackHeatPoints,
            ResolveBackdropImageUrl(poiDetails),
            new UserProfileCard(),
            poiDetails,
            BuildPremiumOffer(null),
            BuildSupportedLanguages(null),
            fallbackRoute is null ? [] : [fallbackRoute]);
    }

    private RouteSnapshot? BuildFallbackRouteSnapshot()
    {
        if (FallbackPois.Count == 0)
        {
            return null;
        }

        return new RouteSnapshot(
            "route-evening-seafood",
            "Tour hải sản buổi tối",
            LocalizeRouteTheme("hai-san"),
            "Lộ trình ngắn đi qua các quán ốc tiêu biểu, phù hợp nhóm bạn muốn cảm nhận nhịp phố Vĩnh Khánh về đêm.",
            90,
            DateTimeOffset.UtcNow,
            ["oc-oanh-1", "oc-phat", "ca-phe-che"]);
    }
}
