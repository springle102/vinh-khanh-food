using Microsoft.Maui.ApplicationModel;
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
    Task<AutoNarrationResult> ProcessMockLocationTapAsync(
        UserLocationPoint location,
        IReadOnlyList<PoiLocation> allPois,
        CancellationToken cancellationToken = default);
    Task<AutoNarrationResult> TriggerPoiAsync(PoiLocation poi, CancellationToken cancellationToken = default);
}

public sealed class AutoNarrationService : IAutoNarrationService
{
    private readonly SemaphoreSlim _stateLock = new(1, 1);
    private readonly IFoodStreetDataService _dataService;
    private readonly IPoiProximityService _poiProximityService;
    private readonly IPoiAudioPlaybackService _audioPlaybackService;
    private readonly IAppLanguageService _languageService;
    private readonly HashSet<string> _recentlyPlayedPoiIds = new(StringComparer.OrdinalIgnoreCase);

    private RouteNarrationPlan? _activeRoutePlan;
    private bool _isPlaybackDispatching;
    private string? _currentPlayingPoiId;
    private DateTimeOffset _lastPlayedTime = DateTimeOffset.MinValue;
    private string? _lastInsidePoiId;
    private string? _candidatePoiId;
    private DateTimeOffset _candidateSince = DateTimeOffset.MinValue;
    private string? _lastManualTriggeredPoiId;
    private DateTimeOffset _lastManualTriggeredTime = DateTimeOffset.MinValue;

    private sealed record PoiTriggerCandidate(
        PoiLocation Poi,
        bool IsInCurrentTour,
        double DistanceToUserMeters);

    public AutoNarrationService(
        IFoodStreetDataService dataService,
        IPoiProximityService poiProximityService,
        IPoiAudioPlaybackService audioPlaybackService,
        IAppLanguageService languageService)
    {
        _dataService = dataService;
        _poiProximityService = poiProximityService;
        _audioPlaybackService = audioPlaybackService;
        _languageService = languageService;
        IsEnabled = Preferences.Default.Get(AppPreferenceKeys.AutoNarrationEnabled, true);
    }

    public bool IsEnabled { get; private set; }

    public double ActivationRadiusMeters => 30d;

    public TimeSpan PlaybackGap => TimeSpan.FromSeconds(20);
    private static TimeSpan CandidateStableDuration => TimeSpan.FromSeconds(3);
    private static TimeSpan ManualReplayCooldown => TimeSpan.FromSeconds(7);

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
            ResetRouteStateLocked();
        }
        finally
        {
            _stateLock.Release();
        }

        await _audioPlaybackService.StopAsync();
    }

    public async Task ResetAsync(bool stopAudio = true, CancellationToken cancellationToken = default)
    {
        await _stateLock.WaitAsync(cancellationToken);
        try
        {
            _activeRoutePlan = null;
            ResetRouteStateLocked();
        }
        finally
        {
            _stateLock.Release();
        }

        if (stopAudio)
        {
            await _audioPlaybackService.StopAsync();
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

        var candidates = BuildCandidates(location, allPois, _activeRoutePlan);
        var selectedCandidate = SelectAutoCandidate(candidates);
        var snapshot = BuildSnapshot(location, candidates, selectedCandidate?.Poi);

        if (!IsEnabled)
        {
            return CreateResult(snapshot, null, null, AutoNarrationDecision.Disabled, isMockLocation);
        }

        PoiLocation? nextPlaybackPoi = null;
        AutoNarrationDecision decision;

        await _stateLock.WaitAsync(cancellationToken);
        try
        {
            if (selectedCandidate is null)
            {
                _lastInsidePoiId = null;
                _candidatePoiId = null;
                _candidateSince = DateTimeOffset.MinValue;
                decision = AutoNarrationDecision.NoNearbyPoi;
            }
            else if (!CanStartPlaybackLocked())
            {
                decision = AutoNarrationDecision.Busy;
            }
            else if (IsInCooldownLocked())
            {
                decision = AutoNarrationDecision.Cooldown;
            }
            else if (string.Equals(_currentPlayingPoiId, selectedCandidate.Poi.Id, StringComparison.OrdinalIgnoreCase))
            {
                decision = AutoNarrationDecision.Busy;
            }
            else if (CanPlayCandidateLocked(selectedCandidate.Poi.Id))
            {
                nextPlaybackPoi = selectedCandidate.Poi;
                ReserveForPlaybackLocked(selectedCandidate.Poi.Id);
                decision = AutoNarrationDecision.Played;
            }
            else
            {
                decision = AutoNarrationDecision.Cooldown;
            }
        }
        finally
        {
            _stateLock.Release();
        }

        if (nextPlaybackPoi is null)
        {
            return CreateResult(snapshot, selectedCandidate?.Poi, null, decision, isMockLocation);
        }

        var detail = await LoadPoiDetailOrFallbackAsync(nextPlaybackPoi);
        if (detail is null)
        {
            await ReleasePlaybackReservationAsync(cancellationToken);
            return CreateResult(snapshot, nextPlaybackPoi, null, AutoNarrationDecision.None, isMockLocation);
        }

        BeginBackgroundPlayback(nextPlaybackPoi, detail);
        return CreateResult(snapshot, nextPlaybackPoi, detail, AutoNarrationDecision.Played, isMockLocation);
    }

    public async Task<AutoNarrationResult> ProcessMockLocationTapAsync(
        UserLocationPoint location,
        IReadOnlyList<PoiLocation> allPois,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(location);
        ArgumentNullException.ThrowIfNull(allPois);

        var candidates = BuildCandidates(location, allPois, _activeRoutePlan);
        var selectedCandidate = SelectManualCandidate(candidates);
        var snapshot = BuildSnapshot(location, candidates, selectedCandidate?.Poi);

        if (!IsEnabled)
        {
            return CreateResult(snapshot, selectedCandidate?.Poi, null, AutoNarrationDecision.Disabled, isMockLocation: true);
        }

        if (selectedCandidate is null)
        {
            return CreateResult(snapshot, null, null, AutoNarrationDecision.NoNearbyPoi, isMockLocation: true);
        }

        PoiLocation? nextPlaybackPoi = null;
        AutoNarrationDecision decision;

        await _stateLock.WaitAsync(cancellationToken);
        try
        {
            if (!CanStartPlaybackLocked())
            {
                decision = AutoNarrationDecision.Busy;
            }
            else if (IsManualReplayInCooldownLocked(selectedCandidate.Poi.Id))
            {
                decision = AutoNarrationDecision.Cooldown;
            }
            else
            {
                var now = DateTimeOffset.UtcNow;
                ReserveForPlaybackLocked(selectedCandidate.Poi.Id);
                _lastManualTriggeredPoiId = selectedCandidate.Poi.Id;
                _lastManualTriggeredTime = now;
                _lastInsidePoiId = selectedCandidate.Poi.Id;
                _candidatePoiId = selectedCandidate.Poi.Id;
                _candidateSince = now;
                nextPlaybackPoi = selectedCandidate.Poi;
                decision = AutoNarrationDecision.Played;
            }
        }
        finally
        {
            _stateLock.Release();
        }

        if (nextPlaybackPoi is null)
        {
            return CreateResult(snapshot, selectedCandidate.Poi, null, decision, isMockLocation: true);
        }

        var detail = await LoadPoiDetailOrFallbackAsync(nextPlaybackPoi);
        if (detail is null)
        {
            await ReleasePlaybackReservationAsync(cancellationToken);
            return CreateResult(snapshot, nextPlaybackPoi, null, AutoNarrationDecision.None, isMockLocation: true);
        }

        BeginBackgroundPlayback(nextPlaybackPoi, detail);
        return CreateResult(snapshot, nextPlaybackPoi, detail, AutoNarrationDecision.Played, isMockLocation: true);
    }

    public async Task<AutoNarrationResult> TriggerPoiAsync(PoiLocation poi, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(poi);

        var location = new UserLocationPoint
        {
            Latitude = poi.Latitude,
            Longitude = poi.Longitude
        };
        var snapshot = BuildSnapshot(
            location,
            [new PoiTriggerCandidate(poi, false, 0d)],
            poi);
        var detail = await LoadPoiDetailOrFallbackAsync(poi);
        if (detail is null)
        {
            return CreateResult(snapshot, poi, null, AutoNarrationDecision.None, isMockLocation: true);
        }

        await _audioPlaybackService.StopAsync();
        await _stateLock.WaitAsync(cancellationToken);
        try
        {
            ReserveForPlaybackLocked(poi.Id);
            _lastManualTriggeredPoiId = poi.Id;
            _lastManualTriggeredTime = DateTimeOffset.UtcNow;
        }
        finally
        {
            _stateLock.Release();
        }

        BeginBackgroundPlayback(poi, detail);
        return CreateResult(snapshot, poi, detail, AutoNarrationDecision.Played, isMockLocation: true);
    }

    private static AutoNarrationResult CreateResult(
        PoiProximitySnapshot snapshot,
        PoiLocation? poi,
        PoiExperienceDetail? detail,
        AutoNarrationDecision decision,
        bool isMockLocation)
        => new()
        {
            Snapshot = snapshot,
            TriggeredPoi = poi,
            TriggeredDetail = detail,
            Decision = decision,
            IsMockLocation = isMockLocation
        };

    private List<PoiTriggerCandidate> BuildCandidates(
        UserLocationPoint location,
        IReadOnlyList<PoiLocation> allPois,
        RouteNarrationPlan? routePlan)
    {
        var currentTourPoiIds = routePlan?.EligiblePois.Count > 0
            ? routePlan.EligiblePois
                .Select(item => item.Poi.Id)
                .ToHashSet(StringComparer.OrdinalIgnoreCase)
            : null;

        return allPois
            .Select(poi => new PoiTriggerCandidate(
                poi,
                currentTourPoiIds?.Contains(poi.Id) == true,
                _poiProximityService.CalculateDistanceMeters(
                    location.Latitude,
                    location.Longitude,
                    poi.Latitude,
                    poi.Longitude)))
            .ToList();
    }

    private static PoiTriggerCandidate? SelectAutoCandidate(IEnumerable<PoiTriggerCandidate> candidates)
        => candidates
            .Where(candidate => candidate.DistanceToUserMeters <= GetEffectiveTriggerRadius(candidate.Poi))
            .OrderByDescending(candidate => candidate.IsInCurrentTour)
            .ThenByDescending(candidate => candidate.Poi.Priority)
            .ThenBy(candidate => candidate.DistanceToUserMeters)
            .ThenBy(candidate => candidate.Poi.Id, StringComparer.Ordinal)
            .FirstOrDefault();

    private static PoiTriggerCandidate? SelectManualCandidate(IEnumerable<PoiTriggerCandidate> candidates)
        => candidates
            .Where(candidate => candidate.DistanceToUserMeters <= GetEffectiveTriggerRadius(candidate.Poi))
            .OrderBy(candidate => candidate.DistanceToUserMeters)
            .ThenByDescending(candidate => candidate.Poi.Priority)
            .ThenBy(candidate => candidate.Poi.Id, StringComparer.Ordinal)
            .FirstOrDefault();

    private static PoiProximitySnapshot BuildSnapshot(
        UserLocationPoint location,
        IReadOnlyList<PoiTriggerCandidate> candidates,
        PoiLocation? activePoi)
    {
        var nearestCandidate = candidates
            .OrderBy(candidate => candidate.DistanceToUserMeters)
            .ThenBy(candidate => candidate.Poi.Id, StringComparer.Ordinal)
            .FirstOrDefault();
        var activeCandidate = activePoi is null
            ? null
            : candidates.FirstOrDefault(candidate =>
                string.Equals(candidate.Poi.Id, activePoi.Id, StringComparison.OrdinalIgnoreCase));

        return new PoiProximitySnapshot
        {
            Location = location,
            NearestPoi = nearestCandidate?.Poi,
            NearestPoiDistanceMeters = nearestCandidate?.DistanceToUserMeters,
            ActivePoi = activeCandidate?.Poi,
            ActivePoiDistanceMeters = activeCandidate?.DistanceToUserMeters,
            ActivationRadiusMeters = activeCandidate is null
                ? 0d
                : GetEffectiveTriggerRadius(activeCandidate.Poi)
        };
    }

    private void ResetRouteStateLocked()
    {
        _recentlyPlayedPoiIds.Clear();
        _isPlaybackDispatching = false;
        _currentPlayingPoiId = null;
        _lastPlayedTime = DateTimeOffset.MinValue;
        _lastInsidePoiId = null;
        _candidatePoiId = null;
        _candidateSince = DateTimeOffset.MinValue;
        _lastManualTriggeredPoiId = null;
        _lastManualTriggeredTime = DateTimeOffset.MinValue;
    }

    private bool CanStartPlaybackLocked()
        => !_isPlaybackDispatching &&
           !_audioPlaybackService.IsBusy;

    private bool IsInCooldownLocked()
        => DateTimeOffset.UtcNow - _lastPlayedTime < PlaybackGap;

    private bool CanPlayCandidateLocked(string poiId)
    {
        var now = DateTimeOffset.UtcNow;
        var hasExitedPreviousPoi = string.IsNullOrWhiteSpace(_lastInsidePoiId);
        var isSamePoiAsLastInside = string.Equals(_lastInsidePoiId, poiId, StringComparison.OrdinalIgnoreCase);
        var stableInsidePoi = string.Equals(_candidatePoiId, poiId, StringComparison.OrdinalIgnoreCase) &&
                              now - _candidateSince >= CandidateStableDuration;

        if (!string.Equals(_candidatePoiId, poiId, StringComparison.OrdinalIgnoreCase))
        {
            _candidatePoiId = poiId;
            _candidateSince = now;
        }

        if (isSamePoiAsLastInside)
        {
            return false;
        }

        // Anti-jump for continuous location updates: either leave the previous POI first,
        // or stay inside the new POI long enough to prove the reading is stable.
        if (!hasExitedPreviousPoi && !stableInsidePoi)
        {
            return false;
        }

        _lastInsidePoiId = poiId;
        return true;
    }

    private bool IsManualReplayInCooldownLocked(string poiId)
        => string.Equals(_lastManualTriggeredPoiId, poiId, StringComparison.OrdinalIgnoreCase) &&
           DateTimeOffset.UtcNow - _lastManualTriggeredTime < ManualReplayCooldown;

    private void ReserveForPlaybackLocked(string poiId)
    {
        _currentPlayingPoiId = poiId;
        _lastPlayedTime = DateTimeOffset.UtcNow;
        _recentlyPlayedPoiIds.Add(poiId);
        _isPlaybackDispatching = true;
    }

    private async Task ReleasePlaybackReservationAsync(CancellationToken cancellationToken)
    {
        await _stateLock.WaitAsync(cancellationToken);
        try
        {
            _isPlaybackDispatching = false;
            _currentPlayingPoiId = null;
        }
        finally
        {
            _stateLock.Release();
        }
    }

    private void BeginBackgroundPlayback(PoiLocation poi, PoiExperienceDetail detail)
    {
        var languageCode = _languageService.CurrentLanguage;
        MainThread.BeginInvokeOnMainThread(() => _ = RunPlaybackAsync(poi, detail, languageCode));
    }

    private async Task RunPlaybackAsync(PoiLocation poi, PoiExperienceDetail detail, string languageCode)
    {
        try
        {
            await _audioPlaybackService.PlayAsync(detail, languageCode);
        }
        catch
        {
            // Best-effort only. Manual replay is still available from the POI sheet.
        }
        finally
        {
            await _stateLock.WaitAsync();
            try
            {
                _isPlaybackDispatching = false;
                _currentPlayingPoiId = null;
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

    private static double GetEffectiveTriggerRadius(PoiLocation poi)
        => double.IsFinite(poi.TriggerRadius) && poi.TriggerRadius >= 20d
            ? poi.TriggerRadius
            : 20d;
}
