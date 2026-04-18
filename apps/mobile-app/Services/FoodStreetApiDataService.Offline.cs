using System.Text.Json;
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
        _offlinePackageLoadAttempted = false;
        InvalidateBootstrapSnapshot();
    }

    private async Task<bool> TryLoadOfflinePackageAsync(string requestedLanguageCode)
    {
        if (_bootstrapSource is not null || _offlinePackageLoadAttempted)
        {
            return _bootstrapSource is not null;
        }

        _offlinePackageLoadAttempted = true;

        var installation = await _offlineStorageService.LoadInstallationAsync();
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
        _syncState = envelope.Data.SyncState;
        _lastSyncCheckAt = DateTimeOffset.UtcNow;
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
            if (!string.IsNullOrWhiteSpace(mediaAsset.Url) &&
                assetMap.TryGetValue(mediaAsset.Url, out var localPath))
            {
                mediaAsset.Url = localPath;
            }
        }

        foreach (var audioGuide in bootstrap.AudioGuides)
        {
            if (!string.IsNullOrWhiteSpace(audioGuide.AudioUrl) &&
                assetMap.TryGetValue(audioGuide.AudioUrl, out var localPath))
            {
                audioGuide.AudioUrl = localPath;
            }
        }

        foreach (var foodItem in bootstrap.FoodItems)
        {
            if (!string.IsNullOrWhiteSpace(foodItem.ImageUrl) &&
                assetMap.TryGetValue(foodItem.ImageUrl, out var localPath))
            {
                foodItem.ImageUrl = localPath;
            }
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
