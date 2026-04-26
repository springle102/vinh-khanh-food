using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using VinhKhanh.MobileApp.Helpers;

namespace VinhKhanh.MobileApp.Services;

public interface IMobileAnalyticsService
{
    Task TrackPoiViewAsync(
        string poiId,
        string? languageCode = null,
        string source = "poi_detail",
        CancellationToken cancellationToken = default);

    Task TrackAudioListenAsync(
        string poiId,
        string? languageCode = null,
        string source = "audio_player",
        int? durationInSeconds = null,
        string? playbackKey = null,
        CancellationToken cancellationToken = default);

    Task TrackOfferViewAsync(
        string poiId,
        string? promotionId = null,
        string? languageCode = null,
        string source = "poi_detail_promotions",
        CancellationToken cancellationToken = default);
}

public sealed class MobileAnalyticsService : IMobileAnalyticsService
{
    private static readonly TimeSpan PoiViewDuplicateWindow = TimeSpan.FromMilliseconds(900);
    private static readonly TimeSpan AudioListenDuplicateWindow = TimeSpan.FromMilliseconds(1200);
    private static readonly TimeSpan OfferViewDuplicateWindow = TimeSpan.FromMilliseconds(900);
    private static readonly TimeSpan RecentKeyRetention = TimeSpan.FromMinutes(3);

    private readonly IFoodStreetDataService _dataService;
    private readonly IAppLanguageService _languageService;
    private readonly ILogger<MobileAnalyticsService> _logger;
    private readonly SemaphoreSlim _recentEventLock = new(1, 1);
    private readonly Dictionary<string, DateTimeOffset> _recentEventKeys = new(StringComparer.OrdinalIgnoreCase);

    public MobileAnalyticsService(
        IFoodStreetDataService dataService,
        IAppLanguageService languageService,
        ILogger<MobileAnalyticsService> logger)
    {
        _dataService = dataService;
        _languageService = languageService;
        _logger = logger;
    }

    public async Task TrackPoiViewAsync(
        string poiId,
        string? languageCode = null,
        string source = "poi_detail",
        CancellationToken cancellationToken = default)
    {
        var normalizedPoiId = NormalizePoiId(poiId);
        if (normalizedPoiId is null)
        {
            return;
        }

        var normalizedLanguageCode = NormalizeLanguageCode(languageCode);
        var normalizedSource = NormalizeSource(source, "poi_detail");
        var dedupeKey = $"poi_view:{normalizedPoiId}:{normalizedSource}";

        if (!await TryReserveEventSlotAsync(dedupeKey, PoiViewDuplicateWindow, cancellationToken))
        {
            _logger.LogInformation(
                "[Analytics] Ignoring duplicate POI view within cooldown. poiId={PoiId}; language={LanguageCode}; source={Source}",
                normalizedPoiId,
                normalizedLanguageCode,
                normalizedSource);
            return;
        }

        try
        {
            _logger.LogInformation(
                "[Analytics] Tracking POI view. poiId={PoiId}; language={LanguageCode}; source={Source}",
                normalizedPoiId,
                normalizedLanguageCode,
                normalizedSource);
            await _dataService.TrackPoiViewAsync(normalizedPoiId, normalizedLanguageCode, normalizedSource);
        }
        catch (Exception exception)
        {
            _logger.LogWarning(
                exception,
                "[Analytics] Failed to track POI view. poiId={PoiId}; language={LanguageCode}; source={Source}",
                normalizedPoiId,
                normalizedLanguageCode,
                normalizedSource);
        }
    }

    public async Task TrackAudioListenAsync(
        string poiId,
        string? languageCode = null,
        string source = "audio_player",
        int? durationInSeconds = null,
        string? playbackKey = null,
        CancellationToken cancellationToken = default)
    {
        var normalizedPoiId = NormalizePoiId(poiId);
        if (normalizedPoiId is null)
        {
            return;
        }

        var normalizedLanguageCode = NormalizeLanguageCode(languageCode);
        var normalizedSource = NormalizeSource(source, "audio_player");
        var normalizedPlaybackKey = string.IsNullOrWhiteSpace(playbackKey) ? "none" : playbackKey.Trim();
        var dedupeKey = $"audio_play:{normalizedPoiId}:{normalizedLanguageCode}:{normalizedSource}:{normalizedPlaybackKey}";

        if (!await TryReserveEventSlotAsync(dedupeKey, AudioListenDuplicateWindow, cancellationToken))
        {
            _logger.LogInformation(
                "[Analytics] Ignoring duplicate audio listen within cooldown. poiId={PoiId}; language={LanguageCode}; source={Source}; playbackKey={PlaybackKey}",
                normalizedPoiId,
                normalizedLanguageCode,
                normalizedSource,
                normalizedPlaybackKey);
            return;
        }

        try
        {
            _logger.LogInformation(
                "[Analytics] Tracking audio listen. poiId={PoiId}; language={LanguageCode}; source={Source}; durationInSeconds={DurationInSeconds}; playbackKey={PlaybackKey}",
                normalizedPoiId,
                normalizedLanguageCode,
                normalizedSource,
                durationInSeconds,
                normalizedPlaybackKey);
            await _dataService.TrackAudioPlayAsync(
                normalizedPoiId,
                normalizedLanguageCode,
                normalizedSource,
                durationInSeconds);
        }
        catch (Exception exception)
        {
            _logger.LogWarning(
                exception,
                "[Analytics] Failed to track audio listen. poiId={PoiId}; language={LanguageCode}; source={Source}; playbackKey={PlaybackKey}",
                normalizedPoiId,
                normalizedLanguageCode,
                normalizedSource,
                normalizedPlaybackKey);
        }
    }

    public async Task TrackOfferViewAsync(
        string poiId,
        string? promotionId = null,
        string? languageCode = null,
        string source = "poi_detail_promotions",
        CancellationToken cancellationToken = default)
    {
        var normalizedPoiId = NormalizePoiId(poiId);
        if (normalizedPoiId is null)
        {
            return;
        }

        var normalizedPromotionId = string.IsNullOrWhiteSpace(promotionId) ? "none" : promotionId.Trim();
        var normalizedLanguageCode = NormalizeLanguageCode(languageCode);
        var normalizedSource = NormalizeSource(source, "poi_detail_promotions");
        var dedupeKey = $"offer_view:{normalizedPoiId}:{normalizedPromotionId}:{normalizedLanguageCode}:{normalizedSource}";

        if (!await TryReserveEventSlotAsync(dedupeKey, OfferViewDuplicateWindow, cancellationToken))
        {
            _logger.LogInformation(
                "[Analytics] Ignoring duplicate offer view within cooldown. poiId={PoiId}; promotionId={PromotionId}; language={LanguageCode}; source={Source}",
                normalizedPoiId,
                normalizedPromotionId,
                normalizedLanguageCode,
                normalizedSource);
            return;
        }

        try
        {
            _logger.LogInformation(
                "[Analytics] Tracking offer view. poiId={PoiId}; promotionId={PromotionId}; language={LanguageCode}; source={Source}",
                normalizedPoiId,
                normalizedPromotionId,
                normalizedLanguageCode,
                normalizedSource);
            await _dataService.TrackOfferViewAsync(
                normalizedPoiId,
                string.Equals(normalizedPromotionId, "none", StringComparison.OrdinalIgnoreCase) ? null : normalizedPromotionId,
                normalizedLanguageCode,
                normalizedSource);
        }
        catch (Exception exception)
        {
            _logger.LogWarning(
                exception,
                "[Analytics] Failed to track offer view. poiId={PoiId}; promotionId={PromotionId}; language={LanguageCode}; source={Source}",
                normalizedPoiId,
                normalizedPromotionId,
                normalizedLanguageCode,
                normalizedSource);
        }
    }

    private async Task<bool> TryReserveEventSlotAsync(
        string eventKey,
        TimeSpan duplicateWindow,
        CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;

        await _recentEventLock.WaitAsync(cancellationToken);
        try
        {
            PruneExpiredRecentKeys(now);
            if (_recentEventKeys.TryGetValue(eventKey, out var lastSeenAt) &&
                now - lastSeenAt < duplicateWindow)
            {
                return false;
            }

            _recentEventKeys[eventKey] = now;
            return true;
        }
        finally
        {
            _recentEventLock.Release();
        }
    }

    private void PruneExpiredRecentKeys(DateTimeOffset now)
    {
        if (_recentEventKeys.Count == 0)
        {
            return;
        }

        var expiredKeys = _recentEventKeys
            .Where(entry => now - entry.Value >= RecentKeyRetention)
            .Select(entry => entry.Key)
            .ToList();

        foreach (var expiredKey in expiredKeys)
        {
            _recentEventKeys.Remove(expiredKey);
        }
    }

    private string NormalizeLanguageCode(string? languageCode)
        => AppLanguage.NormalizeCode(languageCode ?? _languageService.CurrentLanguage);

    private static string NormalizeSource(string? source, string fallbackSource)
        => string.IsNullOrWhiteSpace(source) ? fallbackSource : source.Trim();

    private static string? NormalizePoiId(string? poiId)
        => string.IsNullOrWhiteSpace(poiId) ? null : poiId.Trim();
}
