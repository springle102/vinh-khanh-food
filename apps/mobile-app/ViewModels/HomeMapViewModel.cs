using System.Collections.ObjectModel;
using System.Globalization;
using Microsoft.Maui.ApplicationModel;
using VinhKhanh.MobileApp.Helpers;
using VinhKhanh.MobileApp.Models;
using VinhKhanh.MobileApp.Services;

namespace VinhKhanh.MobileApp.ViewModels;

public sealed partial class HomeMapViewModel : LocalizedViewModelBase
{
    private readonly IFoodStreetDataService _dataService;
    private readonly IPoiNarrationService _poiNarrationService;
    private readonly IAutoNarrationService _autoNarrationService;
    private readonly SemaphoreSlim _locationUpdateLock = new(1, 1);

    private string _searchText = string.Empty;
    private PoiLocation? _selectedPoi;
    private PoiExperienceDetail? _selectedPoiDetail;
    private PoiProximitySnapshot? _userLocationSnapshot;
    private bool _isBottomSheetVisible;
    private bool _isPoiDetailLoading;
    private bool _isLocationTrackingActive;
    private string? _currentActivePoiId;
    private string? _lastTriggeredPoiId;
    private bool _isAutoNarrationPlaying;
    private volatile bool _isNarrationContextActive = true;
    private int _mapDataVersion;
    private int _userLocationVersion;
    private int _syncRefreshInProgress;
    private long _detailRequestVersion;

    public HomeMapViewModel(
        IFoodStreetDataService dataService,
        IAppLanguageService languageService,
        IPoiNarrationService poiNarrationService,
        IRouteService routeService,
        IRoutePoiFilterService routePoiFilterService,
        ISimulationService simulationService,
        IAutoNarrationService autoNarrationService,
        ITourStateService tourStateService)
        : base(languageService)
    {
        _dataService = dataService;
        _poiNarrationService = poiNarrationService;
        _routeService = routeService;
        _routePoiFilterService = routePoiFilterService;
        _simulationService = simulationService;
        _autoNarrationService = autoNarrationService;
        _tourStateService = tourStateService;
        InitializeTourCommands();
    }

    public ObservableCollection<PoiLocation> Pois { get; } = [];
    public ObservableCollection<PoiLocation> SearchResults { get; } = [];
    public string SearchText
    {
        get => _searchText;
        set
        {
            if (SetProperty(ref _searchText, value))
            {
                ApplySearch();
            }
        }
    }

    public PoiLocation? SelectedPoi
    {
        get => _selectedPoi;
        private set
        {
            if (SetProperty(ref _selectedPoi, value))
            {
                RefreshSelectedPoiBindings();
                OnPropertyChanged(nameof(CanSimulateNearSelectedPoi));
                RefreshSimulationBindings();
            }
        }
    }

    public PoiExperienceDetail? SelectedPoiDetail
    {
        get => _selectedPoiDetail;
        private set
        {
            if (SetProperty(ref _selectedPoiDetail, value))
            {
                RefreshDetailBindings();
            }
        }
    }

    public bool IsBottomSheetVisible
    {
        get => _isBottomSheetVisible;
        set
        {
            if (SetProperty(ref _isBottomSheetVisible, value))
            {
                OnPropertyChanged(nameof(HasVisibleBottomSheet));
                OnPropertyChanged(nameof(IsFloatingPoiActionVisible));
                OnPropertyChanged(nameof(IsTourPanelVisible));
            }
        }
    }

    public bool IsPoiDetailLoading
    {
        get => _isPoiDetailLoading;
        set
        {
            if (SetProperty(ref _isPoiDetailLoading, value))
            {
                OnPropertyChanged(nameof(HasDetailContent));
            }
        }
    }

    public int MapDataVersion
    {
        get => _mapDataVersion;
        private set => SetProperty(ref _mapDataVersion, value);
    }

    public int UserLocationVersion
    {
        get => _userLocationVersion;
        private set => SetProperty(ref _userLocationVersion, value);
    }

    public string? CurrentActivePoiId
    {
        get => _currentActivePoiId;
        private set => SetProperty(ref _currentActivePoiId, value);
    }

    public string? LastTriggeredPoiId
    {
        get => _lastTriggeredPoiId;
        private set => SetProperty(ref _lastTriggeredPoiId, value);
    }

    public bool IsAutoNarrationPlaying
    {
        get => _isAutoNarrationPlaying;
        private set => SetProperty(ref _isAutoNarrationPlaying, value);
    }

    public bool HasVisibleBottomSheet => IsBottomSheetVisible && (SelectedPoi is not null || SelectedPoiDetail is not null || IsPoiDetailLoading);
    public bool HasDetailContent => SelectedPoi is not null || SelectedPoiDetail is not null || IsPoiDetailLoading;
    public bool HasSelectedPoiDetail => SelectedPoiDetail is not null;
    public bool IsFeaturedPoi => SelectedPoiDetail?.IsFeatured ?? SelectedPoi?.IsFeatured ?? false;
    public bool HasRatingSummary => (SelectedPoiDetail?.ReviewCount ?? 0) > 0 && (SelectedPoiDetail?.Rating ?? 0) > 0;
    public bool HasReviewSummary => (SelectedPoiDetail?.ReviewCount ?? 0) > 0;
    public bool HasSelectedPoiPriceRange => !string.IsNullOrWhiteSpace(SelectedPoiPriceRange);
    public bool HasSelectedPoiOpeningHours => !string.IsNullOrWhiteSpace(SelectedPoiOpeningHours);
    public bool HasSelectedPoiTags => SelectedPoiTags.Count > 0;
    public bool HasSelectedPoiFoodItems => SelectedPoiFoodItems.Count > 0;
    public bool HasSelectedPoiPromotions => SelectedPoiPromotions.Count > 0;
    public bool ShowSelectedPoiFoodItemsEmptyState => HasSelectedPoiDetail && !IsPoiDetailLoading && !HasSelectedPoiFoodItems;
    public bool ShowSelectedPoiPromotionsEmptyState => HasSelectedPoiDetail && !IsPoiDetailLoading && !HasSelectedPoiPromotions;
    public bool IsFloatingPoiActionVisible => !HasVisibleBottomSheet && !HasVisibleTour;
    public bool CanSimulateNearSelectedPoi => SelectedPoi is not null;
    public bool IsAutoNarrationEnabled => _autoNarrationService.IsEnabled;
    public bool IsAutoNarrationDevToolsVisible
    {
        get
        {
#if DEBUG
            return true;
#else
            return false;
#endif
        }
    }

    public string SearchPlaceholderText => LanguageService.GetText("home_search_placeholder");
    public string PoiChipText => LanguageService.GetText("home_poi_chip");
    public string PoiActionText => LanguageService.GetText("bottom_poi");
    public string TourEntryActionText => LanguageService.GetText("tour_map_entry_action");
    public string ListenActionText => LanguageService.GetText("poi_detail_listen");
    public string DirectionsActionText => LanguageService.GetText("poi_detail_directions");
    public string DetailLoadingText => LanguageService.GetText("poi_detail_loading");
    public string FeaturedBadgeText => LanguageService.GetText("poi_detail_featured");
    public string NoSelectionText => LanguageService.GetText("poi_detail_no_selection");
    public string AddressLabelText => LanguageService.GetText("poi_detail_address");
    public string PriceRangeLabelText => LanguageService.GetText("poi_detail_price_range");
    public string FoodItemsLabelText => LanguageService.GetText("poi_detail_food_items");
    public string PromotionsLabelText => LanguageService.GetText("poi_detail_promotions");
    public string OpeningHoursLabelText => LanguageService.GetText("poi_detail_opening_hours");
    public string TagsLabelText => LanguageService.GetText("poi_detail_tags");
    public string NoFoodItemsText => LanguageService.GetText("poi_detail_no_food_items");
    public string NoPromotionsText => LanguageService.GetText("poi_detail_no_promotions");
    public string AutoNarrationDevTitleText => LanguageService.GetText("auto_narration_dev_title");
    public string AutoNarrationDevDescriptionText => LanguageService.GetText("auto_narration_dev_description");
    public string SimulateNearSelectedPoiText => LanguageService.GetText("auto_narration_dev_action");

    public string SelectedPoiTitle
        => FirstNonEmpty(
            LocalizedTextHelper.GetLocalizedText(SelectedPoiDetail?.Name, LanguageService.CurrentLanguage),
            SelectedPoi?.Title,
            NoSelectionText);

    public string SelectedPoiDescription
        => FirstNonEmpty(
            LocalizedTextHelper.GetLocalizedText(SelectedPoiDetail?.Description, LanguageService.CurrentLanguage),
            LocalizedTextHelper.GetLocalizedText(SelectedPoiDetail?.Summary, LanguageService.CurrentLanguage),
            SelectedPoi?.ShortDescription,
            LanguageService.GetText("home_default_description"));

    public string SelectedPoiSummary
        => FirstNonEmpty(
            LocalizedTextHelper.GetLocalizedText(SelectedPoiDetail?.Summary, LanguageService.CurrentLanguage),
            SelectedPoi?.ShortDescription,
            LanguageService.GetText("home_default_description"));

    public string SelectedPoiAddress
        => FirstNonEmpty(
            SelectedPoiDetail?.Address,
            SelectedPoi?.Address,
            LanguageService.GetText("home_default_address"));

    public string SelectedPoiCategory
        => FirstNonEmpty(
            SelectedPoiDetail?.Category,
            SelectedPoi?.Category,
            LanguageService.GetText("home_poi_chip"));

    public string SelectedPoiPriceRange
        => FirstNonEmpty(
            SelectedPoiDetail?.PriceRange,
            SelectedPoi?.PriceRange);

    public string SelectedPoiOpeningHours
        => FirstNonEmpty(SelectedPoiDetail?.OpeningHours);

    public string SelectedPoiImageUrl => DetailImages.FirstOrDefault() ?? _dataService.GetBackdropImageUrl();

    public string SelectedPoiRatingText
        => !HasRatingSummary
            ? string.Empty
            : SelectedPoiDetail!.Rating.ToString("0.0", CultureInfo.InvariantCulture);

    public string SelectedPoiReviewText
        => !HasReviewSummary
            ? string.Empty
            : $"{SelectedPoiDetail!.ReviewCount} {LanguageService.GetText("poi_detail_reviews")}";

    public IReadOnlyList<string> DetailImages
    {
        get
        {
            if (SelectedPoiDetail?.Images.Count > 0)
            {
                return SelectedPoiDetail.Images;
            }

            if (!string.IsNullOrWhiteSpace(SelectedPoi?.ThumbnailUrl))
            {
                return [SelectedPoi.ThumbnailUrl];
            }

            return [_dataService.GetBackdropImageUrl()];
        }
    }

    public IReadOnlyList<string> SelectedPoiTags
        => SelectedPoiDetail?.Tags
            .Where(item => !string.IsNullOrWhiteSpace(item))
            .ToList()
            ?? [];

    public IReadOnlyList<PoiFoodItemDetail> SelectedPoiFoodItems
        => SelectedPoiDetail?.FoodItems ?? [];

    public IReadOnlyList<PoiPromotionDetail> SelectedPoiPromotions
        => SelectedPoiDetail?.Promotions ?? [];

    public MapUserLocationState GetMapUserLocationState()
    {
        if (_userLocationSnapshot is null)
        {
            return new MapUserLocationState();
        }

        var activePoiTitle = _userLocationSnapshot.ActivePoi?.Title;
        var nearestPoiTitle = _userLocationSnapshot.NearestPoi?.Title;
        return new MapUserLocationState
        {
            Latitude = _userLocationSnapshot.Location.Latitude,
            Longitude = _userLocationSnapshot.Location.Longitude,
            ActivePoiId = CurrentActivePoiId,
            PopupTitle = LanguageService.GetText("user_location_title"),
            CoordinatesLabel = LanguageService.GetText("user_location_coordinates_label"),
            CoordinatesText = FormatCoordinates(
                _userLocationSnapshot.Location.Latitude,
                _userLocationSnapshot.Location.Longitude),
            StatusLabel = LanguageService.GetText("user_location_status_label"),
            StatusText = string.IsNullOrWhiteSpace(activePoiTitle)
                ? LanguageService.GetText("user_location_status_idle")
                : string.Format(
                    LanguageService.CurrentCulture,
                    LanguageService.GetText("user_location_status_near_poi"),
                    activePoiTitle),
            NearestPoiLabel = LanguageService.GetText("user_location_nearest_poi_label"),
            NearestPoiText = FirstNonEmpty(
                nearestPoiTitle,
                LanguageService.GetText("user_location_no_nearest_poi")),
            NearestDistanceLabel = LanguageService.GetText("user_location_nearest_distance_label"),
            NearestDistanceText = _userLocationSnapshot.NearestPoiDistanceMeters is double distanceMeters
                ? FormatDistanceMeters(distanceMeters)
                : LanguageService.GetText("user_location_distance_unknown"),
            SourceLabel = LanguageService.GetText("user_location_source_label"),
            SourceText = LanguageService.GetText(
                _simulationMode == SimulationMode.Auto && _simulationRunState == SimulationRunState.Running
                    ? "user_location_source_auto"
                    : "user_location_source_manual")
        };
    }

    public AsyncCommand<PoiLocation> SelectPoiCommand => new(poi => SelectPoiAsync(poi, autoPlayNarration: true));
    public AsyncCommand<string> LoadPoiDetailCommand => new(poiId => LoadPoiDetailByIdAsync(poiId, autoPlayNarration: true));
    public AsyncCommand SelectNextPoiCommand => new(SelectNextPoiAsync);
    public AsyncCommand SimulateNearSelectedPoiCommand => new(SimulateNearSelectedPoiAsync);
    public AsyncCommand CloseBottomSheetCommand => new(CloseBottomSheetAsync);
    public AsyncCommand PlayNarrationCommand => new(PlayNarrationAsync);
    public AsyncCommand OpenDirectionsCommand => new(BuildRouteAsync);

    public void ActivateNarrationContext()
        => _isNarrationContextActive = true;

    public async Task SuspendNarrationAsync()
    {
        _isNarrationContextActive = false;
        IsAutoNarrationPlaying = false;
        await _poiNarrationService.StopAsync();
    }

    public async Task StopNarrationAsync()
    {
        IsAutoNarrationPlaying = false;
        await _poiNarrationService.StopAsync();
    }

    public Task LoadAsync()
        => LoadAsync(autoPlayNarrationForSelection: false);

    public async Task LoadAsync(bool autoPlayNarrationForSelection)
    {
        await _dataService.RefreshDataIfChangedAsync();
        await ReloadCurrentStateAsync(autoPlayNarrationForSelection);
    }

    public async Task RefreshIfNeededAsync()
    {
        if (Interlocked.Exchange(ref _syncRefreshInProgress, 1) == 1)
        {
            return;
        }

        try
        {
            if (await _dataService.RefreshDataIfChangedAsync())
            {
                await ReloadCurrentStateAsync(autoPlayNarrationForSelection: false);
            }
        }
        finally
        {
            Interlocked.Exchange(ref _syncRefreshInProgress, 0);
        }
    }

    private async Task ReloadCurrentStateAsync(bool autoPlayNarrationForSelection)
    {
        var currentPoiId = SelectedPoi?.Id;

        Pois.ReplaceRange(await _dataService.GetPoisAsync());
        ApplySearch();
        await ReloadTourExperienceAsync();

        SelectedPoi = currentPoiId is null
            ? null
            : Pois.FirstOrDefault(item => string.Equals(item.Id, currentPoiId, StringComparison.OrdinalIgnoreCase));

        MapDataVersion++;

        if (SelectedPoi is not null && (IsBottomSheetVisible || SelectedPoiDetail is not null))
        {
            await LoadPoiDetailCoreAsync(SelectedPoi, IsBottomSheetVisible, autoPlayNarrationForSelection);
        }
        else
        {
            RefreshLocalizedTexts();
        }

        await EnsureSimulationInitializedAsync();
        await RefreshCurrentLocationAsync();
    }

    public async Task StartLocationTrackingAsync()
    {
        if (_isLocationTrackingActive)
        {
            await RefreshCurrentLocationAsync();
            return;
        }

        _simulationService.LocationChanged += OnUserLocationChanged;
        _simulationService.StateChanged += OnSimulationStateChanged;
        _isLocationTrackingActive = true;
        await EnsureSimulationInitializedAsync();
        await RefreshCurrentLocationAsync();
    }

    public async Task StopLocationTrackingAsync()
    {
        if (_isLocationTrackingActive)
        {
            _simulationService.LocationChanged -= OnUserLocationChanged;
            _simulationService.StateChanged -= OnSimulationStateChanged;
            _isLocationTrackingActive = false;
        }

        await _simulationService.PauseAsync();
    }

    public async Task SelectPoiByIdAsync(string poiId)
        => await SelectPoiByIdAsync(poiId, autoPlayNarration: true);

    public async Task SelectPoiByIdAsync(string poiId, bool autoPlayNarration)
    {
        if (string.IsNullOrWhiteSpace(poiId))
        {
            return;
        }

        if (Pois.Count == 0)
        {
            await LoadAsync();
        }

        await LoadPoiDetailByIdAsync(poiId, autoPlayNarration);
    }

    public Task SetMockLocationAsync(double latitude, double longitude)
        => _simulationService.SetCurrentLocationAsync(latitude, longitude);

    private async Task SelectPoiAsync(PoiLocation? poi, bool autoPlayNarration = true)
    {
        if (poi is null)
        {
            return;
        }

        await LoadPoiDetailCoreAsync(poi, true, autoPlayNarration);
    }

    private async Task LoadPoiDetailByIdAsync(string? poiId, bool autoPlayNarration = true)
    {
        if (string.IsNullOrWhiteSpace(poiId))
        {
            return;
        }

        var poi = Pois.FirstOrDefault(item => string.Equals(item.Id, poiId, StringComparison.OrdinalIgnoreCase));
        if (poi is null)
        {
            return;
        }

        await LoadPoiDetailCoreAsync(poi, true, autoPlayNarration);
    }

    private async Task LoadPoiDetailCoreAsync(
        PoiLocation poi,
        bool showBottomSheet,
        bool autoPlayNarration = false,
        string usageSource = "map")
    {
        var requestVersion = Interlocked.Increment(ref _detailRequestVersion);
        await StopNarrationAsync();

        SelectedPoi = poi;
        IsBottomSheetVisible = showBottomSheet;
        IsPoiDetailLoading = true;

        try
        {
            PoiExperienceDetail detail;
            try
            {
                detail = await _dataService.GetPoiDetailAsync(poi.Id) ?? CreateInlineFallbackDetail(poi);
            }
            catch
            {
                detail = CreateInlineFallbackDetail(poi);
            }

            if (requestVersion != _detailRequestVersion)
            {
                return;
            }

            SelectedPoiDetail = detail;
            await MarkActiveTourPoiVisitedAsync(poi.Id);

            try
            {
                await _dataService.TrackPoiViewAsync(poi.Id, LanguageService.CurrentLanguage, usageSource);
            }
            catch
            {
                // Analytics must never block POI detail rendering.
            }

            if (autoPlayNarration)
            {
                QueueAutoPlayNarration(detail, requestVersion);
            }
        }
        finally
        {
            if (requestVersion == _detailRequestVersion)
            {
                IsPoiDetailLoading = false;
                RefreshLocalizedTexts();
            }
        }
    }

    private async Task RefreshCurrentLocationAsync()
    {
        var currentLocation = _simulationService.CurrentLocation;
        if (currentLocation is null)
        {
            _userLocationSnapshot = null;
            CurrentActivePoiId = null;
            UserLocationVersion++;
            return;
        }

        await ApplyLocationUpdateAsync(currentLocation, isMockLocation: true);
    }

    private void OnUserLocationChanged(object? sender, UserLocationChangedEventArgs e)
        => MainThread.BeginInvokeOnMainThread(() => _ = ApplyLocationUpdateAsync(e.Location, e.IsMock));

    private async Task ApplyLocationUpdateAsync(UserLocationPoint location, bool isMockLocation)
    {
        await _locationUpdateLock.WaitAsync();
        try
        {
            if (Pois.Count == 0)
            {
                _userLocationSnapshot = new PoiProximitySnapshot
                {
                    Location = location
                };
                CurrentActivePoiId = null;
                UserLocationVersion++;
                return;
            }

            var narrationResult = await _autoNarrationService.ProcessLocationAsync(location, Pois, isMockLocation);
            ApplyUserLocationSnapshot(narrationResult.Snapshot, isMockLocation);

            if (!narrationResult.PlaybackStarted ||
                narrationResult.TriggeredPoi is null ||
                narrationResult.TriggeredDetail is null)
            {
                return;
            }

            LastTriggeredPoiId = narrationResult.TriggeredPoi.Id;
            await PresentAutoNarratedPoiAsync(
                narrationResult.TriggeredPoi,
                narrationResult.TriggeredDetail,
                ResolveAutoNarrationUsageSource());
        }
        finally
        {
            _locationUpdateLock.Release();
        }
    }

    private void ApplyUserLocationSnapshot(PoiProximitySnapshot snapshot, bool isMockLocation)
    {
        _userLocationSnapshot = snapshot;
        CurrentActivePoiId = snapshot.ActivePoi?.Id;
        UserLocationVersion++;
    }

    private async Task PresentAutoNarratedPoiAsync(PoiLocation poi, PoiExperienceDetail detail, string usageSource)
    {
        SelectedPoi = poi;
        SelectedPoiDetail = detail;
        IsPoiDetailLoading = false;
        IsBottomSheetVisible = true;

        await MarkActiveTourPoiVisitedAsync(poi.Id);

        try
        {
            await _dataService.TrackPoiViewAsync(poi.Id, LanguageService.CurrentLanguage, usageSource);
        }
        catch
        {
            // Analytics must never block POI detail rendering.
        }

        RefreshLocalizedTexts();
    }

    private async Task SelectNextPoiAsync()
    {
        var poiSequence = GetVisibleTourPoiSequence();
        if (poiSequence.Count == 0)
        {
            return;
        }

        var currentIndex = SelectedPoi is null ? -1 : poiSequence.IndexOf(SelectedPoi);
        var nextPoi = poiSequence[(currentIndex + 1 + poiSequence.Count) % poiSequence.Count];
        await SelectPoiAsync(nextPoi, autoPlayNarration: true);
    }

    private async Task SimulateNearSelectedPoiAsync()
    {
        if (SelectedPoi is null)
        {
            return;
        }

        var narrationResult = await _autoNarrationService.TriggerPoiAsync(SelectedPoi);
        await _simulationService.SetCurrentLocationAsync(SelectedPoi.Latitude, SelectedPoi.Longitude);

        if (!narrationResult.PlaybackStarted ||
            narrationResult.TriggeredPoi is null ||
            narrationResult.TriggeredDetail is null)
        {
            return;
        }

        ApplyUserLocationSnapshot(narrationResult.Snapshot, isMockLocation: true);
        LastTriggeredPoiId = narrationResult.TriggeredPoi.Id;
        await PresentAutoNarratedPoiAsync(
            narrationResult.TriggeredPoi,
            narrationResult.TriggeredDetail,
            usageSource: "mock_location_test");
    }

    private async Task CloseBottomSheetAsync()
    {
        IsBottomSheetVisible = false;
        SelectedPoi = null;
        SelectedPoiDetail = null;
        await StopNarrationAsync();
    }

    private async Task PlayNarrationAsync()
    {
        if (SelectedPoiDetail is null || !_isNarrationContextActive)
        {
            return;
        }

        await _poiNarrationService.PlayAsync(SelectedPoiDetail, LanguageService.CurrentLanguage);
    }

    private string ResolveAutoNarrationUsageSource()
    {
        if (_activeRoutePlan?.ContextKind == SimulationContextKind.Tour)
        {
            return _simulationMode == SimulationMode.Auto
                ? "tour_route_simulation_auto"
                : "tour_route_simulation_manual";
        }

        return _simulationMode == SimulationMode.Auto
            ? "route_simulation_auto"
            : "manual_simulation_route";
    }

    private void QueueAutoPlayNarration(PoiExperienceDetail detail, long requestVersion)
    {
        if (!_isNarrationContextActive)
        {
            return;
        }

        MainThread.BeginInvokeOnMainThread(() => _ = AutoPlayNarrationAsync(detail, requestVersion));
    }

    private async Task AutoPlayNarrationAsync(PoiExperienceDetail detail, long requestVersion)
    {
        if (requestVersion != _detailRequestVersion ||
            !_isNarrationContextActive ||
            SelectedPoiDetail is null ||
            !string.Equals(SelectedPoiDetail.Id, detail.Id, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        try
        {
            await _poiNarrationService.PlayAsync(detail, LanguageService.CurrentLanguage);
        }
        catch
        {
            // Best effort auto-play. Manual replay remains available.
        }
    }

    private void ApplySearch()
    {
        if (string.IsNullOrWhiteSpace(SearchText))
        {
            SearchResults.ReplaceRange(Pois);
            return;
        }

        var matches = Pois.Where(item =>
                item.Title.Contains(SearchText, StringComparison.OrdinalIgnoreCase) ||
                item.Category.Contains(SearchText, StringComparison.OrdinalIgnoreCase) ||
                item.ShortDescription.Contains(SearchText, StringComparison.OrdinalIgnoreCase))
            .ToList();

        SearchResults.ReplaceRange(matches);
        if (matches.Count > 0)
        {
            SelectedPoi = matches[0];
        }
    }

    private void RefreshLocalizedTexts()
        => RefreshLocalizedBindings();

    private void RefreshSelectedPoiBindings()
        => RefreshLocalizedBindings();

    private void RefreshDetailBindings()
        => RefreshLocalizedBindings();

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
            Rating = 0,
            ReviewCount = 0,
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

        var normalizedLanguage = AppLanguage.NormalizeCode(LanguageService.CurrentLanguage);
        target.Set(normalizedLanguage, value);

        var separatorIndex = normalizedLanguage.IndexOf('-');
        if (separatorIndex > 0)
        {
            target.Set(normalizedLanguage[..separatorIndex], value);
        }
    }

    private string FormatCoordinates(double latitude, double longitude)
        => string.Format(
            CultureInfo.InvariantCulture,
            "{0:F6}, {1:F6}",
            latitude,
            longitude);

    private string FormatDistanceMeters(double distanceMeters)
    {
        if (distanceMeters >= 1000)
        {
            return string.Format(
                LanguageService.CurrentCulture,
                "{0:0.0} km",
                distanceMeters / 1000d);
        }

        return string.Format(
            LanguageService.CurrentCulture,
            "{0:0} m",
            Math.Max(0, distanceMeters));
    }

    private static string FirstNonEmpty(params string?[] values)
        => values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))?.Trim() ?? string.Empty;

    protected override async Task ReloadLocalizedStateAsync()
    {
        try
        {
            await StopNarrationAsync();
            SelectedPoiDetail = null;
            if (SelectedPoi is not null && IsBottomSheetVisible)
            {
                IsPoiDetailLoading = true;
            }

            await LoadAsync(autoPlayNarrationForSelection: _isNarrationContextActive && IsBottomSheetVisible);
        }
        catch
        {
            if (SelectedPoi is not null)
            {
                SelectedPoiDetail = CreateInlineFallbackDetail(SelectedPoi);
            }
        }
    }
}
