using System.Text.Json;
using Microsoft.Extensions.Logging;
using VinhKhanh.MobileApp.Helpers;
using VinhKhanh.MobileApp.Models;

namespace VinhKhanh.MobileApp.Services;

public sealed partial class FoodStreetApiDataService
{
    private void OnOfflinePackageStateChanged(object? sender, OfflinePackageState state)
    {
        if (state.Status is OfflinePackageLifecycleStatus.Completed or OfflinePackageLifecycleStatus.NotInstalled)
        {
            InvalidateOfflinePackageCache();
        }
    }

    private void InvalidateOfflinePackageCache()
    {
        _localDatasetLoadAttempted = false;
        _offlinePackageLoadAttempted = false;
        InvalidateBootstrapSnapshot();
    }

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

        var installation = await _offlineStorageService.LoadInstallationAsync();
        if (installation is not null)
        {
            ApplyOfflineAssetMap(envelope.Data, installation.AssetMap);
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

    private async Task<bool> TryLoadOfflinePackageAsync(string requestedLanguageCode)
    {
        if (_bootstrapSource is not null || _offlinePackageLoadAttempted)
        {
            return _bootstrapSource is not null;
        }

        _offlinePackageLoadAttempted = true;

        var installation = await _bundledSeedService.EnsureInstalledAsync()
                           ?? await _offlineStorageService.LoadInstallationAsync();
        if (installation is null || string.IsNullOrWhiteSpace(installation.BootstrapEnvelopeJson))
        {
            return false;
        }

        var envelope = JsonSerializer.Deserialize<ApiEnvelope<AdminBootstrapDto>>(
            installation.BootstrapEnvelopeJson,
            JsonOptions);
        if (envelope?.Success != true || envelope.Data is null)
        {
            return false;
        }

        ApplyOfflineAssetMap(envelope.Data, installation.AssetMap);
        _bootstrapSource = envelope.Data;
        _bootstrapSourceLanguageCode = AppLanguage.NormalizeCode(requestedLanguageCode);
        _syncState = envelope.Data.SyncState;
        return TryRebuildBootstrapSnapshotFromCache(requestedLanguageCode, "offline-package");
    }

    private static void ApplyOfflineAssetMap(AdminBootstrapDto bootstrap, IReadOnlyDictionary<string, string> assetMap)
    {
        if (assetMap.Count == 0)
        {
            return;
        }

        foreach (var mediaAsset in bootstrap.MediaAssets)
        {
            if (OfflineAssetUrlHelper.TryResolveAssetPath(assetMap, mediaAsset.Url, out var localPath))
            {
                mediaAsset.Url = localPath;
            }
        }

        foreach (var audioGuide in bootstrap.AudioGuides)
        {
            if (string.IsNullOrWhiteSpace(audioGuide.RemoteAudioUrl) &&
                !string.IsNullOrWhiteSpace(audioGuide.AudioUrl))
            {
                audioGuide.RemoteAudioUrl = audioGuide.AudioUrl.Trim();
            }

            if (OfflineAssetUrlHelper.TryResolveAssetPath(assetMap, audioGuide.AudioUrl, out var localPath))
            {
                audioGuide.AudioUrl = localPath;
            }
        }

        foreach (var foodItem in bootstrap.FoodItems)
        {
            if (OfflineAssetUrlHelper.TryResolveAssetPath(assetMap, foodItem.ImageUrl, out var localPath))
            {
                foodItem.ImageUrl = localPath;
            }
        }

        foreach (var route in bootstrap.Routes)
        {
            if (OfflineAssetUrlHelper.TryResolveAssetPath(assetMap, route.CoverImageUrl, out var localPath))
            {
                route.CoverImageUrl = localPath;
            }
        }
    }

    private static void ApplyOfflineAssetMap(PoiDetailDto detail, IReadOnlyDictionary<string, string> assetMap)
    {
        if (assetMap.Count == 0)
        {
            return;
        }

        foreach (var mediaAsset in detail.MediaAssets)
        {
            if (OfflineAssetUrlHelper.TryResolveAssetPath(assetMap, mediaAsset.Url, out var localPath))
            {
                mediaAsset.Url = localPath;
            }
        }

        foreach (var audioGuide in detail.AudioGuides)
        {
            if (string.IsNullOrWhiteSpace(audioGuide.RemoteAudioUrl) &&
                !string.IsNullOrWhiteSpace(audioGuide.AudioUrl))
            {
                audioGuide.RemoteAudioUrl = audioGuide.AudioUrl.Trim();
            }

            if (OfflineAssetUrlHelper.TryResolveAssetPath(assetMap, audioGuide.AudioUrl, out var localPath))
            {
                audioGuide.AudioUrl = localPath;
            }
        }

        foreach (var foodItem in detail.FoodItems)
        {
            if (OfflineAssetUrlHelper.TryResolveAssetPath(assetMap, foodItem.ImageUrl, out var localPath))
            {
                foodItem.ImageUrl = localPath;
            }
        }
    }

    private async Task<IReadOnlyDictionary<string, string>?> TryLoadOfflineAssetMapAsync(CancellationToken cancellationToken)
    {
        try
        {
            var installation = await _offlineStorageService.LoadInstallationAsync(cancellationToken)
                               ?? await _bundledSeedService.EnsureInstalledAsync(cancellationToken);
            return installation?.AssetMap.Count > 0
                ? installation.AssetMap
                : null;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            _logger.LogDebug(exception, "[OfflineAssets] Unable to load offline asset map.");
            return null;
        }
    }

    private async Task ApplyCurrentOfflineAssetMapAsync(AdminBootstrapDto bootstrap, CancellationToken cancellationToken)
    {
        var assetMap = await TryLoadOfflineAssetMapAsync(cancellationToken);
        if (assetMap is not null)
        {
            ApplyOfflineAssetMap(bootstrap, assetMap);
        }
    }

    private async Task ApplyCurrentOfflineAssetMapAsync(PoiDetailDto detail, CancellationToken cancellationToken)
    {
        var assetMap = await TryLoadOfflineAssetMapAsync(cancellationToken);
        if (assetMap is not null)
        {
            ApplyOfflineAssetMap(detail, assetMap);
        }
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
            "offline-demo-tour",
            GetTourThemeText(),
            LocalizeRouteTheme("tong-hop"),
            GetTourSummaryText(),
            90,
            DateTimeOffset.UtcNow,
            FallbackPois.Select(item => item.Id).ToList());
    }
}
