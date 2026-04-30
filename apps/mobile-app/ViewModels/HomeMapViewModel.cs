using System.Collections.ObjectModel;
using System.Globalization;
using Microsoft.Extensions.Logging;
using Microsoft.Maui.ApplicationModel;
using VinhKhanh.MobileApp.Helpers;
using VinhKhanh.MobileApp.Models;
using VinhKhanh.MobileApp.Services;

namespace VinhKhanh.MobileApp.ViewModels;

public sealed partial class HomeMapViewModel : LocalizedViewModelBase
{
    private readonly IFoodStreetDataService _dataService;
    private readonly IMobileAnalyticsService _analyticsService;
    private readonly IPoiAudioPlaybackService _poiAudioPlaybackService;
    private readonly IAutoNarrationService _autoNarrationService;
    private readonly ILogger<HomeMapViewModel> _logger;
    private readonly HashSet<string> _preloadedAudioKeys = new(StringComparer.OrdinalIgnoreCase);
    private readonly SemaphoreSlim _locationUpdateLock = new(1, 1);
    private readonly SemaphoreSlim _stateReloadLock = new(1, 1);

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
    private string _narrationMessage = string.Empty;
    private PoiAudioPlaybackSnapshot _playbackSnapshot = PoiAudioPlaybackSnapshot.Idle;
    private volatile bool _isNarrationContextActive = true;
    private int _mapDataVersion;
    private int _mapContentVersion;
    private int _userLocationVersion;
    private int _syncRefreshInProgress;
    private long _detailRequestVersion;
    private string? _pendingMapCenterPoiId;
    private CancellationTokenSource? _detailRequestCancellation;

    public HomeMapViewModel(
        IFoodStreetDataService dataService,
        IMobileAnalyticsService analyticsService,
        IAppLanguageService languageService,
        IPoiAudioPlaybackService poiAudioPlaybackService,
        IRouteService routeService,
        IRoutePoiFilterService routePoiFilterService,
        ISimulationService simulationService,
        IAutoNarrationService autoNarrationService,
        ITourStateService tourStateService,
        ILogger<HomeMapViewModel> logger)
        : base(languageService)
    {
        _dataService = dataService;
        _analyticsService = analyticsService;
        _poiAudioPlaybackService = poiAudioPlaybackService;
        _routeService = routeService;
        _routePoiFilterService = routePoiFilterService;
        _simulationService = simulationService;
        _autoNarrationService = autoNarrationService;
        _tourStateService = tourStateService;
        _logger = logger;
        _poiAudioPlaybackService.PlaybackStateChanged += OnPlaybackStateChanged;
        PlayNarrationCommand = new(PlayNarrationAsync, () => CanToggleNarration);
        ApplyPlaybackSnapshot(_poiAudioPlaybackService.Snapshot);
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
                RefreshNarrationBindings();
            }
        }
    }

    public int MapDataVersion
    {
        get => _mapDataVersion;
        private set => SetProperty(ref _mapDataVersion, value);
    }

    public int MapContentVersion
    {
        get => _mapContentVersion;
        private set => SetProperty(ref _mapContentVersion, value);
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

    public string NarrationMessage
    {
        get => _narrationMessage;
        private set
        {
            if (SetProperty(ref _narrationMessage, value))
            {
                OnPropertyChanged(nameof(HasNarrationMessage));
            }
        }
    }

    public string CurrentLanguageCode => LanguageService.CurrentLanguage;
    public bool HasVisibleBottomSheet => IsBottomSheetVisible && (SelectedPoi is not null || SelectedPoiDetail is not null || IsPoiDetailLoading);
    public bool HasDetailContent => SelectedPoi is not null || SelectedPoiDetail is not null || IsPoiDetailLoading;
    public bool HasSelectedPoiDetail => SelectedPoiDetail is not null;
    public bool HasNarrationMessage => !string.IsNullOrWhiteSpace(NarrationMessage);
    public bool IsFeaturedPoi => SelectedPoiDetail?.IsFeatured ?? SelectedPoi?.IsFeatured ?? false;
    public bool HasSelectedPoiPriceRange => !string.IsNullOrWhiteSpace(SelectedPoiPriceRange);
    public bool HasSelectedPoiOpeningHours => !string.IsNullOrWhiteSpace(SelectedPoiOpeningHours);
    public bool HasSelectedPoiTags => SelectedPoiTags.Count > 0;
    public bool HasSelectedPoiFoodItems => SelectedPoiFoodItems.Count > 0;
    public bool HasSelectedPoiPromotions => SelectedPoiPromotions.Count > 0;
    public bool ShowSelectedPoiFoodItemsEmptyState => HasSelectedPoiDetail && !IsPoiDetailLoading && !HasSelectedPoiFoodItems;
    public bool ShowSelectedPoiPromotionsEmptyState => HasSelectedPoiDetail && !IsPoiDetailLoading && !HasSelectedPoiPromotions;
    public bool IsFloatingPoiActionVisible => !HasVisibleBottomSheet && !HasVisibleTour;
    public bool IsAutoNarrationEnabled => _autoNarrationService.IsEnabled;

    public string SearchPlaceholderText => LanguageService.GetText("home_search_placeholder");
    public string PoiChipText => LanguageService.GetText("home_poi_chip");
    public string PoiActionText => LanguageService.GetText("bottom_poi");
    public string TourEntryActionText => LanguageService.GetText("tour_map_entry_action");
    public string ListenActionText
        => IsSelectedPoiNarrationLoading
            ? LanguageService.GetText("poi_detail_narration_loading")
            : IsSelectedPoiNarrationPlaying
                ? LanguageService.GetText("poi_detail_pause_narration")
                : IsSelectedPoiNarrationPaused
                    ? LanguageService.GetText("poi_detail_resume_narration")
                : LanguageService.GetText("poi_detail_listen");
    public string ListenActionIconText
        => IsSelectedPoiNarrationLoading
            ? "..."
            : IsSelectedPoiNarrationPlaying
                ? "||"
                : ">";
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
    public bool IsSelectedPoiNarrationLoading => MatchesSelectedPoiNarration(_playbackSnapshot, PoiAudioPlaybackStatus.Loading);
    public bool IsSelectedPoiNarrationPlaying => MatchesSelectedPoiNarration(_playbackSnapshot, PoiAudioPlaybackStatus.Playing);
    public bool IsSelectedPoiNarrationPaused => MatchesSelectedPoiNarration(_playbackSnapshot, PoiAudioPlaybackStatus.Paused);
    public bool CanToggleNarration => SelectedPoiDetail is not null && _isNarrationContextActive && !IsPoiDetailLoading && !IsSelectedPoiNarrationLoading;
    public double NarrationButtonOpacity => CanToggleNarration ? 1d : 0.72d;

    public string SelectedPoiTitle
        => FirstNonEmpty(
            LocalizedTextHelper.GetLocalizedText(SelectedPoiDetail?.Name, LanguageService.CurrentLanguage),
            SelectedPoi?.Title,
            NoSelectionText);

    public string SelectedPoiDescription
        => FirstNonEmpty(
            LocalizedTextHelper.GetLocalizedText(SelectedPoiDetail?.Description, LanguageService.CurrentLanguage),
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
    public AsyncCommand CloseBottomSheetCommand => new(CloseBottomSheetAsync);
    public AsyncCommand PlayNarrationCommand { get; }
    public AsyncCommand OpenDirectionsCommand => new(BuildRouteAsync);

    public void ActivateNarrationContext()
    {
        _isNarrationContextActive = true;
        RefreshNarrationBindings();
    }

    public async Task SuspendNarrationAsync()
    {
        _isNarrationContextActive = false;
        IsAutoNarrationPlaying = false;
        RefreshNarrationBindings();
        await _poiAudioPlaybackService.StopAsync();
    }

    public async Task StopNarrationAsync()
    {
        IsAutoNarrationPlaying = false;
        await _poiAudioPlaybackService.StopAsync();
    }

    public Task LoadAsync()
        => LoadAsync(autoPlayNarrationForSelection: false);

    public async Task LoadAsync(bool autoPlayNarrationForSelection)
    {
        _logger.LogInformation(
            "[HomeMapLoad] Loading home map state. language={LanguageCode}; currentPoiCount={PoiCount}; selectedPoiId={SelectedPoiId}",
            LanguageService.CurrentLanguage,
            Pois.Count,
            SelectedPoi?.Id ?? string.Empty);
        var hasVisibleState =
            Pois.Count > 0 ||
            AvailableTours.Count > 0 ||
            SelectedPoi is not null ||
            ActiveTour is not null ||
            PreviewTour is not null;

        await ReloadCurrentStateAsync(autoPlayNarrationForSelection);

        _logger.LogInformation(
            "[HomeMapLoad] Home map state loaded. language={LanguageCode}; poiCount={PoiCount}; selectedPoiId={SelectedPoiId}; hasDetail={HasDetail}",
            LanguageService.CurrentLanguage,
            Pois.Count,
            SelectedPoi?.Id ?? string.Empty,
            SelectedPoiDetail is not null);

        if (hasVisibleState)
        {
            _ = RefreshIfNeededSafelyAsync();
        }
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
                _preloadedAudioKeys.Clear();
                await ReloadCurrentStateAsync(autoPlayNarrationForSelection: false);
            }
        }
        finally
        {
            Interlocked.Exchange(ref _syncRefreshInProgress, 0);
        }
    }

    private async Task RefreshIfNeededSafelyAsync()
    {
        try
        {
            await RefreshIfNeededAsync();
        }
        catch
        {
            // Best-effort background refresh. Existing snapshot should remain visible.
        }
    }

    private async Task ReloadCurrentStateAsync(bool autoPlayNarrationForSelection)
    {
        await _stateReloadLock.WaitAsync();
        try
        {
            var currentPoiId = SelectedPoi?.Id;
            var previousPoiCount = Pois.Count;

            Pois.ReplaceRange(await _dataService.GetPoisAsync());
            ApplySearch();
            await ReloadTourExperienceAsync();

            SelectedPoi = currentPoiId is null
                ? null
                : Pois.FirstOrDefault(item => string.Equals(item.Id, currentPoiId, StringComparison.OrdinalIgnoreCase));

            MapDataVersion++;

            if (SelectedPoi is not null && (IsBottomSheetVisible || SelectedPoiDetail is not null))
            {
                await LoadPoiDetailCoreAsync(
                    SelectedPoi,
                    IsBottomSheetVisible,
                    autoPlayNarrationForSelection,
                    trackPoiView: false,
                    trackAutoPlayListen: false);
            }
            else
            {
                RefreshLocalizedTexts();
            }

            await EnsureSimulationInitializedAsync();
            await RefreshCurrentLocationAsync();

            _logger.LogInformation(
                "[HomeMapLoad] Snapshot reloaded. language={LanguageCode}; previousPoiCount={PreviousPoiCount}; currentPoiCount={CurrentPoiCount}; selectedPoiId={SelectedPoiId}",
                LanguageService.CurrentLanguage,
                previousPoiCount,
                Pois.Count,
                SelectedPoi?.Id ?? string.Empty);
        }
        finally
        {
            _stateReloadLock.Release();
        }
    }

    private async Task ReloadLocalizedMapStateAsync()
    {
        await _stateReloadLock.WaitAsync();
        try
        {
            var reloadLanguage = LanguageService.CurrentLanguage;
            var currentPoiId = SelectedPoi?.Id;
            var previousPoiCount = Pois.Count;
            var shouldRefreshSelectedDetail = !string.IsNullOrWhiteSpace(currentPoiId) &&
                                              (SelectedPoiDetail is not null || IsBottomSheetVisible);

            _logger.LogInformation(
                "[LanguageReload] Reloading localized home map state. language={LanguageCode}; previousPoiCount={PreviousPoiCount}; selectedPoiId={SelectedPoiId}; hasSelectedDetail={HasSelectedDetail}",
                LanguageService.CurrentLanguage,
                previousPoiCount,
                currentPoiId ?? string.Empty,
                shouldRefreshSelectedDetail);

            var pois = await _dataService.GetPoisAsync();
            if (!string.Equals(
                    AppLanguage.NormalizeCode(reloadLanguage),
                    AppLanguage.NormalizeCode(LanguageService.CurrentLanguage),
                    StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogInformation(
                    "[LanguageReload] Discarding stale POI list. reloadLanguage={ReloadLanguage}; currentLanguage={CurrentLanguage}",
                    reloadLanguage,
                    LanguageService.CurrentLanguage);
                return;
            }

            Pois.ReplaceRange(pois);
            ApplySearch();
            await ReloadTourExperienceAsync();

            SelectedPoi = currentPoiId is null
                ? null
                : Pois.FirstOrDefault(item => string.Equals(item.Id, currentPoiId, StringComparison.OrdinalIgnoreCase));

            if (SelectedPoi is null)
            {
                SelectedPoiDetail = null;
            }
            else if (shouldRefreshSelectedDetail)
            {
                try
                {
                    var detail = await _dataService.GetPoiDetailAsync(SelectedPoi.Id);
                    if (!string.Equals(
                            AppLanguage.NormalizeCode(reloadLanguage),
                            AppLanguage.NormalizeCode(LanguageService.CurrentLanguage),
                            StringComparison.OrdinalIgnoreCase))
                    {
                        _logger.LogInformation(
                            "[LanguageReload] Discarding stale selected detail. poiId={PoiId}; reloadLanguage={ReloadLanguage}; currentLanguage={CurrentLanguage}",
                            SelectedPoi.Id,
                            reloadLanguage,
                            LanguageService.CurrentLanguage);
                        return;
                    }

                    SelectedPoiDetail = detail ?? CreateInlineFallbackDetail(SelectedPoi);
                }
                catch
                {
                    SelectedPoiDetail = CreateInlineFallbackDetail(SelectedPoi);
                }
            }

            MapContentVersion++;
            RefreshLocalizedTexts();
            await RefreshCurrentLocationAsync();

            _logger.LogInformation(
                "[LanguageReload] Localized home map state reloaded. language={LanguageCode}; previousPoiCount={PreviousPoiCount}; currentPoiCount={CurrentPoiCount}; selectedPoiId={SelectedPoiId}; hasSelectedDetail={HasSelectedDetail}",
                LanguageService.CurrentLanguage,
                previousPoiCount,
                Pois.Count,
                SelectedPoi?.Id ?? string.Empty,
                SelectedPoiDetail is not null);
        }
        finally
        {
            _stateReloadLock.Release();
        }
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

        await LoadPoiDetailCoreAsync(
            poi,
            true,
            autoPlayNarration,
            usageSource: "map",
            trackPoiView: true,
            trackAutoPlayListen: autoPlayNarration,
            centerMapOnSelection: true);
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

        await LoadPoiDetailCoreAsync(
            poi,
            true,
            autoPlayNarration,
            usageSource: "map",
            trackPoiView: true,
            trackAutoPlayListen: autoPlayNarration,
            centerMapOnSelection: true);
    }

    private async Task LoadPoiDetailCoreAsync(
        PoiLocation poi,
        bool showBottomSheet,
        bool autoPlayNarration = false,
        string usageSource = "map",
        bool trackPoiView = false,
        bool trackAutoPlayListen = false,
        bool centerMapOnSelection = false)
    {
        var requestVersion = Interlocked.Increment(ref _detailRequestVersion);
        var requestLanguage = LanguageService.CurrentLanguage;
        var requestCancellation = ResetDetailRequestCancellation();
        await StopNarrationIfSelectingDifferentPoiAsync(poi.Id);

        NarrationMessage = string.Empty;
        RequestMapCenterForPoi(centerMapOnSelection ? poi.Id : null);
        SelectedPoi = poi;
        IsBottomSheetVisible = showBottomSheet;
        SelectedPoiDetail = CreateInlineFallbackDetail(poi);
        IsPoiDetailLoading = true;

        if (trackPoiView)
        {
            _logger.LogInformation(
                "[PoiAnalytics] Recording POI view from user selection. poiId={PoiId}; language={LanguageCode}; source={Source}; autoPlayNarration={AutoPlayNarration}",
                poi.Id,
                requestLanguage,
                usageSource,
                autoPlayNarration);
            await _analyticsService.TrackPoiViewAsync(poi.Id, requestLanguage, usageSource);
        }

        try
        {
            PoiExperienceDetail detail;
            try
            {
                detail = await _dataService.GetPoiDetailAsync(poi.Id, requestCancellation.Token) ?? CreateInlineFallbackDetail(poi);
            }
            catch (OperationCanceledException) when (requestCancellation.Token.IsCancellationRequested)
            {
                _logger.LogInformation(
                    "[PoiDetailRace] canceled stale detail request. poiId={PoiId}; requestLanguage={RequestLanguage}; currentLanguage={CurrentLanguage}; requestVersion={RequestVersion}",
                    poi.Id,
                    requestLanguage,
                    LanguageService.CurrentLanguage,
                    requestVersion);
                return;
            }
            catch
            {
                detail = CreateInlineFallbackDetail(poi);
            }

            if (requestVersion != _detailRequestVersion ||
                !string.Equals(
                    AppLanguage.NormalizeCode(requestLanguage),
                    AppLanguage.NormalizeCode(LanguageService.CurrentLanguage),
                    StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogInformation(
                    "[PoiDetailRace] ignoring stale detail response. poiId={PoiId}; requestLanguage={RequestLanguage}; currentLanguage={CurrentLanguage}; requestVersion={RequestVersion}; latestVersion={LatestVersion}",
                    poi.Id,
                    requestLanguage,
                    LanguageService.CurrentLanguage,
                    requestVersion,
                    _detailRequestVersion);
                return;
            }

            SelectedPoiDetail = detail;
            if (trackPoiView && detail.Promotions.Count > 0)
            {
                await TrackVisibleOfferViewsAsync(detail, requestLanguage, requestCancellation.Token);
            }

            _ = _poiAudioPlaybackService.PreloadAsync(detail, requestLanguage);
            await MarkActiveTourPoiVisitedAsync(poi.Id);

            _logger.LogInformation(
                "[PoiDetail] Loaded POI detail. poiId={PoiId}; language={LanguageCode}; audioAssetCount={AudioAssetCount}; imageCount={ImageCount}",
                detail.Id,
                requestLanguage,
                detail.AudioAssets.Values.Count,
                detail.Images.Count);

            if (autoPlayNarration)
            {
                ScheduleAutoPlayNarration(detail, requestVersion, trackAutoPlayListen);
            }
        }
        finally
        {
            if (requestVersion == _detailRequestVersion)
            {
                IsPoiDetailLoading = false;
                RefreshLocalizedTexts();
            }

            if (ReferenceEquals(_detailRequestCancellation, requestCancellation))
            {
                _detailRequestCancellation = null;
            }

            requestCancellation.Dispose();
        }
    }

    private async Task TrackVisibleOfferViewsAsync(
        PoiExperienceDetail detail,
        string languageCode,
        CancellationToken cancellationToken)
    {
        foreach (var promotion in detail.Promotions.Where(item => !string.IsNullOrWhiteSpace(item.Id)))
        {
            cancellationToken.ThrowIfCancellationRequested();
            await _analyticsService.TrackOfferViewAsync(
                detail.Id,
                promotion.Id,
                languageCode,
                source: "poi_detail_promotions",
                cancellationToken: cancellationToken);
        }
    }

    private CancellationTokenSource ResetDetailRequestCancellation()
    {
        var previous = _detailRequestCancellation;
        previous?.Cancel();
        _detailRequestCancellation = new CancellationTokenSource();
        return _detailRequestCancellation;
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

        await ApplyLocationUpdateAsync(currentLocation, isMockLocation: true, isUserInitiated: false);
    }

    private void OnUserLocationChanged(object? sender, UserLocationChangedEventArgs e)
        => MainThread.BeginInvokeOnMainThread(() => _ = ApplyLocationUpdateAsync(
            e.Location,
            e.IsMock,
            e.IsUserInitiated));

    private async Task ApplyLocationUpdateAsync(
        UserLocationPoint location,
        bool isMockLocation,
        bool isUserInitiated)
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

            if (isMockLocation && !isUserInitiated && _simulationMode == SimulationMode.Manual)
            {
                ApplyUserLocationSnapshot(BuildPassiveLocationSnapshot(location), isMockLocation);
                return;
            }

            // Map clicks in mock mode should trigger POI lookup immediately at the clicked point.
            var narrationResult = isMockLocation && isUserInitiated
                ? await _autoNarrationService.ProcessMockLocationTapAsync(location, Pois)
                : await _autoNarrationService.ProcessLocationAsync(location, Pois, isMockLocation);
            ApplyUserLocationSnapshot(narrationResult.Snapshot, isMockLocation);
            ScheduleNearbyAudioPreload(narrationResult.Snapshot);

            if (!narrationResult.PlaybackStarted ||
                narrationResult.TriggeredPoi is null ||
                narrationResult.TriggeredDetail is null)
            {
                return;
            }

            LastTriggeredPoiId = narrationResult.TriggeredPoi.Id;
            await PresentAutoNarratedPoiAsync(
                narrationResult.TriggeredPoi,
                narrationResult.TriggeredDetail);
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

    private void ScheduleNearbyAudioPreload(PoiProximitySnapshot snapshot)
    {
        SchedulePoiAudioPreload(snapshot.ActivePoi);
        if (snapshot.NearestPoi is not null &&
            !string.Equals(snapshot.NearestPoi.Id, snapshot.ActivePoi?.Id, StringComparison.OrdinalIgnoreCase))
        {
            SchedulePoiAudioPreload(snapshot.NearestPoi);
        }
    }

    private void SchedulePoiAudioPreload(PoiLocation? poi)
    {
        if (poi is null)
        {
            return;
        }

        var languageCode = AppLanguage.NormalizeCode(LanguageService.CurrentLanguage);
        var preloadKey = $"{poi.Id}:{languageCode}";
        if (!_preloadedAudioKeys.Add(preloadKey))
        {
            return;
        }

        _ = PreloadPoiAudioAsync(poi, languageCode, preloadKey);
    }

    private async Task PreloadPoiAudioAsync(PoiLocation poi, string languageCode, string preloadKey)
    {
        try
        {
            var detail = await _dataService.GetPoiDetailAsync(poi.Id) ?? CreateInlineFallbackDetail(poi);
            await _poiAudioPlaybackService.PreloadAsync(detail, languageCode);
        }
        catch
        {
            _preloadedAudioKeys.Remove(preloadKey);
        }
    }

    private PoiProximitySnapshot BuildPassiveLocationSnapshot(UserLocationPoint location)
    {
        var candidates = PoiOverlapSelectionHelper.BuildCandidates(
            location,
            Pois,
            CalculateDistanceMeters);
        var activeCandidate = PoiOverlapSelectionHelper.SelectBestCandidate(candidates);

        _logger.LogDebug(
            "[PoiOverlap] source=passive-snapshot; latitude={Latitude}; longitude={Longitude}; candidates={Candidates}; selected={Selected}",
            location.Latitude,
            location.Longitude,
            PoiOverlapSelectionHelper.DescribeCandidates(candidates),
            PoiOverlapSelectionHelper.DescribeCandidate(activeCandidate));

        return PoiOverlapSelectionHelper.BuildSnapshot(location, candidates, activeCandidate);
    }

    private async Task PresentAutoNarratedPoiAsync(PoiLocation poi, PoiExperienceDetail detail)
    {
        RequestMapCenterForPoi(null);
        SelectedPoi = poi;
        SelectedPoiDetail = detail;
        IsPoiDetailLoading = false;
        IsBottomSheetVisible = true;

        await MarkActiveTourPoiVisitedAsync(poi.Id);

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

    private async Task CloseBottomSheetAsync()
    {
        IsBottomSheetVisible = false;
        RequestMapCenterForPoi(null);
        SelectedPoi = null;
        SelectedPoiDetail = null;
        NarrationMessage = string.Empty;
        await StopNarrationAsync();
    }

    private async Task PlayNarrationAsync()
    {
        if (SelectedPoiDetail is null || !_isNarrationContextActive || !CanToggleNarration)
        {
            return;
        }

        NarrationMessage = string.Empty;
        await _poiAudioPlaybackService.ToggleAsync(
            SelectedPoiDetail,
            LanguageService.CurrentLanguage,
            trackAnalytics: true);
    }

    private static double CalculateDistanceMeters(
        double latitude1,
        double longitude1,
        double latitude2,
        double longitude2)
    {
        const double earthRadiusMeters = 6_371_000d;
        var latitudeDelta = DegreesToRadians(latitude2 - latitude1);
        var longitudeDelta = DegreesToRadians(longitude2 - longitude1);
        var latitudeStart = DegreesToRadians(latitude1);
        var latitudeEnd = DegreesToRadians(latitude2);
        var haversine =
            Math.Pow(Math.Sin(latitudeDelta / 2d), 2d) +
            Math.Cos(latitudeStart) *
            Math.Cos(latitudeEnd) *
            Math.Pow(Math.Sin(longitudeDelta / 2d), 2d);
        var centralAngle = 2d * Math.Atan2(Math.Sqrt(haversine), Math.Sqrt(1d - haversine));
        return earthRadiusMeters * centralAngle;
    }

    private static double DegreesToRadians(double degrees)
        => degrees * (Math.PI / 180d);

    private void ScheduleAutoPlayNarration(
        PoiExperienceDetail detail,
        long requestVersion,
        bool trackAnalytics)
    {
        if (!_isNarrationContextActive)
        {
            return;
        }

        MainThread.BeginInvokeOnMainThread(() => _ = AutoPlayNarrationAsync(detail, requestVersion, trackAnalytics));
    }

    private async Task AutoPlayNarrationAsync(
        PoiExperienceDetail detail,
        long requestVersion,
        bool trackAnalytics)
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
            var snapshot = _poiAudioPlaybackService.Snapshot;
            if (snapshot.Status is PoiAudioPlaybackStatus.Loading or PoiAudioPlaybackStatus.Playing or PoiAudioPlaybackStatus.Paused &&
                snapshot.Matches(detail.Id, LanguageService.CurrentLanguage))
            {
                return;
            }

            await _poiAudioPlaybackService.PlayAsync(
                detail,
                LanguageService.CurrentLanguage,
                trackAnalytics: trackAnalytics);
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
            MapContentVersion++;
            return;
        }

        var matches = Pois.Where(item =>
                item.Title.Contains(SearchText, StringComparison.OrdinalIgnoreCase) ||
                item.Category.Contains(SearchText, StringComparison.OrdinalIgnoreCase))
            .ToList();

        SearchResults.ReplaceRange(matches);
        if (matches.Count > 0)
        {
            RequestMapCenterForPoi(matches[0].Id);
            SelectedPoi = matches[0];
        }

        MapContentVersion++;
    }

    private void RefreshLocalizedTexts()
    {
        SyncNarrationMessage();
        RefreshLocalizedBindings();
        RefreshNarrationBindings();
    }

    private void RefreshSelectedPoiBindings()
    {
        RefreshLocalizedBindings();
        RefreshNarrationBindings();
    }

    private void RefreshDetailBindings()
    {
        SyncNarrationMessage();
        RefreshLocalizedBindings();
        RefreshNarrationBindings();
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

    public bool ConsumeSelectedPoiMapCenterRequest()
    {
        if (SelectedPoi is null ||
            string.IsNullOrWhiteSpace(_pendingMapCenterPoiId) ||
            !string.Equals(_pendingMapCenterPoiId, SelectedPoi.Id, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        _pendingMapCenterPoiId = null;
        return true;
    }

    private void RequestMapCenterForPoi(string? poiId)
    {
        _pendingMapCenterPoiId = string.IsNullOrWhiteSpace(poiId)
            ? null
            : poiId.Trim();
    }

    private async Task StopNarrationIfSelectingDifferentPoiAsync(string nextPoiId)
    {
        var snapshot = _poiAudioPlaybackService.Snapshot;
        if (snapshot.Status is not (PoiAudioPlaybackStatus.Loading or PoiAudioPlaybackStatus.Playing or PoiAudioPlaybackStatus.Paused) ||
            string.IsNullOrWhiteSpace(snapshot.PoiId) ||
            string.Equals(snapshot.PoiId, nextPoiId, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        await StopNarrationAsync();
    }

    private void OnPlaybackStateChanged(object? sender, PoiAudioPlaybackSnapshot snapshot)
        => MainThread.BeginInvokeOnMainThread(() => ApplyPlaybackSnapshot(snapshot));

    private void ApplyPlaybackSnapshot(PoiAudioPlaybackSnapshot snapshot)
    {
        _playbackSnapshot = snapshot;
        IsAutoNarrationPlaying = snapshot.Status == PoiAudioPlaybackStatus.Playing;
        SyncNarrationMessage();
        RefreshNarrationBindings();
    }

    private void SyncNarrationMessage()
    {
        NarrationMessage =
            SelectedPoiDetail is not null &&
            _playbackSnapshot.Status == PoiAudioPlaybackStatus.Error &&
            _playbackSnapshot.Matches(SelectedPoiDetail.Id, LanguageService.CurrentLanguage)
                ? ResolveNarrationErrorMessage(_playbackSnapshot.ErrorMessage)
                : string.Empty;
    }

    private string ResolveNarrationErrorMessage(string? errorMessage)
    {
        var poiTitle = SelectedPoiDetail is null ? string.Empty : SelectedPoiTitle;
        var languageLabel = LanguageService.GetLanguageDefinition(LanguageService.CurrentLanguage).DisplayName;
        var contextSuffix = string.IsNullOrWhiteSpace(poiTitle)
            ? string.Empty
            : $" ({poiTitle} - {languageLabel})";

        return errorMessage switch
        {
            "missing_pre_generated_audio" => $"{LanguageService.GetText("poi_detail_narration_missing")}{contextSuffix}",
            "audio_playback_unavailable" => $"{LanguageService.GetText("poi_detail_narration_error")}{contextSuffix}",
            _ => string.IsNullOrWhiteSpace(errorMessage)
                ? $"{LanguageService.GetText("poi_detail_narration_error")}{contextSuffix}"
                : $"{errorMessage}{contextSuffix}"
        };
    }

    private void RefreshNarrationBindings()
    {
        OnPropertyChanged(nameof(ListenActionText));
        OnPropertyChanged(nameof(ListenActionIconText));
        OnPropertyChanged(nameof(IsSelectedPoiNarrationLoading));
        OnPropertyChanged(nameof(IsSelectedPoiNarrationPlaying));
        OnPropertyChanged(nameof(IsSelectedPoiNarrationPaused));
        OnPropertyChanged(nameof(CanToggleNarration));
        OnPropertyChanged(nameof(NarrationButtonOpacity));
        PlayNarrationCommand.NotifyCanExecuteChanged();
    }

    private bool MatchesSelectedPoiNarration(PoiAudioPlaybackSnapshot snapshot, PoiAudioPlaybackStatus status)
        => snapshot.Status == status &&
           SelectedPoiDetail is not null &&
           snapshot.Matches(SelectedPoiDetail.Id, LanguageService.CurrentLanguage);

    protected override async Task ReloadLocalizedStateAsync()
    {
        Interlocked.Increment(ref _detailRequestVersion);
        _detailRequestCancellation?.Cancel();
        _preloadedAudioKeys.Clear();
        await StopNarrationAsync();
        await ReloadLocalizedMapStateAsync();
    }
}
