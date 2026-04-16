using Microsoft.Maui.ApplicationModel;
using Microsoft.Extensions.Logging;
using Microsoft.Maui.Storage;
using VinhKhanh.MobileApp.Helpers;
using VinhKhanh.MobileApp.Models;

namespace VinhKhanh.MobileApp.Services;

public interface IAudioPlayerService
{
    bool IsPlaying { get; }
    Task PlayPoiNarrationAsync(PoiExperienceDetail detail, string languageCode, CancellationToken cancellationToken = default);
    Task StopAsync();
}

public interface IAutoNarrationService
{
    bool IsEnabled { get; }
    double ActivationRadiusMeters { get; }
    TimeSpan PlaybackGap { get; }

    Task SetEnabledAsync(bool isEnabled);
    Task ConfigureRouteAsync(RouteNarrationPlan? routePlan, CancellationToken cancellationToken = default);
    Task ResetAsync(bool stopAudio = true, CancellationToken cancellationToken = default);
    Task<AutoNarrationResult> ProcessLocationAsync(
        UserLocationPoint location,
        IReadOnlyList<PoiLocation> allPois,
        bool isMockLocation,
        CancellationToken cancellationToken = default);
    Task<AutoNarrationResult> TriggerPoiAsync(PoiLocation poi, CancellationToken cancellationToken = default);
}

public sealed class AutoNarrationService : IAutoNarrationService
{
    private const double DefaultTriggerRadiusMeters = 20d;
    private const int DefaultPriority = 100;
    private const int ImportantPriority = 200;
    private static readonly TimeSpan DefaultCooldown = TimeSpan.FromSeconds(25);
    private static readonly TimeSpan AntiJumpDwellTime = TimeSpan.FromSeconds(3);

    private readonly SemaphoreSlim _stateLock = new(1, 1);
    private readonly HashSet<string> _recentlyPlayedPoiIds = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _lastNearbyPoiIds = new(StringComparer.OrdinalIgnoreCase);
    private readonly IFoodStreetDataService _dataService;
    private readonly IPoiProximityService _poiProximityService;
    private readonly IAudioPlayerService _audioPlayerService;
    private readonly IAppLanguageService _languageService;
    private readonly ITourStateService _tourStateService;
    private readonly ILogger<AutoNarrationService> _logger;

    private RouteNarrationPlan? _activeRoutePlan;
    private string? _currentPlayingPoiId;
    private DateTimeOffset _lastPlayedTime = DateTimeOffset.MinValue;
    private string? _lastPlayedPoiId;
    private string? _pendingPoiId;
    private DateTimeOffset _pendingPoiEnteredAt = DateTimeOffset.MinValue;
    private string? _cachedActiveTourId;
    private HashSet<string> _cachedActiveTourPoiIds = new(StringComparer.OrdinalIgnoreCase);

    public AutoNarrationService(
        IFoodStreetDataService dataService,
        IPoiProximityService poiProximityService,
        IAudioPlayerService audioPlayerService,
        IAppLanguageService languageService,
        ITourStateService tourStateService,
        ILogger<AutoNarrationService> logger)
    {
        _dataService = dataService;
        _poiProximityService = poiProximityService;
        _audioPlayerService = audioPlayerService;
        _languageService = languageService;
        _tourStateService = tourStateService;
        _logger = logger;
        IsEnabled = Preferences.Default.Get(AppPreferenceKeys.AutoNarrationEnabled, true);
    }

    public bool IsEnabled { get; private set; }

    public double ActivationRadiusMeters => DefaultTriggerRadiusMeters;

    public TimeSpan PlaybackGap => DefaultCooldown;

    public Task SetEnabledAsync(bool isEnabled)
    {
        IsEnabled = isEnabled;
        Preferences.Default.Set(AppPreferenceKeys.AutoNarrationEnabled, isEnabled);
        return Task.CompletedTask;
    }

    public async Task ConfigureRouteAsync(RouteNarrationPlan? routePlan, CancellationToken cancellationToken = default)
    {
        await _stateLock.WaitAsync(cancellationToken);
        try
        {
            _activeRoutePlan = routePlan;
            ResetNarrationStateLocked();
        }
        finally
        {
            _stateLock.Release();
        }

        await _audioPlayerService.StopAsync();
    }

    public async Task ResetAsync(bool stopAudio = true, CancellationToken cancellationToken = default)
    {
        await _stateLock.WaitAsync(cancellationToken);
        try
        {
            _activeRoutePlan = null;
            _cachedActiveTourId = null;
            _cachedActiveTourPoiIds.Clear();
            ResetNarrationStateLocked();
        }
        finally
        {
            _stateLock.Release();
        }

        if (stopAudio)
        {
            await _audioPlayerService.StopAsync();
        }
    }

    public async Task<AutoNarrationResult> ProcessLocationAsync(
        UserLocationPoint location,
        IReadOnlyList<PoiLocation> allPois,
        bool isMockLocation,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(location);
        ArgumentNullException.ThrowIfNull(allPois);

        var currentTourPoiIds = await ResolveCurrentTourPoiIdsAsync(cancellationToken);
        var evaluation = BuildEvaluation(location, allPois, currentTourPoiIds);

        if (!IsEnabled)
        {
            _logger.LogDebug(
                "Auto narration is disabled. location={Latitude},{Longitude}",
                location.Latitude,
                location.Longitude);
            return new AutoNarrationResult
            {
                Snapshot = evaluation.Snapshot,
                Decision = AutoNarrationDecision.Disabled,
                IsMockLocation = isMockLocation
            };
        }

        if (evaluation.NearbyCandidates.Count == 0)
        {
            _logger.LogDebug(
                "No nearby POI matched the auto narration trigger radius. location={Latitude},{Longitude}",
                location.Latitude,
                location.Longitude);
            await _stateLock.WaitAsync(cancellationToken);
            try
            {
                UpdateNearbyStateLocked(new HashSet<string>(StringComparer.OrdinalIgnoreCase), null, DateTimeOffset.UtcNow);
            }
            finally
            {
                _stateLock.Release();
            }

            return new AutoNarrationResult
            {
                Snapshot = evaluation.Snapshot,
                Decision = AutoNarrationDecision.NoNearbyPoi,
                IsMockLocation = isMockLocation
            };
        }

        var selectedCandidate = evaluation.NearbyCandidates[0];
        var nearbyPoiIds = evaluation.NearbyCandidates
            .Select(item => item.Poi.Id)
            .Where(item => !string.IsNullOrWhiteSpace(item))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var now = DateTimeOffset.UtcNow;
        var shouldPlay = false;
        var decision = AutoNarrationDecision.None;

        await _stateLock.WaitAsync(cancellationToken);
        try
        {
            var selectedPoiId = selectedCandidate.Poi.Id;
            var enteredSelectedPoi = !_lastNearbyPoiIds.Contains(selectedPoiId);
            UpdateNearbyStateLocked(nearbyPoiIds, selectedPoiId, now);

            var hasExitedLastPlayedPoi =
                string.IsNullOrWhiteSpace(_lastPlayedPoiId) ||
                !nearbyPoiIds.Contains(_lastPlayedPoiId);
            var hasStayedInsideSelectedPoi = HasPendingCandidateSettledLocked(selectedPoiId, now);
            var canSwitchByExitAndEnter = hasExitedLastPlayedPoi && enteredSelectedPoi;

            if (!string.IsNullOrWhiteSpace(_currentPlayingPoiId) || _audioPlayerService.IsPlaying)
            {
                decision = AutoNarrationDecision.Busy;
            }
            else if (_recentlyPlayedPoiIds.Contains(selectedPoiId))
            {
                decision = AutoNarrationDecision.Busy;
            }
            else if (IsInCooldownLocked(now))
            {
                decision = AutoNarrationDecision.Cooldown;
            }
            else if (canSwitchByExitAndEnter || hasStayedInsideSelectedPoi)
            {
                _currentPlayingPoiId = selectedPoiId;
                shouldPlay = true;
                decision = AutoNarrationDecision.Played;
            }
            else
            {
                decision = AutoNarrationDecision.Busy;
            }
        }
        finally
        {
            _stateLock.Release();
        }

        if (!shouldPlay)
        {
            _logger.LogInformation(
                "Auto narration decision completed without playback. poiId={PoiId}; decision={Decision}; distanceMeters={DistanceMeters:0.##}; currentPlayingPoiId={CurrentPlayingPoiId}; recentlyPlayed={RecentlyPlayed}",
                selectedCandidate.Poi.Id,
                decision,
                selectedCandidate.DistanceMeters,
                _currentPlayingPoiId ?? "(none)",
                _recentlyPlayedPoiIds.Contains(selectedCandidate.Poi.Id));
            return new AutoNarrationResult
            {
                Snapshot = evaluation.Snapshot,
                Decision = decision,
                IsMockLocation = isMockLocation
            };
        }

        var detail = await LoadPoiDetailOrFallbackAsync(selectedCandidate.Poi);
        if (detail is null)
        {
            await ReleasePlaybackReservationAsync(selectedCandidate.Poi.Id, cancellationToken);
            return new AutoNarrationResult
            {
                Snapshot = evaluation.Snapshot,
                TriggeredPoi = selectedCandidate.Poi,
                Decision = AutoNarrationDecision.None,
                IsMockLocation = isMockLocation
            };
        }

        var playbackConfirmed = await ConfirmPlaybackStartAsync(selectedCandidate.Poi.Id, now, cancellationToken);
        if (!playbackConfirmed)
        {
            _logger.LogInformation(
                "Auto narration lost the playback reservation before starting. poiId={PoiId}",
                selectedCandidate.Poi.Id);
            return new AutoNarrationResult
            {
                Snapshot = evaluation.Snapshot,
                Decision = AutoNarrationDecision.Busy,
                IsMockLocation = isMockLocation
            };
        }

        _logger.LogInformation(
            "Auto narration will start playback. poiId={PoiId}; distanceMeters={DistanceMeters:0.##}; language={LanguageCode}; isMockLocation={IsMockLocation}",
            selectedCandidate.Poi.Id,
            selectedCandidate.DistanceMeters,
            _languageService.CurrentLanguage,
            isMockLocation);
        BeginBackgroundPlayback(selectedCandidate.Poi, detail);
        return new AutoNarrationResult
        {
            Snapshot = evaluation.Snapshot,
            TriggeredPoi = selectedCandidate.Poi,
            TriggeredDetail = detail,
            Decision = AutoNarrationDecision.Played,
            IsMockLocation = isMockLocation
        };
    }

    public async Task<AutoNarrationResult> TriggerPoiAsync(PoiLocation poi, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(poi);

        var evaluation = BuildEvaluation(
            new UserLocationPoint
            {
                Latitude = poi.Latitude,
                Longitude = poi.Longitude
            },
            new[] { poi },
            new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                poi.Id
            });
        var detail = await LoadPoiDetailOrFallbackAsync(poi);
        if (detail is null)
        {
            return new AutoNarrationResult
            {
                Snapshot = evaluation.Snapshot,
                TriggeredPoi = poi,
                Decision = AutoNarrationDecision.None,
                IsMockLocation = true
            };
        }

        await _audioPlayerService.StopAsync();

        var now = DateTimeOffset.UtcNow;
        await _stateLock.WaitAsync(cancellationToken);
        try
        {
            _currentPlayingPoiId = poi.Id;
            _lastPlayedTime = now;
            _lastPlayedPoiId = poi.Id;
            _recentlyPlayedPoiIds.Clear();
            _recentlyPlayedPoiIds.Add(poi.Id);
            _lastNearbyPoiIds.Clear();
            _lastNearbyPoiIds.Add(poi.Id);
            _pendingPoiId = poi.Id;
            _pendingPoiEnteredAt = now;
        }
        finally
        {
            _stateLock.Release();
        }

        _logger.LogInformation(
            "Auto narration was triggered manually for POI. poiId={PoiId}; language={LanguageCode}",
            poi.Id,
            _languageService.CurrentLanguage);
        BeginBackgroundPlayback(poi, detail);
        return new AutoNarrationResult
        {
            Snapshot = evaluation.Snapshot,
            TriggeredPoi = poi,
            TriggeredDetail = detail,
            Decision = AutoNarrationDecision.Played,
            IsMockLocation = true
        };
    }

    private void ResetNarrationStateLocked()
    {
        _currentPlayingPoiId = null;
        _lastPlayedTime = DateTimeOffset.MinValue;
        _lastPlayedPoiId = null;
        _pendingPoiId = null;
        _pendingPoiEnteredAt = DateTimeOffset.MinValue;
        _recentlyPlayedPoiIds.Clear();
        _lastNearbyPoiIds.Clear();
    }

    private bool IsInCooldownLocked(DateTimeOffset now)
        => _lastPlayedTime != DateTimeOffset.MinValue &&
           now - _lastPlayedTime < PlaybackGap;

    private bool HasPendingCandidateSettledLocked(string poiId, DateTimeOffset now)
        => string.Equals(_pendingPoiId, poiId, StringComparison.OrdinalIgnoreCase) &&
           _pendingPoiEnteredAt != DateTimeOffset.MinValue &&
           now - _pendingPoiEnteredAt >= AntiJumpDwellTime;

    private void UpdateNearbyStateLocked(
        HashSet<string> nearbyPoiIds,
        string? selectedPoiId,
        DateTimeOffset now)
    {
        _recentlyPlayedPoiIds.RemoveWhere(item => !nearbyPoiIds.Contains(item));

        if (string.IsNullOrWhiteSpace(selectedPoiId))
        {
            _pendingPoiId = null;
            _pendingPoiEnteredAt = DateTimeOffset.MinValue;
        }
        else if (!string.Equals(_pendingPoiId, selectedPoiId, StringComparison.OrdinalIgnoreCase))
        {
            _pendingPoiId = selectedPoiId;
            _pendingPoiEnteredAt = now;
        }

        _lastNearbyPoiIds.Clear();
        _lastNearbyPoiIds.UnionWith(nearbyPoiIds);
    }

    private async Task<bool> ConfirmPlaybackStartAsync(
        string poiId,
        DateTimeOffset playedAt,
        CancellationToken cancellationToken)
    {
        await _stateLock.WaitAsync(cancellationToken);
        try
        {
            if (!string.Equals(_currentPlayingPoiId, poiId, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            _lastPlayedTime = playedAt;
            _lastPlayedPoiId = poiId;
            _recentlyPlayedPoiIds.Add(poiId);
            return true;
        }
        finally
        {
            _stateLock.Release();
        }
    }

    private async Task ReleasePlaybackReservationAsync(string poiId, CancellationToken cancellationToken)
    {
        await _stateLock.WaitAsync(cancellationToken);
        try
        {
            if (string.Equals(_currentPlayingPoiId, poiId, StringComparison.OrdinalIgnoreCase))
            {
                _currentPlayingPoiId = null;
            }
        }
        finally
        {
            _stateLock.Release();
        }
    }

    private async Task<HashSet<string>> ResolveCurrentTourPoiIdsAsync(CancellationToken cancellationToken)
    {
        var routePlan = _activeRoutePlan;
        if (routePlan?.EligiblePois.Count > 0)
        {
            return routePlan.EligiblePois
                .Select(item => item.Poi.Id)
                .Where(item => !string.IsNullOrWhiteSpace(item))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
        }

        var activeTour = await _tourStateService.GetActiveTourAsync();
        if (activeTour is null || string.IsNullOrWhiteSpace(activeTour.TourId))
        {
            _cachedActiveTourId = null;
            _cachedActiveTourPoiIds.Clear();
            return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        }

        if (string.Equals(_cachedActiveTourId, activeTour.TourId, StringComparison.OrdinalIgnoreCase) &&
            _cachedActiveTourPoiIds.Count > 0)
        {
            return new HashSet<string>(_cachedActiveTourPoiIds, StringComparer.OrdinalIgnoreCase);
        }

        var tourPlan = await _dataService.GetTourPlanAsync(activeTour.TourId, activeTour.CompletedPoiIds);
        _cachedActiveTourId = activeTour.TourId;
        _cachedActiveTourPoiIds = tourPlan.Stops
            .Select(item => item.PoiId)
            .Where(item => !string.IsNullOrWhiteSpace(item))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        return new HashSet<string>(_cachedActiveTourPoiIds, StringComparer.OrdinalIgnoreCase);
    }

    private PoiEvaluation BuildEvaluation(
        UserLocationPoint location,
        IReadOnlyList<PoiLocation> allPois,
        IReadOnlySet<string> currentTourPoiIds)
    {
        PoiPlaybackCandidate? nearestCandidate = null;
        var nearbyCandidates = new List<PoiPlaybackCandidate>();

        foreach (var poi in allPois)
        {
            var distanceMeters = _poiProximityService.CalculateDistanceMeters(
                location.Latitude,
                location.Longitude,
                poi.Latitude,
                poi.Longitude);
            var candidate = new PoiPlaybackCandidate(
                poi,
                distanceMeters,
                currentTourPoiIds.Contains(poi.Id));

            if (nearestCandidate is null || distanceMeters < nearestCandidate.DistanceMeters)
            {
                nearestCandidate = candidate;
            }

            if (distanceMeters <= ResolveTriggerRadius(poi))
            {
                nearbyCandidates.Add(candidate);
            }
        }

        var orderedNearbyCandidates = nearbyCandidates
            .OrderByDescending(item => item.IsInCurrentTour)
            .ThenByDescending(item => ResolvePriority(item.Poi))
            .ThenBy(item => item.DistanceMeters)
            .ThenBy(item => item.Poi.Id, StringComparer.OrdinalIgnoreCase)
            .ToList();
        var activeCandidate = orderedNearbyCandidates.FirstOrDefault();

        return new PoiEvaluation(
            new PoiProximitySnapshot
            {
                Location = location,
                NearestPoi = nearestCandidate?.Poi,
                NearestPoiDistanceMeters = nearestCandidate?.DistanceMeters,
                ActivePoi = activeCandidate?.Poi,
                ActivePoiDistanceMeters = activeCandidate?.DistanceMeters,
                ActivationRadiusMeters = activeCandidate is null
                    ? nearestCandidate is null
                        ? 0d
                        : ResolveTriggerRadius(nearestCandidate.Poi)
                    : ResolveTriggerRadius(activeCandidate.Poi)
            },
            orderedNearbyCandidates);
    }

    private void BeginBackgroundPlayback(PoiLocation poi, PoiExperienceDetail detail)
    {
        var languageCode = _languageService.CurrentLanguage;
        _logger.LogInformation(
            "Dispatching background POI narration playback. poiId={PoiId}; language={LanguageCode}",
            poi.Id,
            languageCode);
        MainThread.BeginInvokeOnMainThread(() => _ = RunPlaybackAsync(poi, detail, languageCode));
    }

    private async Task RunPlaybackAsync(PoiLocation poi, PoiExperienceDetail detail, string languageCode)
    {
        try
        {
            await _audioPlayerService.PlayPoiNarrationAsync(detail, languageCode);
            _logger.LogInformation(
                "Background POI narration playback completed. poiId={PoiId}; language={LanguageCode}",
                poi.Id,
                languageCode);
        }
        catch (Exception exception)
        {
            _logger.LogWarning(
                exception,
                "Background POI narration playback failed. poiId={PoiId}; language={LanguageCode}",
                poi.Id,
                languageCode);
        }
        finally
        {
            await _stateLock.WaitAsync();
            try
            {
                if (string.Equals(_currentPlayingPoiId, poi.Id, StringComparison.OrdinalIgnoreCase))
                {
                    _currentPlayingPoiId = null;
                }
            }
            finally
            {
                _stateLock.Release();
            }
        }
    }

    private async Task<PoiExperienceDetail?> LoadPoiDetailOrFallbackAsync(PoiLocation poi)
    {
        try
        {
            return await _dataService.GetPoiDetailAsync(poi.Id) ?? CreateInlineFallbackDetail(poi);
        }
        catch
        {
            return CreateInlineFallbackDetail(poi);
        }
    }

    private PoiExperienceDetail CreateInlineFallbackDetail(PoiLocation poi)
    {
        var detail = new PoiExperienceDetail
        {
            Id = poi.Id,
            Category = poi.Category,
            Address = poi.Address,
            PriceRange = poi.PriceRange,
            Latitude = poi.Latitude,
            Longitude = poi.Longitude,
            IsFeatured = poi.IsFeatured,
            Images =
            [
                poi.ThumbnailUrl
            ]
        };

        SetInlineFallbackText(detail.Name, poi.Title);
        SetInlineFallbackText(detail.Summary, poi.ShortDescription);
        SetInlineFallbackText(detail.Description, poi.ShortDescription);
        return detail;
    }

    private void SetInlineFallbackText(LocalizedTextSet target, string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return;
        }

        var normalizedLanguage = AppLanguage.NormalizeCode(_languageService.CurrentLanguage);
        target.Set(normalizedLanguage, value);

        var separatorIndex = normalizedLanguage.IndexOf('-');
        if (separatorIndex > 0)
        {
            target.Set(normalizedLanguage[..separatorIndex], value);
        }
    }

    private static double ResolveTriggerRadius(PoiLocation poi)
        => double.IsFinite(poi.TriggerRadius) && poi.TriggerRadius >= DefaultTriggerRadiusMeters
            ? poi.TriggerRadius
            : DefaultTriggerRadiusMeters;

    private static int ResolvePriority(PoiLocation poi)
        => poi.Priority > 0
            ? poi.Priority
            : poi.IsFeatured
                ? ImportantPriority
                : DefaultPriority;

    private sealed record PoiPlaybackCandidate(
        PoiLocation Poi,
        double DistanceMeters,
        bool IsInCurrentTour);

    private sealed record PoiEvaluation(
        PoiProximitySnapshot Snapshot,
        IReadOnlyList<PoiPlaybackCandidate> NearbyCandidates);
}
