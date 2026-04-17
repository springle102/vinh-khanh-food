using Microsoft.Maui.ApplicationModel;
using VinhKhanh.MobileApp.Helpers;
using VinhKhanh.MobileApp.Models;
using VinhKhanh.MobileApp.Services;

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
    private int _simulationMapVersion;

    private SimulationContextKind ActiveSimulationContextKind => SimulationContextKind.None;
    private string? ActiveSimulationContextId => null;

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

    public bool HasSimulationRoute => false;
    public bool IsSimulationPanelVisible => false;
    public bool IsSimulationAutoMode => false;
    public bool IsSimulationManualMode => true;
    public bool CanBuildRoute => false;
    public bool CanStartSimulation => false;
    public bool CanPauseSimulation => false;
    public bool CanStopSimulation => false;
    public bool CanResetSimulation => _userLocationSnapshot is not null;
    public bool IsTourSimulationRouteActive => false;
    public bool IsSelectedPoiSimulationCardVisible => SelectedPoi is not null && IsAutoNarrationDevToolsVisible;
    public bool HasSelectedPoiSimulationRoute => false;
    public bool CanStartSelectedPoiSimulation => SelectedPoi is not null;
    public bool CanPauseSelectedPoiSimulation => false;
    public bool CanStopSelectedPoiSimulation => false;

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

    public string SimulationStatusText => LanguageService.GetText("poi_simulation_manual_hint");

    public string SimulationDetailText => LanguageService.GetText("auto_narration_dev_description");

    public string SelectedPoiSimulationDescriptionText
        => SelectedPoi is null
            ? LanguageService.GetText("simulation_select_destination_hint")
            : LanguageService.GetText("auto_narration_dev_description");

    public string SelectedPoiSimulationStatusText => LanguageService.GetText("poi_simulation_manual_hint");

    public AsyncCommand StartSimulationCommand => new(StartSimulationAsync);
    public AsyncCommand PauseSimulationCommand => new(PauseSimulationAsync);
    public AsyncCommand StopSimulationCommand => new(StopSimulationAsync);
    public AsyncCommand ResetSimulationCommand => new(ResetSimulationAsync);
    public AsyncCommand SwitchToManualSimulationCommand => new(SwitchToManualSimulationAsync);
    public AsyncCommand SwitchToAutoSimulationCommand => new(SwitchToAutoSimulationAsync);
    public AsyncCommand StartSelectedPoiSimulationCommand => new(SimulateNearSelectedPoiAsync);
    public AsyncCommand PauseSelectedPoiSimulationCommand => new(PauseSelectedPoiSimulationAsync);
    public AsyncCommand StopSelectedPoiSimulationCommand => new(StopSelectedPoiSimulationAsync);

    public MapRouteSimulationState? GetMapRouteSimulationState() => null;

    private async Task EnsureSimulationInitializedAsync()
    {
        await _simulationService.EnsureInitializedAsync(Pois);
        await SynchronizeSimulationStateAsync();
    }

    private Task BuildRouteAsync()
        => Task.CompletedTask;

    private Task<bool> BuildPoiRouteAsync(PoiLocation destination)
        => Task.FromResult(false);

    private Task<bool> BuildTourRouteAsync(TourPlan? tour, bool startSimulation)
        => Task.FromResult(false);

    private Task ApplyRoutePlanAsync(RouteNarrationPlan routePlan)
        => Task.CompletedTask;

    private async Task StartSimulationAsync()
    {
        await _simulationService.SetModeAsync(SimulationMode.Manual);
        await SynchronizeSimulationStateAsync();
    }

    private async Task PauseSimulationAsync()
    {
        await _simulationService.PauseAsync();
        await SynchronizeSimulationStateAsync();
    }

    private async Task StopSimulationAsync()
    {
        await ClearActiveRouteAsync(stopAudio: false);
    }

    private async Task ResetSimulationAsync()
    {
        await _simulationService.ResetAsync();
        _activeRoutePlan = null;
        _simulationDestinationPoi = null;
        await _autoNarrationService.ResetAsync(stopAudio: false);
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
        // Auto route playback is intentionally disabled. Keep the simulation in manual mode.
        await _simulationService.SetModeAsync(SimulationMode.Manual);
        await SynchronizeSimulationStateAsync();
    }

    private Task PauseSelectedPoiSimulationAsync()
        => Task.CompletedTask;

    private Task StopSelectedPoiSimulationAsync()
        => Task.CompletedTask;

    private bool IsCurrentRouteContext(SimulationContextKind contextKind, string? contextId = null)
        => false;

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
        _simulationMode = _simulationService.Mode;
        _simulationRunState = _simulationService.RunState;
        UserLocationVersion++;
        RefreshSimulationBindings();
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
}
