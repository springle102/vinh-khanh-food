using System.Collections.ObjectModel;
using System.Globalization;
using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.Devices.Sensors;
using VinhKhanh.MobileApp.Helpers;
using VinhKhanh.MobileApp.Models;
using VinhKhanh.MobileApp.Services;

namespace VinhKhanh.MobileApp.ViewModels;

public sealed class HomeMapViewModel : BaseViewModel
{
    private readonly IFoodStreetDataService _dataService;
    private readonly IAppLanguageService _languageService;
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
    {
        _dataService = dataService;
        _languageService = languageService;
        _poiNarrationService = poiNarrationService;
        _poiTourStoreService = poiTourStoreService;
        _languageService.LanguageChanged += OnLanguageChanged;
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
    public bool HasReviewSummary => (SelectedPoiDetail?.ReviewCount ?? 0) > 0;
    public bool IsFloatingPoiActionVisible => !HasVisibleBottomSheet;

    public string SearchPlaceholderText => _languageService.GetText("home_search_placeholder");
    public string PoiChipText => _languageService.GetText("home_poi_chip");
    public string LayerButtonText => _languageService.GetText("home_layer");
    public string PoiActionText => _languageService.GetText("bottom_poi");
    public string ListenActionText => _languageService.GetText("poi_detail_listen");
    public string DirectionsActionText => _languageService.GetText("poi_detail_directions");
    public string SaveActionText => _languageService.GetText(IsPoiSaved ? "poi_detail_saved" : "poi_detail_save");
    public string DetailLoadingText => _languageService.GetText("poi_detail_loading");
    public string FeaturedBadgeText => _languageService.GetText("poi_detail_featured");
    public string NoSelectionText => _languageService.GetText("poi_detail_no_selection");
    public string AddressLabelText => _languageService.GetText("poi_detail_address");

    public string SelectedPoiTitle
        => FirstNonEmpty(
            LocalizedTextHelper.GetLocalizedText(SelectedPoiDetail?.Name, _languageService.CurrentLanguage),
            SelectedPoi?.Title,
            NoSelectionText);

    public string SelectedPoiDescription
        => FirstNonEmpty(
            LocalizedTextHelper.GetLocalizedText(SelectedPoiDetail?.Description, _languageService.CurrentLanguage),
            LocalizedTextHelper.GetLocalizedText(SelectedPoiDetail?.Summary, _languageService.CurrentLanguage),
            SelectedPoi?.ShortDescription,
            _languageService.GetText("home_default_description"));

    public string SelectedPoiSummary
        => FirstNonEmpty(
            LocalizedTextHelper.GetLocalizedText(SelectedPoiDetail?.Summary, _languageService.CurrentLanguage),
            SelectedPoi?.ShortDescription,
            _languageService.GetText("home_default_description"));

    public string SelectedPoiAddress
        => FirstNonEmpty(
            SelectedPoiDetail?.Address,
            SelectedPoi?.Address,
            _languageService.GetText("home_default_address"));

    public string SelectedPoiCategory
        => FirstNonEmpty(
            SelectedPoiDetail?.Category,
            SelectedPoi?.Category,
            _languageService.GetText("home_poi_chip"));

    public string SelectedPoiPriceRange
        => FirstNonEmpty(
            SelectedPoi?.PriceRange,
            "80.000 - 350.000 VND");

    public string SelectedPoiImageUrl => DetailImages.FirstOrDefault() ?? _dataService.GetBackdropImageUrl();

    public string SelectedPoiRatingText
        => SelectedPoiDetail is null
            ? "4.6"
            : SelectedPoiDetail.Rating.ToString("0.0", CultureInfo.InvariantCulture);

    public string SelectedPoiReviewText
        => !HasReviewSummary
            ? string.Empty
            : $"{SelectedPoiDetail!.ReviewCount} {_languageService.GetText("poi_detail_reviews")}";

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
        SearchResults.ReplaceRange(Pois);
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

        await _poiNarrationService.PlayAsync(SelectedPoiDetail, _languageService.CurrentLanguage);
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
            await _poiNarrationService.PlayAsync(detail, _languageService.CurrentLanguage);
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
    {
        OnPropertyChanged(nameof(SearchPlaceholderText));
        OnPropertyChanged(nameof(PoiChipText));
        OnPropertyChanged(nameof(LayerButtonText));
        OnPropertyChanged(nameof(PoiActionText));
        OnPropertyChanged(nameof(ListenActionText));
        OnPropertyChanged(nameof(DirectionsActionText));
        OnPropertyChanged(nameof(SaveActionText));
        OnPropertyChanged(nameof(DetailLoadingText));
        OnPropertyChanged(nameof(FeaturedBadgeText));
        OnPropertyChanged(nameof(NoSelectionText));
        OnPropertyChanged(nameof(AddressLabelText));
        RefreshSelectedPoiBindings();
        RefreshDetailBindings();
    }

    private void RefreshSelectedPoiBindings()
    {
        OnPropertyChanged(nameof(SelectedPoiTitle));
        OnPropertyChanged(nameof(SelectedPoiDescription));
        OnPropertyChanged(nameof(SelectedPoiSummary));
        OnPropertyChanged(nameof(SelectedPoiAddress));
        OnPropertyChanged(nameof(SelectedPoiCategory));
        OnPropertyChanged(nameof(SelectedPoiPriceRange));
        OnPropertyChanged(nameof(SelectedPoiImageUrl));
        OnPropertyChanged(nameof(HasDetailContent));
    }

    private void RefreshDetailBindings()
    {
        OnPropertyChanged(nameof(HasSelectedPoiDetail));
        OnPropertyChanged(nameof(IsFeaturedPoi));
        OnPropertyChanged(nameof(HasReviewSummary));
        OnPropertyChanged(nameof(SelectedPoiTitle));
        OnPropertyChanged(nameof(SelectedPoiDescription));
        OnPropertyChanged(nameof(SelectedPoiSummary));
        OnPropertyChanged(nameof(SelectedPoiAddress));
        OnPropertyChanged(nameof(SelectedPoiCategory));
        OnPropertyChanged(nameof(SelectedPoiImageUrl));
        OnPropertyChanged(nameof(SelectedPoiRatingText));
        OnPropertyChanged(nameof(SelectedPoiReviewText));
        OnPropertyChanged(nameof(DetailImages));
        OnPropertyChanged(nameof(HasVisibleBottomSheet));
        OnPropertyChanged(nameof(IsFloatingPoiActionVisible));
    }

    private void OnLanguageChanged(object? sender, EventArgs e)
    {
        MainThread.BeginInvokeOnMainThread(async () =>
        {
            try
            {
                await StopNarrationAsync();
                await LoadAsync(autoPlayNarrationForSelection: _isNarrationContextActive && IsBottomSheetVisible);
            }
            catch
            {
                if (SelectedPoi is not null)
                {
                    SelectedPoiDetail = CreateInlineFallbackDetail(SelectedPoi);
                }

                RefreshLocalizedTexts();
            }
        });
    }

    private PoiExperienceDetail CreateInlineFallbackDetail(PoiLocation poi)
    {
        var detail = new PoiExperienceDetail
        {
            Id = poi.Id,
            Category = poi.Category,
            Address = poi.Address,
            Latitude = poi.Latitude,
            Longitude = poi.Longitude,
            Rating = 4.5,
            ReviewCount = 84,
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

        var normalizedLanguage = NormalizeLanguageCode(_languageService.CurrentLanguage);
        target.Set(normalizedLanguage, value);

        var separatorIndex = normalizedLanguage.IndexOf('-');
        if (separatorIndex > 0)
        {
            target.Set(normalizedLanguage[..separatorIndex], value);
        }
    }

    private static string NormalizeLanguageCode(string? languageCode)
    {
        if (string.IsNullOrWhiteSpace(languageCode))
        {
            return "vi";
        }

        return languageCode.Trim() switch
        {
            "zh" => "zh-CN",
            "fr-FR" => "fr",
            "en-US" => "en",
            "ja-JP" => "ja",
            "ko-KR" => "ko",
            _ => languageCode.Trim()
        };
    }

    private static string FirstNonEmpty(params string?[] values)
        => values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))?.Trim() ?? string.Empty;
}
