using VinhKhanh.MobileApp.Helpers;
using VinhKhanh.MobileApp.Models;
using VinhKhanh.MobileApp.Services;
using Microsoft.Maui.ApplicationModel;

namespace VinhKhanh.MobileApp.ViewModels;

public sealed partial class HomeMapViewModel
{
    private readonly IRouteService _routeService;
    private readonly IRoutePoiFilterService _routePoiFilterService;
    private readonly ISimulationService _simulationService;

    private RouteNarrationPlan? _activeRoutePlan;
    private PoiLocation? _simulationDestinationPoi;
    private SimulationMode _simulationMode = SimulationMode.Manual;
    private SimulationRunState _simulationRunState = SimulationRunState.Idle;
    private bool _isRouteLoading;
    private bool _isAutoClearingCompletedRoute;
    private int _simulationMapVersion;

    private SimulationContextKind ActiveSimulationContextKind => _activeRoutePlan?.ContextKind ?? SimulationContextKind.None;
    private string? ActiveSimulationContextId => _activeRoutePlan?.ContextId;

    public int SimulationMapVersion
    {
        get => _simulationMapVersion;
        private set => SetProperty(ref _simulationMapVersion, value);
    }

    public bool IsRouteLoading
    {
        get => _isRouteLoading;
        private set
        {
            if (SetProperty(ref _isRouteLoading, value))
            {
                RefreshSimulationBindings();
            }
        }
    }

    public bool HasSimulationRoute => _activeRoutePlan is not null;
    public bool IsSimulationPanelVisible => false;
    public bool IsSimulationAutoMode => _simulationMode == SimulationMode.Auto;
    public bool IsSimulationManualMode => _simulationMode == SimulationMode.Manual;
    public bool CanBuildRoute => SelectedPoi is not null && !IsRouteLoading;
    public bool CanStartSimulation => HasSimulationRoute && !IsRouteLoading && _simulationRunState != SimulationRunState.Running;
    public bool CanPauseSimulation => HasSimulationRoute && _simulationRunState == SimulationRunState.Running;
    public bool CanStopSimulation => HasSimulationRoute && _simulationRunState != SimulationRunState.Idle;
    public bool CanResetSimulation => _userLocationSnapshot is not null;
    public bool IsTourSimulationRouteActive => ActiveSimulationContextKind == SimulationContextKind.Tour && !string.IsNullOrWhiteSpace(ActiveSimulationContextId);
    public bool IsSelectedPoiSimulationCardVisible => SelectedPoi is not null && !IsTourSimulationRouteActive;
    public bool HasSelectedPoiSimulationRoute
        => SelectedPoi is not null &&
           ActiveSimulationContextKind == SimulationContextKind.Poi &&
           string.Equals(ActiveSimulationContextId, SelectedPoi.Id, StringComparison.OrdinalIgnoreCase);
    public bool CanStartSelectedPoiSimulation => SelectedPoi is not null && !IsRouteLoading && _simulationRunState != SimulationRunState.Running;
    public bool CanPauseSelectedPoiSimulation => HasSelectedPoiSimulationRoute && _simulationRunState == SimulationRunState.Running;
    public bool CanStopSelectedPoiSimulation => HasSelectedPoiSimulationRoute && _simulationRunState != SimulationRunState.Idle;

    public string SimulationPanelTitleText => LanguageService.GetText("simulation_panel_title");
    public string SimulationModeManualText => LanguageService.GetText("simulation_mode_manual");
    public string SimulationModeAutoText => LanguageService.GetText("simulation_mode_auto");
    public string StartSimulationText => LanguageService.GetText("simulation_action_start");
    public string PauseSimulationText => LanguageService.GetText("simulation_action_pause");
    public string StopSimulationText => LanguageService.GetText("simulation_action_stop");
    public string ResetSimulationText => LanguageService.GetText("simulation_action_reset");
    public string PoiSimulationTitleText => LanguageService.GetText("poi_simulation_title");

    public string SimulationDestinationText
    {
        get
        {
            var destinationTitle = _simulationDestinationPoi?.Title ?? SelectedPoi?.Title;
            return string.IsNullOrWhiteSpace(destinationTitle)
                ? LanguageService.GetText("simulation_destination_none")
                : string.Format(
                    LanguageService.CurrentCulture,
                    LanguageService.GetText("simulation_destination_format"),
                    destinationTitle);
        }
    }

    public string SimulationStatusText => LanguageService.GetText(_simulationRunState switch
    {
        SimulationRunState.Ready => "simulation_status_ready",
        SimulationRunState.Running => "simulation_status_running",
        SimulationRunState.Paused => "simulation_status_paused",
        SimulationRunState.Completed => "simulation_status_completed",
        _ => "simulation_status_idle"
    });

    public string SimulationDetailText
    {
        get
        {
            if (IsRouteLoading)
            {
                return LanguageService.GetText("simulation_route_loading");
            }

            if (_activeRoutePlan is null)
            {
                return SelectedPoi is null
                    ? LanguageService.GetText("simulation_select_destination_hint")
                    : string.Format(
                        LanguageService.CurrentCulture,
                        LanguageService.GetText("simulation_build_route_hint"),
                        SelectedPoi.Title);
            }

            return string.Format(
                LanguageService.CurrentCulture,
                LanguageService.GetText("simulation_route_summary"),
                FormatDistanceMeters(_activeRoutePlan.Route.DistanceMeters),
                FormatDurationText(_activeRoutePlan.Route.DurationSeconds),
                string.Format(
                    LanguageService.CurrentCulture,
                    LanguageService.GetText("simulation_valid_pois_count"),
                    _activeRoutePlan.EligiblePois.Count));
        }
    }

    public string SelectedPoiSimulationDescriptionText
    {
        get
        {
            if (SelectedPoi is null)
            {
                return LanguageService.GetText("simulation_select_destination_hint");
            }

            if (IsRouteLoading)
            {
                return LanguageService.GetText("simulation_route_loading");
            }

            if (HasSelectedPoiSimulationRoute)
            {
                return SimulationDetailText;
            }

            if (HasSimulationRoute &&
                ActiveSimulationContextKind == SimulationContextKind.Poi &&
                !string.IsNullOrWhiteSpace(_activeRoutePlan?.ContextTitle))
            {
                return string.Format(
                    LanguageService.CurrentCulture,
                    LanguageService.GetText("poi_simulation_override_hint"),
                    _activeRoutePlan.ContextTitle,
                    SelectedPoi.Title);
            }

            return string.Format(
                LanguageService.CurrentCulture,
                LanguageService.GetText("simulation_build_route_hint"),
                SelectedPoi.Title);
        }
    }

    public string SelectedPoiSimulationStatusText
        => HasSelectedPoiSimulationRoute
            ? SimulationStatusText
            : LanguageService.GetText("poi_simulation_manual_hint");

    public AsyncCommand StartSimulationCommand => new(StartSimulationAsync);
    public AsyncCommand PauseSimulationCommand => new(PauseSimulationAsync);
    public AsyncCommand StopSimulationCommand => new(StopSimulationAsync);
    public AsyncCommand ResetSimulationCommand => new(ResetSimulationAsync);
    public AsyncCommand SwitchToManualSimulationCommand => new(SwitchToManualSimulationAsync);
    public AsyncCommand SwitchToAutoSimulationCommand => new(SwitchToAutoSimulationAsync);
    public AsyncCommand StartSelectedPoiSimulationCommand => new(StartSelectedPoiSimulationAsync);
    public AsyncCommand PauseSelectedPoiSimulationCommand => new(PauseSelectedPoiSimulationAsync);
    public AsyncCommand StopSelectedPoiSimulationCommand => new(StopSelectedPoiSimulationAsync);

    public MapRouteSimulationState? GetMapRouteSimulationState()
    {
        if (_activeRoutePlan is null)
        {
            return null;
        }

        return new MapRouteSimulationState
        {
            ContextKind = _activeRoutePlan.ContextKind.ToString().ToLowerInvariant(),
            ContextId = _activeRoutePlan.ContextId,
            ContextTitle = _activeRoutePlan.ContextTitle,
            DestinationPoiId = _activeRoutePlan.DestinationPoi.Id,
            Mode = _simulationMode.ToString().ToLowerInvariant(),
            RunState = _simulationRunState.ToString().ToLowerInvariant(),
            SummaryText = SimulationDetailText,
            ProviderText = _activeRoutePlan.Route.Provider,
            Points = _activeRoutePlan.Route.Points.ToList(),
            EligiblePoiIds = _activeRoutePlan.EligiblePois.Select(item => item.Poi.Id).ToList()
        };
    }

    private async Task EnsureSimulationInitializedAsync()
    {
        await _simulationService.EnsureInitializedAsync(Pois);
        await SynchronizeSimulationStateAsync();
    }

    private async Task BuildRouteAsync()
    {
        if (SelectedPoi is null)
        {
            return;
        }

        await BuildPoiRouteAsync(SelectedPoi);
    }

    private async Task<bool> BuildPoiRouteAsync(PoiLocation destination)
    {
        if (destination is null || IsRouteLoading)
        {
            return false;
        }

        await EnsureSimulationInitializedAsync();
        var currentLocation = _simulationService.CurrentLocation;
        if (currentLocation is null)
        {
            return false;
        }

        IsRouteLoading = true;
        try
        {
            var route = await _routeService.BuildRouteAsync(currentLocation, destination);
            var eligiblePois = _routePoiFilterService.FilterEligiblePois(Pois.ToList(), route, destination.Id, 25d);
            var routePlan = new RouteNarrationPlan
            {
                ContextKind = SimulationContextKind.Poi,
                ContextId = destination.Id,
                ContextTitle = destination.Title,
                DestinationPoi = destination,
                Route = route,
                EligiblePois = eligiblePois,
                ActivationRadiusMeters = 30d,
                RouteSnapRadiusMeters = 25d
            };

            await ApplyRoutePlanAsync(routePlan);
            return true;
        }
        finally
        {
            IsRouteLoading = false;
            RefreshSimulationBindings();
        }
    }

    private async Task<bool> BuildTourRouteAsync(TourPlan? tour, bool startSimulation)
    {
        if (tour is null || IsRouteLoading)
        {
            return false;
        }

        var orderedTourPois = GetOrderedTourPois(tour);
        if (orderedTourPois.Count == 0)
        {
            return false;
        }

        await EnsureSimulationInitializedAsync();
        var currentLocation = _simulationService.CurrentLocation;
        if (currentLocation is null)
        {
            return false;
        }

        IsRouteLoading = true;
        try
        {
            var route = await _routeService.BuildRouteAsync(currentLocation, orderedTourPois);
            var destinationPoi = orderedTourPois[^1];
            var eligiblePois = _routePoiFilterService.FilterEligiblePois(orderedTourPois, route, destinationPoi.Id, 25d);
            var routePlan = new RouteNarrationPlan
            {
                ContextKind = SimulationContextKind.Tour,
                ContextId = tour.Id,
                ContextTitle = tour.Title,
                DestinationPoi = destinationPoi,
                Route = route,
                EligiblePois = eligiblePois,
                ActivationRadiusMeters = 30d,
                RouteSnapRadiusMeters = 25d
            };

            await ApplyRoutePlanAsync(routePlan);
        }
        finally
        {
            IsRouteLoading = false;
            RefreshSimulationBindings();
        }

        if (startSimulation)
        {
            await StartSimulationAsync();
        }

        return true;
    }

    private async Task ApplyRoutePlanAsync(RouteNarrationPlan routePlan)
    {
        _simulationDestinationPoi = routePlan.DestinationPoi;
        _activeRoutePlan = routePlan;
        await _simulationService.LoadRouteAsync(routePlan.Route);
        await _autoNarrationService.ConfigureRouteAsync(routePlan);
        await SynchronizeSimulationStateAsync();
        await RefreshCurrentLocationAsync();
        SimulationMapVersion++;
    }

    private async Task StartSimulationAsync()
    {
        if (!HasSimulationRoute)
        {
            return;
        }

        await _simulationService.SetModeAsync(SimulationMode.Auto);
        await _simulationService.StartAsync(TimeSpan.FromMilliseconds(450));
        await SynchronizeSimulationStateAsync();
    }

    private async Task PauseSimulationAsync()
    {
        await _simulationService.PauseAsync();
        await SynchronizeSimulationStateAsync();
    }

    private async Task StopSimulationAsync()
    {
        if (!HasSimulationRoute)
        {
            return;
        }

        await ClearActiveRouteAsync(stopAudio: true);
    }

    private async Task ResetSimulationAsync()
    {
        await _simulationService.ResetAsync();
        _activeRoutePlan = null;
        _simulationDestinationPoi = null;
        await _autoNarrationService.ResetAsync();
        await SynchronizeSimulationStateAsync();
        await RefreshCurrentLocationAsync();
        SimulationMapVersion++;
    }

    private async Task SwitchToManualSimulationAsync()
    {
        await _simulationService.SetModeAsync(SimulationMode.Manual);
        await SynchronizeSimulationStateAsync();
    }

    private async Task SwitchToAutoSimulationAsync()
    {
        await _simulationService.SetModeAsync(SimulationMode.Auto);
        await SynchronizeSimulationStateAsync();
    }

    private async Task StartSelectedPoiSimulationAsync()
    {
        if (SelectedPoi is null || IsTourSimulationRouteActive)
        {
            return;
        }

        if (!HasSelectedPoiSimulationRoute)
        {
            var built = await BuildPoiRouteAsync(SelectedPoi);
            if (!built)
            {
                return;
            }
        }

        await StartSimulationAsync();
    }

    private async Task PauseSelectedPoiSimulationAsync()
    {
        if (!HasSelectedPoiSimulationRoute)
        {
            return;
        }

        await PauseSimulationAsync();
    }

    private async Task StopSelectedPoiSimulationAsync()
    {
        if (!HasSelectedPoiSimulationRoute)
        {
            return;
        }

        await StopSimulationAsync();
    }

    private IReadOnlyList<PoiLocation> GetOrderedTourPois(TourPlan tour)
    {
        var poiLookup = Pois.ToDictionary(item => item.Id, StringComparer.OrdinalIgnoreCase);
        var checkpointIds = tour.Checkpoints.Count > 0
            ? tour.Checkpoints
                .OrderBy(item => item.Order)
                .Where(item => !item.IsCompleted)
                .Select(item => item.PoiId)
                .ToList()
            : new List<string>();

        if (checkpointIds.Count == 0)
        {
            checkpointIds = tour.Checkpoints.Count > 0
                ? tour.Checkpoints
                    .OrderBy(item => item.Order)
                    .Select(item => item.PoiId)
                    .ToList()
                : tour.Stops.Select(item => item.PoiId).ToList();
        }

        return checkpointIds
            .Select(poiId => poiLookup.TryGetValue(poiId, out var poi) ? poi : null)
            .Where(poi => poi is not null)
            .Cast<PoiLocation>()
            .ToList();
    }

    private bool IsCurrentRouteContext(SimulationContextKind contextKind, string? contextId = null)
    {
        if (_activeRoutePlan is null || _activeRoutePlan.ContextKind != contextKind)
        {
            return false;
        }

        return string.IsNullOrWhiteSpace(contextId) ||
               string.Equals(_activeRoutePlan.ContextId, contextId, StringComparison.OrdinalIgnoreCase);
    }

    private async Task ClearActiveRouteAsync(bool stopAudio = true)
    {
        await _simulationService.StopAsync(keepRoute: false);
        _activeRoutePlan = null;
        _simulationDestinationPoi = null;
        await _autoNarrationService.ResetAsync(stopAudio);
        await SynchronizeSimulationStateAsync();
        await RefreshCurrentLocationAsync();
        SimulationMapVersion++;
    }

    private void OnSimulationStateChanged(object? sender, SimulationStateChangedEventArgs e)
    {
        var shouldAutoClearCompletedRoute =
            e.RunState == SimulationRunState.Completed &&
            _activeRoutePlan is not null &&
            !_isAutoClearingCompletedRoute;

        _simulationMode = e.Mode;
        _simulationRunState = e.RunState;
        UserLocationVersion++;
        RefreshSimulationBindings();

        if (shouldAutoClearCompletedRoute)
        {
            MainThread.BeginInvokeOnMainThread(() => _ = AutoClearCompletedRouteAsync());
        }
    }

    private async Task AutoClearCompletedRouteAsync()
    {
        if (_isAutoClearingCompletedRoute ||
            _simulationRunState != SimulationRunState.Completed ||
            _activeRoutePlan is null)
        {
            return;
        }

        _isAutoClearingCompletedRoute = true;
        try
        {
            await Task.Delay(250);

            if (_simulationRunState == SimulationRunState.Completed &&
                _activeRoutePlan is not null)
            {
                await ClearActiveRouteAsync(stopAudio: false);
            }
        }
        finally
        {
            _isAutoClearingCompletedRoute = false;
            RefreshSimulationBindings();
        }
    }

    private async Task SynchronizeSimulationStateAsync()
    {
        _simulationMode = _simulationService.Mode;
        _simulationRunState = _simulationService.RunState;
        RefreshSimulationBindings();
        await Task.CompletedTask;
    }

    private void RefreshSimulationBindings()
    {
        OnPropertyChanged(nameof(IsRouteLoading));
        OnPropertyChanged(nameof(HasSimulationRoute));
        OnPropertyChanged(nameof(IsSimulationPanelVisible));
        OnPropertyChanged(nameof(IsSimulationAutoMode));
        OnPropertyChanged(nameof(IsSimulationManualMode));
        OnPropertyChanged(nameof(CanBuildRoute));
        OnPropertyChanged(nameof(CanStartSimulation));
        OnPropertyChanged(nameof(CanPauseSimulation));
        OnPropertyChanged(nameof(CanStopSimulation));
        OnPropertyChanged(nameof(CanResetSimulation));
        OnPropertyChanged(nameof(IsTourSimulationRouteActive));
        OnPropertyChanged(nameof(IsSelectedPoiSimulationCardVisible));
        OnPropertyChanged(nameof(HasSelectedPoiSimulationRoute));
        OnPropertyChanged(nameof(CanStartSelectedPoiSimulation));
        OnPropertyChanged(nameof(CanPauseSelectedPoiSimulation));
        OnPropertyChanged(nameof(CanStopSelectedPoiSimulation));
        OnPropertyChanged(nameof(SimulationPanelTitleText));
        OnPropertyChanged(nameof(SimulationDestinationText));
        OnPropertyChanged(nameof(SimulationStatusText));
        OnPropertyChanged(nameof(SimulationDetailText));
        OnPropertyChanged(nameof(SimulationModeManualText));
        OnPropertyChanged(nameof(SimulationModeAutoText));
        OnPropertyChanged(nameof(StartSimulationText));
        OnPropertyChanged(nameof(PauseSimulationText));
        OnPropertyChanged(nameof(StopSimulationText));
        OnPropertyChanged(nameof(ResetSimulationText));
        OnPropertyChanged(nameof(PoiSimulationTitleText));
        OnPropertyChanged(nameof(SelectedPoiSimulationDescriptionText));
        OnPropertyChanged(nameof(SelectedPoiSimulationStatusText));
    }

    private string FormatDurationText(double durationSeconds)
    {
        var duration = TimeSpan.FromSeconds(Math.Max(0d, durationSeconds));
        if (duration.TotalHours >= 1d)
        {
            return string.Format(
                LanguageService.CurrentCulture,
                LanguageService.GetText("simulation_duration_hours"),
                (int)duration.TotalHours,
                duration.Minutes);
        }

        return string.Format(
            LanguageService.CurrentCulture,
            LanguageService.GetText("simulation_duration_minutes"),
            Math.Max(1, (int)Math.Round(duration.TotalMinutes)));
    }
}
