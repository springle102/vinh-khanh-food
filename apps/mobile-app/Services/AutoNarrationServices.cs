using Microsoft.Maui.ApplicationModel;
using Microsoft.Extensions.Logging;
using Microsoft.Maui.Storage;
using VinhKhanh.MobileApp.Helpers;
using VinhKhanh.MobileApp.Models;

namespace VinhKhanh.MobileApp.Services;

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
    private readonly ILogger<AutoNarrationService> _logger;

    private RouteNarrationPlan? _activeRoutePlan;
    private bool _isPlaybackDispatching;
    private string? _currentPlayingPoiId;
    private DateTimeOffset _lastPlayedTime = DateTimeOffset.MinValue;
    private string? _lastInsidePoiId;
    private string? _candidatePoiId;
    private DateTimeOffset _candidateSince = DateTimeOffset.MinValue;
    private string? _lastManualTriggeredPoiId;
    private DateTimeOffset _lastManualTriggeredTime = DateTimeOffset.MinValue;

    public AutoNarrationService(
        IFoodStreetDataService dataService,
        IPoiProximityService poiProximityService,
        IPoiAudioPlaybackService audioPlaybackService,
        IAppLanguageService languageService,
        ILogger<AutoNarrationService> logger)
    {
        _dataService = dataService;
        _poiProximityService = poiProximityService;
        _audioPlaybackService = audioPlaybackService;
        _languageService = languageService;
        _logger = logger;
        IsEnabled = Preferences.Default.Get(AppPreferenceKeys.AutoNarrationEnabled, true);
    }

    public bool IsEnabled { get; private set; }

    public double ActivationRadiusMeters => 30d;

    public TimeSpan PlaybackGap => TimeSpan.FromSeconds(3);
    private static TimeSpan CandidateStableDuration => TimeSpan.FromSeconds(2);
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

        var candidates = BuildCandidates(location, allPois);
        var selectedCandidate = PoiOverlapSelectionHelper.SelectBestCandidate(candidates);
        var snapshot = PoiOverlapSelectionHelper.BuildSnapshot(location, candidates, selectedCandidate);
        var selectedPoi = selectedCandidate is PoiOverlapCandidate autoCandidate ? autoCandidate.Poi : null;
        LogOverlapSelection(
            isMockLocation ? "mock-stream" : "gps-stream",
            location,
            candidates,
            selectedCandidate);

        if (!IsEnabled)
        {
            return CreateResult(snapshot, null, null, AutoNarrationDecision.Disabled, isMockLocation);
        }

        PoiLocation? nextPlaybackPoi = null;
        AutoNarrationDecision decision;
        var shouldStopAudioBecauseUserLeftPoi = false;

        await _stateLock.WaitAsync(cancellationToken);
        try
        {
            if (selectedCandidate is null)
            {
                shouldStopAudioBecauseUserLeftPoi = !string.IsNullOrWhiteSpace(_currentPlayingPoiId);
                _lastInsidePoiId = null;
                _candidatePoiId = null;
                _candidateSince = DateTimeOffset.MinValue;
                _currentPlayingPoiId = null;
                _isPlaybackDispatching = false;
                decision = AutoNarrationDecision.NoNearbyPoi;
            }
            else if (!CanStartPlaybackLocked())
            {
                decision = AutoNarrationDecision.Busy;
            }
            else if (string.Equals(_currentPlayingPoiId, selectedPoi?.Id, StringComparison.OrdinalIgnoreCase))
            {
                decision = AutoNarrationDecision.Busy;
            }
            else if (string.IsNullOrWhiteSpace(_currentPlayingPoiId) && IsInCooldownLocked())
            {
                decision = AutoNarrationDecision.Cooldown;
            }
            else if (selectedPoi is not null && CanPlayCandidateLocked(selectedPoi.Id))
            {
                nextPlaybackPoi = selectedPoi;
                ReserveForPlaybackLocked(selectedPoi.Id);
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

        if (shouldStopAudioBecauseUserLeftPoi)
        {
            _logger.LogInformation("Stopping auto narration because the user left the POI activation area.");
            await _audioPlaybackService.StopAsync();
        }

        if (nextPlaybackPoi is null)
        {
            return CreateResult(snapshot, selectedPoi, null, decision, isMockLocation);
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

        var candidates = BuildCandidates(location, allPois);
        var selectedCandidate = PoiOverlapSelectionHelper.SelectBestCandidate(candidates);
        var snapshot = PoiOverlapSelectionHelper.BuildSnapshot(location, candidates, selectedCandidate);
        var selectedPoi = selectedCandidate is PoiOverlapCandidate manualCandidate ? manualCandidate.Poi : null;
        LogOverlapSelection("mock-tap", location, candidates, selectedCandidate);

        if (!IsEnabled)
        {
            return CreateResult(snapshot, selectedPoi, null, AutoNarrationDecision.Disabled, isMockLocation: true);
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
            else if (selectedPoi is not null && IsManualReplayInCooldownLocked(selectedPoi.Id))
            {
                decision = AutoNarrationDecision.Cooldown;
            }
            else
            {
                var now = DateTimeOffset.UtcNow;
                ReserveForPlaybackLocked(selectedPoi!.Id);
                _lastManualTriggeredPoiId = selectedPoi.Id;
                _lastManualTriggeredTime = now;
                _lastInsidePoiId = selectedPoi.Id;
                _candidatePoiId = selectedPoi.Id;
                _candidateSince = now;
                nextPlaybackPoi = selectedPoi;
                decision = AutoNarrationDecision.Played;
            }
        }
        finally
        {
            _stateLock.Release();
        }

        if (nextPlaybackPoi is null)
        {
            return CreateResult(snapshot, selectedPoi, null, decision, isMockLocation: true);
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
        var candidates = new List<PoiOverlapCandidate>
        {
            new(
                poi,
                0d,
                PoiOverlapSelectionHelper.ResolveTriggerRadius(poi),
                IsInsideTriggerRadius: true)
        };
        var snapshot = PoiOverlapSelectionHelper.BuildSnapshot(
            location,
            candidates,
            candidates[0]);
        var detail = await LoadPoiDetailOrFallbackAsync(poi);
        if (detail is null)
        {
            return CreateResult(snapshot, poi, null, AutoNarrationDecision.None, isMockLocation: true);
        }

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

    private List<PoiOverlapCandidate> BuildCandidates(
        UserLocationPoint location,
        IReadOnlyList<PoiLocation> allPois)
        => PoiOverlapSelectionHelper.BuildCandidates(
            location,
            allPois,
            _poiProximityService.CalculateDistanceMeters);

    private void LogOverlapSelection(
        string source,
        UserLocationPoint location,
        IReadOnlyList<PoiOverlapCandidate> candidates,
        PoiOverlapCandidate? selectedCandidate)
    {
        _logger.LogDebug(
            "[PoiOverlap] source={Source}; latitude={Latitude}; longitude={Longitude}; candidates={Candidates}; selected={Selected}",
            source,
            location.Latitude,
            location.Longitude,
            PoiOverlapSelectionHelper.DescribeCandidates(candidates),
            PoiOverlapSelectionHelper.DescribeCandidate(selectedCandidate));
    }

    private void ResetRouteStateLocked()
    {
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
        => !_isPlaybackDispatching;

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
        _logger.LogInformation(
            "Dispatching POI auto narration. poiId={PoiId}; languageCode={LanguageCode}",
            poi.Id,
            languageCode);
        MainThread.BeginInvokeOnMainThread(() => _ = RunPlaybackAsync(poi, detail, languageCode));
    }

    private async Task RunPlaybackAsync(PoiLocation poi, PoiExperienceDetail detail, string languageCode)
    {
        try
        {
            await _audioPlaybackService.PlayAsync(detail, languageCode, trackAnalytics: false);
        }
        catch
        {
            _logger.LogDebug(
                "Auto narration playback dispatch ended with a best-effort failure. poiId={PoiId}; languageCode={LanguageCode}",
                poi.Id,
                languageCode);
        }
        finally
        {
            await _stateLock.WaitAsync();
            try
            {
                _isPlaybackDispatching = false;
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

}
