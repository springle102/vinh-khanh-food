using System.Collections.ObjectModel;
using VinhKhanh.MobileApp.Helpers;
using VinhKhanh.MobileApp.Models;
using VinhKhanh.MobileApp.Services;

namespace VinhKhanh.MobileApp.ViewModels;

public sealed partial class HomeMapViewModel
{
    private readonly ITourStateService _tourStateService;

    private TourPlan? _previewTour;
    private TourPlan? _activeTour;
    private bool _isActiveTourVisible;
    private int _tourMapVersion;
    private string _visibleTourToken = "free";

    public ObservableCollection<TourCatalogItem> AvailableTours { get; } = [];

    public TourPlan? PreviewTour
    {
        get => _previewTour;
        private set
        {
            if (SetProperty(ref _previewTour, value))
            {
                RefreshTourBindings();
            }
        }
    }

    public TourPlan? ActiveTour
    {
        get => _activeTour;
        private set
        {
            if (SetProperty(ref _activeTour, value))
            {
                RefreshTourBindings();
            }
        }
    }

    public bool IsActiveTourVisible
    {
        get => _isActiveTourVisible;
        private set
        {
            if (SetProperty(ref _isActiveTourVisible, value))
            {
                RefreshTourBindings();
            }
        }
    }

    public int TourMapVersion
    {
        get => _tourMapVersion;
        private set => SetProperty(ref _tourMapVersion, value);
    }

    public TourPlan? VisibleTour => PreviewTour ?? (IsActiveTourVisible ? ActiveTour : null);
    public TourExperienceMode TourMode => PreviewTour is not null
        ? TourExperienceMode.Preview
        : IsActiveTourVisible && ActiveTour is not null
            ? TourExperienceMode.Active
            : TourExperienceMode.Free;

    public bool HasAvailableTours => AvailableTours.Count > 0;
    public bool HasVisibleTour => VisibleTour is not null;
    public bool HasActiveSavedTour => ActiveTour is not null;
    public bool IsTourPreviewMode => TourMode == TourExperienceMode.Preview;
    public bool IsTourActiveMode => TourMode == TourExperienceMode.Active;
    public bool IsTourPanelVisible => HasVisibleTour && !HasVisibleBottomSheet;

    public string DiscoverTourTitleText => LanguageService.GetText("tour_discover_title");
    public string DiscoverTourSubtitleText => HasActiveSavedTour
        ? LanguageService.GetText("tour_discover_subtitle_resume")
        : LanguageService.GetText("tour_discover_subtitle");
    public string TourBannerText => VisibleTour is null
        ? string.Empty
        : string.Format(
            LanguageService.CurrentCulture,
            LanguageService.GetText(IsTourActiveMode ? "tour_banner_active" : "tour_banner_preview"),
            VisibleTour.Title);
    public string TourBannerMetaText => VisibleTour is null
        ? string.Empty
        : BuildTourMetaText(VisibleTour);
    public string TourPanelTitleText => VisibleTour?.Title ?? string.Empty;
    public string TourPanelDescriptionText => VisibleTour?.SummaryText ?? string.Empty;
    public string TourPanelModeText => LanguageService.GetText(
        IsTourActiveMode ? "tour_mode_active_short" :
        IsTourPreviewMode ? "tour_mode_preview_short" :
        "tour_mode_free_short");
    public string ViewAllPlacesText => LanguageService.GetText("tour_action_view_all_places");
    public string ChangeTourText => LanguageService.GetText("tour_action_change_tour");
    public string StartTourActionText => LanguageService.GetText("tour_action_start");
    public string TourPanelPrimaryActionText => IsTourActiveMode
        ? LanguageService.GetText("tour_action_continue_short")
        : LanguageService.GetText("tour_mode_active_short");
    public string PauseTourActionText => LanguageService.GetText("tour_action_pause");
    public string ExitTourActionText => LanguageService.GetText("tour_action_exit_mode");
    public string OpenMyTourActionText => LanguageService.GetText("tour_action_open_my_tour");

    public AsyncCommand OpenDiscoverToursCommand { get; private set; } = default!;
    public AsyncCommand StartTourCommand { get; private set; } = default!;
    public AsyncCommand ShowAllPlacesCommand { get; private set; } = default!;
    public AsyncCommand ChangeTourCommand { get; private set; } = default!;
    public AsyncCommand PauseTourCommand { get; private set; } = default!;
    public AsyncCommand ExitTourModeCommand { get; private set; } = default!;
    public AsyncCommand OpenMyTourCommand { get; private set; } = default!;

    private void InitializeTourCommands()
    {
        OpenDiscoverToursCommand = new(OpenDiscoverToursAsync);
        StartTourCommand = new(StartTourAsync);
        ShowAllPlacesCommand = new(ShowAllPlacesAsync);
        ChangeTourCommand = new(ChangeTourAsync);
        PauseTourCommand = new(PauseTourAsync);
        ExitTourModeCommand = new(ExitTourModeAsync);
        OpenMyTourCommand = new(OpenMyTourAsync);
    }

    private async Task ReloadTourExperienceAsync()
    {
        var publishedTours = await _dataService.GetPublishedToursAsync();
        AvailableTours.ReplaceRange(publishedTours);
        OnPropertyChanged(nameof(HasAvailableTours));

        var activeSession = await _tourStateService.GetActiveTourAsync();
        await RestoreActiveTourAsync(activeSession);

        if (!string.IsNullOrWhiteSpace(PreviewTour?.Id))
        {
            PreviewTour = await LoadTourPlanOrNullAsync(PreviewTour.Id);
        }

        SynchronizeTourCatalogStates();
    }

    private async Task RestoreActiveTourAsync(TourSessionState? activeSession)
    {
        if (activeSession is null || string.IsNullOrWhiteSpace(activeSession.TourId))
        {
            ActiveTour = null;
            IsActiveTourVisible = false;
            return;
        }

        var activeTour = await LoadTourPlanOrNullAsync(activeSession.TourId, activeSession.CompletedPoiIds);
        if (activeTour is null)
        {
            await _tourStateService.ClearActiveTourAsync();
            ActiveTour = null;
            IsActiveTourVisible = false;
            return;
        }

        ActiveTour = activeTour;
    }

    private async Task<TourPlan?> LoadTourPlanOrNullAsync(string? tourId, IReadOnlyCollection<string>? completedPoiIds = null)
    {
        if (string.IsNullOrWhiteSpace(tourId))
        {
            return null;
        }

        var tourPlan = await _dataService.GetTourPlanAsync(tourId, completedPoiIds);
        if (string.IsNullOrWhiteSpace(tourPlan.Id) || tourPlan.Stops.Count == 0)
        {
            return null;
        }

        return tourPlan;
    }

    private async Task PreviewTourAsync(TourCatalogItem? tour)
    {
        if (tour is null || string.IsNullOrWhiteSpace(tour.Id))
        {
            return;
        }

        await StopNarrationAsync();
        IsPoiDetailLoading = false;
        IsBottomSheetVisible = false;
        SelectedPoi = null;
        SelectedPoiDetail = null;

        if (ActiveTour is not null && string.Equals(ActiveTour.Id, tour.Id, StringComparison.OrdinalIgnoreCase))
        {
            PreviewTour = null;
            IsActiveTourVisible = true;
            return;
        }

        PreviewTour = await LoadTourPlanOrNullAsync(tour.Id);
        IsActiveTourVisible = false;
    }

    public async Task PreviewTourByIdAsync(string? tourId)
    {
        if (string.IsNullOrWhiteSpace(tourId))
        {
            return;
        }

        var selectedTour = AvailableTours.FirstOrDefault(item =>
            string.Equals(item.Id, tourId, StringComparison.OrdinalIgnoreCase));
        if (selectedTour is not null)
        {
            await PreviewTourAsync(selectedTour);
            return;
        }

        var fallbackTour = await LoadTourPlanOrNullAsync(tourId);
        if (fallbackTour is null)
        {
            return;
        }

        await StopNarrationAsync();
        IsPoiDetailLoading = false;
        IsBottomSheetVisible = false;
        SelectedPoi = null;
        SelectedPoiDetail = null;
        PreviewTour = fallbackTour;
        IsActiveTourVisible = false;
    }

    public async Task StartTourByIdAsync(string? tourId)
    {
        if (string.IsNullOrWhiteSpace(tourId))
        {
            return;
        }

        if (ActiveTour is not null &&
            string.Equals(ActiveTour.Id, tourId, StringComparison.OrdinalIgnoreCase))
        {
            await StartTourAsync();
            return;
        }

        var previewTour = await LoadTourPlanOrNullAsync(tourId);
        if (previewTour is null)
        {
            return;
        }

        PreviewTour = previewTour;
        IsActiveTourVisible = false;
        await StartTourAsync();
    }

    private async Task OpenDiscoverToursAsync()
    {
        if (Shell.Current is null)
        {
            return;
        }

        await Shell.Current.GoToAsync(AppRoutes.DiscoverTours);
    }

    private async Task StartTourAsync()
    {
        var sourceTour = PreviewTour ?? ActiveTour;
        if (sourceTour is null || string.IsNullOrWhiteSpace(sourceTour.Id))
        {
            return;
        }

        var activeSession = await _tourStateService.StartTourAsync(sourceTour.Id);
        var activeTour = await LoadTourPlanOrNullAsync(activeSession.TourId, activeSession.CompletedPoiIds);
        if (activeTour is null)
        {
            return;
        }

        ActiveTour = activeTour;
        PreviewTour = null;
        IsActiveTourVisible = true;
        await BuildTourRouteAsync(activeTour, startSimulation: true);
    }

    private Task ShowAllPlacesAsync()
    {
        PreviewTour = null;
        IsActiveTourVisible = false;
        return Task.CompletedTask;
    }

    private async Task ChangeTourAsync()
    {
        await OpenDiscoverToursAsync();
    }

    private async Task PauseTourAsync()
    {
        if (ActiveTour is not null && IsCurrentRouteContext(SimulationContextKind.Tour, ActiveTour.Id))
        {
            await PauseSimulationAsync();
        }

        PreviewTour = null;
        IsActiveTourVisible = false;
    }

    private async Task ExitTourModeAsync()
    {
        var shouldClearSavedTour = IsTourActiveMode && ActiveTour is not null;
        var activeTourId = ActiveTour?.Id;

        PreviewTour = null;
        IsActiveTourVisible = false;

        if (!string.IsNullOrWhiteSpace(activeTourId) &&
            IsCurrentRouteContext(SimulationContextKind.Tour, activeTourId))
        {
            await ClearActiveRouteAsync(stopAudio: true);
        }

        if (!shouldClearSavedTour)
        {
            return;
        }

        await _tourStateService.ClearActiveTourAsync();
        ActiveTour = null;
    }

    public Task ResumeActiveTourAsync()
    {
        if (ActiveTour is null)
        {
            return Task.CompletedTask;
        }

        PreviewTour = null;
        IsActiveTourVisible = true;
        return Task.CompletedTask;
    }

    private async Task OpenMyTourAsync()
    {
        if (Shell.Current is null)
        {
            return;
        }

        await Shell.Current.GoToAsync(AppRoutes.MyTour);
    }

    private async Task MarkActiveTourPoiVisitedAsync(string poiId)
    {
        if (ActiveTour is null ||
            string.IsNullOrWhiteSpace(ActiveTour.Id) ||
            !ActiveTour.Stops.Any(item => string.Equals(item.PoiId, poiId, StringComparison.OrdinalIgnoreCase)))
        {
            return;
        }

        var activeSession = await _tourStateService.MarkCheckpointVisitedAsync(poiId);
        if (activeSession is null)
        {
            return;
        }

        var refreshedActiveTour = await LoadTourPlanOrNullAsync(activeSession.TourId, activeSession.CompletedPoiIds);
        if (refreshedActiveTour is not null)
        {
            if (IsTourCompleted(refreshedActiveTour))
            {
                await CompleteActiveTourAsync(refreshedActiveTour.Id, stopAudio: false);
                return;
            }

            ActiveTour = refreshedActiveTour;
        }
    }

    private async Task CompleteActiveTourAsync(string? tourId, bool stopAudio)
    {
        if (string.IsNullOrWhiteSpace(tourId))
        {
            return;
        }

        PreviewTour = null;
        IsActiveTourVisible = false;

        if (IsCurrentRouteContext(SimulationContextKind.Tour, tourId))
        {
            await ClearActiveRouteAsync(stopAudio);
        }

        await _tourStateService.ClearActiveTourAsync();
        ActiveTour = null;
    }

    private static bool IsTourCompleted(TourPlan? tour)
        => tour is not null &&
           tour.Checkpoints.Count > 0 &&
           tour.Checkpoints.All(item => item.IsCompleted);

    private List<PoiLocation> GetVisibleTourPoiSequence()
    {
        if (VisibleTour is null)
        {
            return Pois.ToList();
        }

        var poiLookup = Pois.ToDictionary(item => item.Id, StringComparer.OrdinalIgnoreCase);
        return VisibleTour.Stops
            .Select(stop => poiLookup.TryGetValue(stop.PoiId, out var poi) ? poi : null)
            .Where(poi => poi is not null)
            .Cast<PoiLocation>()
            .ToList();
    }

    public TourMapOverlayState? GetCurrentTourMapState()
    {
        if (VisibleTour is null)
        {
            return null;
        }

        var poiLookup = Pois.ToDictionary(item => item.Id, StringComparer.OrdinalIgnoreCase);
        var checkpoints = VisibleTour.Checkpoints
            .Where(item => poiLookup.ContainsKey(item.PoiId))
            .Select(item =>
            {
                var poi = poiLookup[item.PoiId];
                return new TourMapCheckpoint
                {
                    PoiId = item.PoiId,
                    Title = item.Title,
                    Order = item.Order,
                    Latitude = poi.Latitude,
                    Longitude = poi.Longitude,
                    IsCompleted = item.IsCompleted
                };
            })
            .ToList();

        return new TourMapOverlayState
        {
            Id = VisibleTour.Id,
            Mode = IsTourActiveMode ? "active" : "preview",
            Name = VisibleTour.Title,
            StopPoiIds = VisibleTour.Stops.Select(item => item.PoiId).ToList(),
            StopOrderByPoiId = VisibleTour.Checkpoints.ToDictionary(
                item => item.PoiId,
                item => item.Order,
                StringComparer.OrdinalIgnoreCase),
            Checkpoints = checkpoints
        };
    }

    private void RefreshTourBindings()
    {
        var nextToken = VisibleTour is null
            ? "free"
            : $"{TourMode}:{VisibleTour.Id}";

        if (!string.Equals(_visibleTourToken, nextToken, StringComparison.Ordinal))
        {
            _visibleTourToken = nextToken;
            TourMapVersion++;
        }

        SynchronizeTourCatalogStates();

        OnPropertyChanged(nameof(VisibleTour));
        OnPropertyChanged(nameof(TourMode));
        OnPropertyChanged(nameof(HasVisibleTour));
        OnPropertyChanged(nameof(HasActiveSavedTour));
        OnPropertyChanged(nameof(IsTourPreviewMode));
        OnPropertyChanged(nameof(IsTourActiveMode));
        OnPropertyChanged(nameof(IsTourPanelVisible));
        OnPropertyChanged(nameof(DiscoverTourSubtitleText));
        OnPropertyChanged(nameof(TourBannerText));
        OnPropertyChanged(nameof(TourBannerMetaText));
        OnPropertyChanged(nameof(TourPanelTitleText));
        OnPropertyChanged(nameof(TourPanelDescriptionText));
        OnPropertyChanged(nameof(TourPanelModeText));
        OnPropertyChanged(nameof(TourPanelPrimaryActionText));
        OnPropertyChanged(nameof(IsFloatingPoiActionVisible));
        RefreshSimulationBindings();
    }

    private void SynchronizeTourCatalogStates()
    {
        foreach (var tour in AvailableTours)
        {
            tour.IsPreviewing = PreviewTour is not null &&
                string.Equals(tour.Id, PreviewTour.Id, StringComparison.OrdinalIgnoreCase);
            tour.IsActiveSession = ActiveTour is not null &&
                string.Equals(tour.Id, ActiveTour.Id, StringComparison.OrdinalIgnoreCase);
            tour.StatusText = tour.IsPreviewing
                ? LanguageService.GetText("tour_status_preview")
                : tour.IsActiveSession
                    ? LanguageService.GetText(IsTourActiveMode ? "tour_status_active_visible" : "tour_status_active_saved")
                    : string.Empty;
        }
    }

    private string BuildTourMetaText(TourPlan tour)
    {
        var segments = new List<string>();
        if (!string.IsNullOrWhiteSpace(tour.DurationText))
        {
            segments.Add(tour.DurationText);
        }

        if (tour.Stops.Count > 0)
        {
            segments.Add(string.Format(
                LanguageService.CurrentCulture,
                LanguageService.GetText("tour_meta_stops"),
                tour.Stops.Count));
        }

        if (!string.IsNullOrWhiteSpace(tour.ProgressText) && IsTourActiveMode)
        {
            segments.Add(tour.ProgressText);
        }

        return string.Join(" • ", segments);
    }
}
