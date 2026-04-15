using System.Collections.ObjectModel;
using System.Globalization;
using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.Devices.Sensors;
using VinhKhanh.MobileApp.Helpers;
using VinhKhanh.MobileApp.Models;
using VinhKhanh.MobileApp.Services;

namespace VinhKhanh.MobileApp.ViewModels;

public sealed partial class HomeMapViewModel : LocalizedViewModelBase
{
    private const double VirtualPoiActivationRadiusMeters = 10d;
    private const double DefaultVirtualLocationLatitudeOffset = 0.00015d;

    private readonly IFoodStreetDataService _dataService;
    private readonly IPoiNarrationService _poiNarrationService;
    private readonly IVirtualLocationService _virtualLocationService;
    private readonly IPoiProximityService _poiProximityService;

    private string _searchText = string.Empty;
    private PoiLocation? _selectedPoi;
    private PoiExperienceDetail? _selectedPoiDetail;
    private PoiProximitySnapshot? _virtualLocationSnapshot;
    private bool _isHeatmapVisible = true;
    private bool _isBottomSheetVisible;
    private bool _isPoiDetailLoading;
    private string? _currentActivePoiId;
    private string? _lastTriggeredPoiId;
    private bool _isAutoNarrationPlaying;
    private volatile bool _isNarrationContextActive = true;
    private int _mapDataVersion;
    private int _virtualLocationVersion;
    private int _syncRefreshInProgress;
    private long _detailRequestVersion;
    private long _virtualLocationRequestVersion;

    public HomeMapViewModel(
        IFoodStreetDataService dataService,
        IAppLanguageService languageService,
        IPoiNarrationService poiNarrationService,
        IVirtualLocationService virtualLocationService,
        IPoiProximityService poiProximityService,
        ITourStateService tourStateService)
        : base(languageService)
    {
        _dataService = dataService;
        _poiNarrationService = poiNarrationService;
        _virtualLocationService = virtualLocationService;
        _poiProximityService = poiProximityService;
        _tourStateService = tourStateService;
        InitializeTourCommands();
    }

    public ObservableCollection<PoiLocation> Pois { get; } = [];
    public ObservableCollection<PoiLocation> SearchResults { get; } = [];
    public ObservableCollection<MapHeatPoint> HeatPoints { get; } = [];

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

    public bool IsHeatmapVisible
    {
        get => _isHeatmapVisible;
        set => SetProperty(ref _isHeatmapVisible, value);
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

    public int VirtualLocationVersion
    {
        get => _virtualLocationVersion;
        private set => SetProperty(ref _virtualLocationVersion, value);
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

    public string SearchPlaceholderText => LanguageService.GetText("home_search_placeholder");
    public string PoiChipText => LanguageService.GetText("home_poi_chip");
    public string LayerButtonText => LanguageService.GetText("home_layer");
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

    public MapVirtualUserState GetMapVirtualUserState()
    {
        if (_virtualLocationSnapshot is null)
        {
            return new MapVirtualUserState();
        }

        var activePoiTitle = _virtualLocationSnapshot.ActivePoi?.Title;
        var nearestPoiTitle = _virtualLocationSnapshot.NearestPoi?.Title;
        return new MapVirtualUserState
        {
            Latitude = _virtualLocationSnapshot.Location.Latitude,
            Longitude = _virtualLocationSnapshot.Location.Longitude,
            ActivePoiId = CurrentActivePoiId,
            PopupTitle = LanguageService.GetText("virtual_location_title"),
            CoordinatesLabel = LanguageService.GetText("virtual_location_coordinates_label"),
            CoordinatesText = FormatCoordinates(
                _virtualLocationSnapshot.Location.Latitude,
                _virtualLocationSnapshot.Location.Longitude),
            StatusLabel = LanguageService.GetText("virtual_location_status_label"),
            StatusText = string.IsNullOrWhiteSpace(activePoiTitle)
                ? LanguageService.GetText("virtual_location_status_idle")
                : string.Format(
                    LanguageService.CurrentCulture,
                    LanguageService.GetText("virtual_location_status_near_poi"),
                    activePoiTitle),
            NearestPoiLabel = LanguageService.GetText("virtual_location_nearest_poi_label"),
            NearestPoiText = FirstNonEmpty(
                nearestPoiTitle,
                LanguageService.GetText("virtual_location_no_nearest_poi")),
            NearestDistanceLabel = LanguageService.GetText("virtual_location_nearest_distance_label"),
            NearestDistanceText = _virtualLocationSnapshot.NearestPoiDistanceMeters is double distanceMeters
                ? FormatDistanceMeters(distanceMeters)
                : LanguageService.GetText("virtual_location_distance_unknown")
        };
    }

    public AsyncCommand<PoiLocation> SelectPoiCommand => new(poi => SelectPoiAsync(poi, autoPlayNarration: true));
    public AsyncCommand<string> LoadPoiDetailCommand => new(poiId => LoadPoiDetailByIdAsync(poiId, autoPlayNarration: true));
    public AsyncCommand SelectNextPoiCommand => new(SelectNextPoiAsync);
    public AsyncCommand ToggleHeatmapCommand => new(ToggleHeatmapAsync);
    public AsyncCommand CloseBottomSheetCommand => new(CloseBottomSheetAsync);
    public AsyncCommand PlayNarrationCommand => new(PlayNarrationAsync);
    public AsyncCommand OpenDirectionsCommand => new(OpenDirectionsAsync);

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
        HeatPoints.ReplaceRange(await _dataService.GetHeatPointsAsync());
        await ReloadTourExperienceAsync();

        EnsureVirtualLocationInitialized();

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

        await SyncVirtualLocationAsync(allowAutoNarration: false);
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

    public async Task SetVirtualLocationAsync(double latitude, double longitude)
    {
        _virtualLocationService.SetLocation(latitude, longitude);
        await SyncVirtualLocationAsync(allowAutoNarration: true);
    }

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

    private void EnsureVirtualLocationInitialized()
    {
        if (_virtualLocationService.HasLocation || Pois.Count == 0)
        {
            return;
        }

        var anchorPoi = SelectedPoi
            ?? Pois.FirstOrDefault(item => item.IsFeatured)
            ?? Pois[0];

        // Seed the demo marker close to a POI, but still outside the 10 m trigger radius.
        _virtualLocationService.Initialize(
            anchorPoi.Latitude + DefaultVirtualLocationLatitudeOffset,
            anchorPoi.Longitude);
    }

    private async Task SyncVirtualLocationAsync(bool allowAutoNarration)
    {
        var currentLocation = _virtualLocationService.CurrentLocation;
        if (currentLocation is null || Pois.Count == 0)
        {
            _virtualLocationSnapshot = null;
            CurrentActivePoiId = null;
            VirtualLocationVersion++;
            return;
        }

        var snapshot = _poiProximityService.Evaluate(
            currentLocation,
            Pois,
            VirtualPoiActivationRadiusMeters);
        var previousActivePoiId = CurrentActivePoiId;
        var nextActivePoiId = snapshot.ActivePoi?.Id;
        var hasEnteredDifferentPoi =
            !string.IsNullOrWhiteSpace(nextActivePoiId) &&
            !string.Equals(previousActivePoiId, nextActivePoiId, StringComparison.OrdinalIgnoreCase);

        _virtualLocationSnapshot = snapshot;
        CurrentActivePoiId = nextActivePoiId;
        VirtualLocationVersion++;

        if (!allowAutoNarration || !hasEnteredDifferentPoi || snapshot.ActivePoi is null)
        {
            return;
        }

        var locationRequestVersion = Interlocked.Increment(ref _virtualLocationRequestVersion);
        await HandleEnteredActivePoiAsync(snapshot.ActivePoi, locationRequestVersion);
    }

    private async Task HandleEnteredActivePoiAsync(PoiLocation poi, long locationRequestVersion)
    {
        try
        {
            await LoadPoiDetailCoreAsync(
                poi,
                showBottomSheet: true,
                autoPlayNarration: false,
                usageSource: "virtual_location");
            if (locationRequestVersion != _virtualLocationRequestVersion ||
                !_isNarrationContextActive ||
                SelectedPoiDetail is null ||
                !string.Equals(CurrentActivePoiId, poi.Id, StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(SelectedPoiDetail.Id, poi.Id, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            LastTriggeredPoiId = poi.Id;
            IsAutoNarrationPlaying = true;
            await _poiNarrationService.PlayAsync(SelectedPoiDetail, LanguageService.CurrentLanguage);
        }
        catch
        {
            // Best effort auto narration only. The user can replay from the detail sheet.
        }
        finally
        {
            if (locationRequestVersion == _virtualLocationRequestVersion)
            {
                IsAutoNarrationPlaying = false;
            }
        }
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

    private Task ToggleHeatmapAsync()
    {
        IsHeatmapVisible = !IsHeatmapVisible;
        return Task.CompletedTask;
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

    private async Task OpenDirectionsAsync()
    {
        var latitude = SelectedPoiDetail?.Latitude ?? SelectedPoi?.Latitude;
        var longitude = SelectedPoiDetail?.Longitude ?? SelectedPoi?.Longitude;
        if (latitude is null || longitude is null)
        {
            return;
        }

        await Map.Default.OpenAsync(
            new Location(latitude.Value, longitude.Value),
            new MapLaunchOptions
            {
                Name = SelectedPoiTitle,
                NavigationMode = NavigationMode.Walking
            });
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
