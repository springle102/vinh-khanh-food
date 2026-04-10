using System.Collections.ObjectModel;
using System.Globalization;
using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.Devices.Sensors;
using VinhKhanh.MobileApp.Helpers;
using VinhKhanh.MobileApp.Models;
using VinhKhanh.MobileApp.Services;

namespace VinhKhanh.MobileApp.ViewModels;

public sealed class HomeMapViewModel : LocalizedViewModelBase
{
    private readonly IFoodStreetDataService _dataService;
    private readonly IPoiNarrationService _poiNarrationService;
    private readonly IPoiTourStoreService _poiTourStoreService;

    private string _searchText = string.Empty;
    private PoiLocation? _selectedPoi;
    private PoiExperienceDetail? _selectedPoiDetail;
    private bool _isHeatmapVisible = true;
    private bool _isBottomSheetVisible;
    private bool _isPoiDetailLoading;
    private bool _isPoiSaved;
    private volatile bool _isNarrationContextActive = true;
    private int _mapDataVersion;
    private long _detailRequestVersion;

    public HomeMapViewModel(
        IFoodStreetDataService dataService,
        IAppLanguageService languageService,
        IPoiNarrationService poiNarrationService,
        IPoiTourStoreService poiTourStoreService)
        : base(languageService)
    {
        _dataService = dataService;
        _poiNarrationService = poiNarrationService;
        _poiTourStoreService = poiTourStoreService;
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

    public bool IsPoiSaved
    {
        get => _isPoiSaved;
        set
        {
            if (SetProperty(ref _isPoiSaved, value))
            {
                OnPropertyChanged(nameof(SaveActionText));
            }
        }
    }

    public int MapDataVersion
    {
        get => _mapDataVersion;
        private set => SetProperty(ref _mapDataVersion, value);
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
    public bool IsFloatingPoiActionVisible => !HasVisibleBottomSheet;

    public string SearchPlaceholderText => LanguageService.GetText("home_search_placeholder");
    public string PoiChipText => LanguageService.GetText("home_poi_chip");
    public string LayerButtonText => LanguageService.GetText("home_layer");
    public string PoiActionText => LanguageService.GetText("bottom_poi");
    public string ListenActionText => LanguageService.GetText("poi_detail_listen");
    public string DirectionsActionText => LanguageService.GetText("poi_detail_directions");
    public string SaveActionText => LanguageService.GetText(IsPoiSaved ? "poi_detail_saved" : "poi_detail_save");
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

    public AsyncCommand<PoiLocation> SelectPoiCommand => new(poi => SelectPoiAsync(poi, autoPlayNarration: true));
    public AsyncCommand<string> LoadPoiDetailCommand => new(poiId => LoadPoiDetailByIdAsync(poiId, autoPlayNarration: true));
    public AsyncCommand SelectNextPoiCommand => new(SelectNextPoiAsync);
    public AsyncCommand ToggleHeatmapCommand => new(ToggleHeatmapAsync);
    public AsyncCommand CloseBottomSheetCommand => new(CloseBottomSheetAsync);
    public AsyncCommand PlayNarrationCommand => new(PlayNarrationAsync);
    public AsyncCommand OpenDirectionsCommand => new(OpenDirectionsAsync);
    public AsyncCommand ToggleSaveToTourCommand => new(ToggleSaveToTourAsync);

    public void ActivateNarrationContext()
        => _isNarrationContextActive = true;

    public async Task SuspendNarrationAsync()
    {
        _isNarrationContextActive = false;
        await _poiNarrationService.StopAsync();
    }

    public Task StopNarrationAsync()
        => _poiNarrationService.StopAsync();

    public Task LoadAsync()
        => LoadAsync(autoPlayNarrationForSelection: false);

    public async Task LoadAsync(bool autoPlayNarrationForSelection)
    {
        var currentPoiId = SelectedPoi?.Id;

        Pois.ReplaceRange(await _dataService.GetPoisAsync());
        ApplySearch();
        HeatPoints.ReplaceRange(await _dataService.GetHeatPointsAsync());

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

    private async Task LoadPoiDetailCoreAsync(PoiLocation poi, bool showBottomSheet, bool autoPlayNarration = false)
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

            var isSaved = await _poiTourStoreService.IsSavedAsync(poi.Id);
            if (requestVersion != _detailRequestVersion)
            {
                return;
            }

            SelectedPoiDetail = detail;
            IsPoiSaved = isSaved;

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

    private async Task SelectNextPoiAsync()
    {
        if (Pois.Count == 0)
        {
            return;
        }

        var currentIndex = SelectedPoi is null ? -1 : Pois.IndexOf(SelectedPoi);
        var nextPoi = Pois[(currentIndex + 1 + Pois.Count) % Pois.Count];
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
        IsPoiSaved = false;
        await _poiNarrationService.StopAsync();
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

    private async Task ToggleSaveToTourAsync()
    {
        var poiId = SelectedPoiDetail?.Id ?? SelectedPoi?.Id;
        if (string.IsNullOrWhiteSpace(poiId))
        {
            return;
        }

        IsPoiSaved = await _poiTourStoreService.ToggleSavedAsync(poiId);
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
