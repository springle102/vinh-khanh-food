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
    Task<AutoNarrationResult> TriggerPoiAsync(PoiLocation poi, CancellationToken cancellationToken = default);
}

public sealed class AutoNarrationService : IAutoNarrationService
{
    private readonly SemaphoreSlim _stateLock = new(1, 1);
    private readonly HashSet<string> _triggeredPoiIds = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _queuedPoiIds = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<RouteEligiblePoi> _queuedPois = [];
    private readonly IFoodStreetDataService _dataService;
    private readonly IPoiProximityService _poiProximityService;
    private readonly IAudioPlayerService _audioPlayerService;
    private readonly IAppLanguageService _languageService;

    private RouteNarrationPlan? _activeRoutePlan;
    private bool _isPlaybackDispatching;
    private DateTimeOffset _nextPlaybackAllowedAt = DateTimeOffset.MinValue;

    public AutoNarrationService(
        IFoodStreetDataService dataService,
        IPoiProximityService poiProximityService,
        IAudioPlayerService audioPlayerService,
        IAppLanguageService languageService)
    {
        _dataService = dataService;
        _poiProximityService = poiProximityService;
        _audioPlayerService = audioPlayerService;
        _languageService = languageService;
        IsEnabled = Preferences.Default.Get(AppPreferenceKeys.AutoNarrationEnabled, true);
    }

    public bool IsEnabled { get; private set; }

    public double ActivationRadiusMeters => 30d;

    public TimeSpan PlaybackGap => TimeSpan.FromSeconds(4);

    public Task SetEnabledAsync(bool isEnabled)
    {
        IsEnabled = isEnabled;
        Preferences.Default.Set(AppPreferenceKeys.AutoNarrationEnabled, isEnabled);
        return Task.CompletedTask;
    }

    public async Task ConfigureRouteAsync(RouteNarrationPlan? routePlan, CancellationToken cancellationToken = default)
    {
        const bool shouldStopAudio = true;

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

        if (shouldStopAudio)
        {
            await _audioPlayerService.StopAsync();
        }
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

        var routePlan = _activeRoutePlan;
        var snapshotPois = routePlan?.EligiblePois.Count > 0
            ? routePlan.EligiblePois.Select(item => item.Poi).ToList()
            : allPois;
        var snapshotActivationRadius = routePlan?.EligiblePois.Count > 0
            ? ActivationRadiusMeters
            : 0d;
        var snapshot = snapshotPois.Count == 0
            ? new PoiProximitySnapshot
            {
                Location = location,
                ActivationRadiusMeters = snapshotActivationRadius
            }
            : _poiProximityService.Evaluate(location, snapshotPois, snapshotActivationRadius);

        if (routePlan is null || routePlan.EligiblePois.Count == 0)
        {
            return new AutoNarrationResult
            {
                Snapshot = snapshot,
                Decision = IsEnabled ? AutoNarrationDecision.NoRoute : AutoNarrationDecision.Disabled,
                IsMockLocation = isMockLocation
            };
        }

        if (!IsEnabled)
        {
            return new AutoNarrationResult
            {
                Snapshot = snapshot,
                Decision = AutoNarrationDecision.Disabled,
                IsMockLocation = isMockLocation
            };
        }

        RouteEligiblePoi? nextPlaybackCandidate = null;
        AutoNarrationDecision decision = AutoNarrationDecision.NoNearbyPoi;

        await _stateLock.WaitAsync(cancellationToken);
        try
        {
            var activeCandidates = routePlan.EligiblePois
                .Where(item => !_triggeredPoiIds.Contains(item.Poi.Id))
                .Select(item => new
                {
                    Candidate = item,
                    DistanceToUserMeters = _poiProximityService.CalculateDistanceMeters(
                        location.Latitude,
                        location.Longitude,
                        item.Poi.Latitude,
                        item.Poi.Longitude)
                })
                .Where(item => item.DistanceToUserMeters <= ActivationRadiusMeters)
                .OrderByDescending(item => item.Candidate.IsDestination)
                .ThenBy(item => item.DistanceToUserMeters)
                .ThenBy(item => item.Candidate.ClosestSegmentIndex)
                .Select(item => item.Candidate)
                .ToList();

            if (activeCandidates.Count > 0)
            {
                if (CanStartPlaybackLocked())
                {
                    nextPlaybackCandidate = activeCandidates[0];
                    ReserveForPlaybackLocked(nextPlaybackCandidate);
                    QueueCandidatesLocked(activeCandidates.Skip(1));
                    decision = AutoNarrationDecision.Played;
                }
                else
                {
                    var queuedAny = QueueCandidatesLocked(activeCandidates);
                    decision = queuedAny
                        ? AutoNarrationDecision.Queued
                        : AutoNarrationDecision.Busy;
                }
            }
            else if (CanStartPlaybackLocked() &&
                     TryDequeueNextLocked(out var queuedCandidate) &&
                     queuedCandidate is not null)
            {
                nextPlaybackCandidate = queuedCandidate;
                ReserveForPlaybackLocked(nextPlaybackCandidate);
                decision = AutoNarrationDecision.Played;
            }
        }
        finally
        {
            _stateLock.Release();
        }

        if (nextPlaybackCandidate is null)
        {
            return new AutoNarrationResult
            {
                Snapshot = snapshot,
                Decision = decision,
                IsMockLocation = isMockLocation
            };
        }

        var detail = await LoadPoiDetailOrFallbackAsync(nextPlaybackCandidate.Poi);
        if (detail is null)
        {
            await ReleasePlaybackReservationAsync(nextPlaybackCandidate.Poi.Id, cancellationToken);
            return new AutoNarrationResult
            {
                Snapshot = snapshot,
                TriggeredPoi = nextPlaybackCandidate.Poi,
                Decision = AutoNarrationDecision.None,
                IsMockLocation = isMockLocation
            };
        }

        BeginBackgroundPlayback(nextPlaybackCandidate.Poi, detail);
        return new AutoNarrationResult
        {
            Snapshot = snapshot,
            TriggeredPoi = nextPlaybackCandidate.Poi,
            TriggeredDetail = detail,
            Decision = AutoNarrationDecision.Played,
            IsMockLocation = isMockLocation
        };
    }

    public async Task<AutoNarrationResult> TriggerPoiAsync(PoiLocation poi, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(poi);

        var snapshot = _poiProximityService.Evaluate(
            new UserLocationPoint
            {
                Latitude = poi.Latitude,
                Longitude = poi.Longitude
            },
            [poi],
            ActivationRadiusMeters);
        var detail = await LoadPoiDetailOrFallbackAsync(poi);
        if (detail is null)
        {
            return new AutoNarrationResult
            {
                Snapshot = snapshot,
                TriggeredPoi = poi,
                Decision = AutoNarrationDecision.None,
                IsMockLocation = true
            };
        }

        await _audioPlayerService.StopAsync();
        await _stateLock.WaitAsync(cancellationToken);
        try
        {
            _triggeredPoiIds.Add(poi.Id);
            _queuedPoiIds.Remove(poi.Id);
            _queuedPois.RemoveAll(item => string.Equals(item.Poi.Id, poi.Id, StringComparison.OrdinalIgnoreCase));
            _isPlaybackDispatching = true;
        }
        finally
        {
            _stateLock.Release();
        }

        BeginBackgroundPlayback(poi, detail);
        return new AutoNarrationResult
        {
            Snapshot = snapshot,
            TriggeredPoi = poi,
            TriggeredDetail = detail,
            Decision = AutoNarrationDecision.Played,
            IsMockLocation = true
        };
    }

    private void ResetRouteStateLocked()
    {
        _triggeredPoiIds.Clear();
        _queuedPoiIds.Clear();
        _queuedPois.Clear();
        _isPlaybackDispatching = false;
        _nextPlaybackAllowedAt = DateTimeOffset.MinValue;
    }

    private bool CanStartPlaybackLocked()
        => !_isPlaybackDispatching &&
           !_audioPlayerService.IsPlaying &&
           DateTimeOffset.UtcNow >= _nextPlaybackAllowedAt;

    private bool QueueCandidatesLocked(IEnumerable<RouteEligiblePoi> candidates)
    {
        var queuedAny = false;
        foreach (var candidate in candidates)
        {
            if (_triggeredPoiIds.Contains(candidate.Poi.Id) ||
                _queuedPoiIds.Contains(candidate.Poi.Id))
            {
                continue;
            }

            _queuedPoiIds.Add(candidate.Poi.Id);
            _queuedPois.Add(candidate);
            queuedAny = true;
        }

        return queuedAny;
    }

    private bool TryDequeueNextLocked(out RouteEligiblePoi? candidate)
    {
        if (_queuedPois.Count == 0)
        {
            candidate = null;
            return false;
        }

        var nextCandidate = _queuedPois
            .OrderByDescending(item => item.IsDestination)
            .ThenBy(item => item.ClosestSegmentIndex)
            .ThenBy(item => item.DistanceToRouteMeters)
            .First();

        _queuedPois.Remove(nextCandidate);
        _queuedPoiIds.Remove(nextCandidate.Poi.Id);
        candidate = nextCandidate;
        return true;
    }

    private void ReserveForPlaybackLocked(RouteEligiblePoi candidate)
    {
        _queuedPoiIds.Remove(candidate.Poi.Id);
        _queuedPois.RemoveAll(item => string.Equals(item.Poi.Id, candidate.Poi.Id, StringComparison.OrdinalIgnoreCase));
        _triggeredPoiIds.Add(candidate.Poi.Id);
        _isPlaybackDispatching = true;
    }

    private async Task ReleasePlaybackReservationAsync(string poiId, CancellationToken cancellationToken)
    {
        await _stateLock.WaitAsync(cancellationToken);
        try
        {
            _isPlaybackDispatching = false;
            _triggeredPoiIds.Remove(poiId);
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
            await _audioPlayerService.PlayPoiNarrationAsync(detail, languageCode);
        }
        catch
        {
            // Best effort only. Manual replay is still available from the POI sheet.
        }

        await Task.Delay(PlaybackGap);

        RouteEligiblePoi? nextQueuedCandidate = null;
        await _stateLock.WaitAsync();
        try
        {
            _isPlaybackDispatching = false;
            _nextPlaybackAllowedAt = DateTimeOffset.UtcNow;
            if (CanStartPlaybackLocked() &&
                TryDequeueNextLocked(out var candidate) &&
                candidate is not null)
            {
                nextQueuedCandidate = candidate;
                ReserveForPlaybackLocked(nextQueuedCandidate);
            }
        }
        finally
        {
            _stateLock.Release();
        }

        if (nextQueuedCandidate is null)
        {
            return;
        }

        var nextDetail = await LoadPoiDetailOrFallbackAsync(nextQueuedCandidate.Poi);
        if (nextDetail is null)
        {
            await ReleasePlaybackReservationAsync(nextQueuedCandidate.Poi.Id, CancellationToken.None);
            return;
        }

        BeginBackgroundPlayback(nextQueuedCandidate.Poi, nextDetail);
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
}
