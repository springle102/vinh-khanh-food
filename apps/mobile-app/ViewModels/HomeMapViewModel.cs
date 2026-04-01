using System.Collections.ObjectModel;
using VinhKhanh.MobileApp.Helpers;
using VinhKhanh.MobileApp.Models;
using VinhKhanh.MobileApp.Services;

namespace VinhKhanh.MobileApp.ViewModels;

public sealed class HomeMapViewModel : BaseViewModel
{
    private readonly IFoodStreetDataService _dataService;
    private readonly IAppLanguageService _languageService;
    private string _loadedLanguage = string.Empty;
    private string _searchText = string.Empty;
    private PoiLocation? _selectedPoi;
    private bool _isHeatmapVisible = true;

    public HomeMapViewModel(
        IFoodStreetDataService dataService,
        IAppLanguageService languageService)
    {
        _dataService = dataService;
        _languageService = languageService;
        _languageService.LanguageChanged += (_, _) => RefreshLocalizedTexts();
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
        set
        {
            if (SetProperty(ref _selectedPoi, value))
            {
                OnPropertyChanged(nameof(SelectedPoiTitle));
                OnPropertyChanged(nameof(SelectedPoiDescription));
                OnPropertyChanged(nameof(SelectedPoiAddress));
                OnPropertyChanged(nameof(SelectedPoiPriceRange));
                OnPropertyChanged(nameof(SelectedPoiImageUrl));
            }
        }
    }

    public bool IsHeatmapVisible
    {
        get => _isHeatmapVisible;
        set => SetProperty(ref _isHeatmapVisible, value);
    }

    public string SearchPlaceholderText => _languageService.GetText("home_search_placeholder");
    public string PoiChipText => _languageService.GetText("home_poi_chip");
    public string LayerButtonText => _languageService.GetText("home_layer");
    public string PoiActionText => _languageService.GetText("bottom_poi");
    public string SelectedPoiTitle => SelectedPoi?.Title ?? _languageService.GetText("home_default_title");
    public string SelectedPoiDescription => SelectedPoi?.ShortDescription ?? _languageService.GetText("home_default_description");
    public string SelectedPoiAddress => SelectedPoi?.Address ?? _languageService.GetText("home_default_address");
    public string SelectedPoiPriceRange => SelectedPoi?.PriceRange ?? "80.000 - 350.000 VND";
    public string SelectedPoiImageUrl => SelectedPoi?.ThumbnailUrl ?? _dataService.GetBackdropImageUrl();

    public AsyncCommand<PoiLocation> SelectPoiCommand => new(SelectPoiAsync);
    public AsyncCommand SelectNextPoiCommand => new(SelectNextPoiAsync);
    public AsyncCommand ToggleHeatmapCommand => new(ToggleHeatmapAsync);

    public async Task LoadAsync()
    {
        if (Pois.Count == 0 || !string.Equals(_loadedLanguage, _languageService.CurrentLanguage, StringComparison.OrdinalIgnoreCase))
        {
            var selectedPoiId = SelectedPoi?.Id;
            Pois.ReplaceRange(await _dataService.GetPoisAsync());
            SearchResults.ReplaceRange(Pois);
            HeatPoints.ReplaceRange(await _dataService.GetHeatPointsAsync());
            SelectedPoi = selectedPoiId is null
                ? Pois.FirstOrDefault()
                : Pois.FirstOrDefault(item => item.Id == selectedPoiId) ?? Pois.FirstOrDefault();
            _loadedLanguage = _languageService.CurrentLanguage;
        }

        RefreshLocalizedTexts();
    }

    public Task SelectPoiByIdAsync(string poiId)
    {
        var poi = Pois.FirstOrDefault(item => item.Id == poiId);
        return SelectPoiAsync(poi);
    }

    private Task SelectPoiAsync(PoiLocation? poi)
    {
        if (poi is not null)
        {
            SelectedPoi = poi;
        }

        return Task.CompletedTask;
    }

    private Task SelectNextPoiAsync()
    {
        if (Pois.Count == 0)
        {
            return Task.CompletedTask;
        }

        var currentIndex = SelectedPoi is null ? -1 : Pois.IndexOf(SelectedPoi);
        SelectedPoi = Pois[(currentIndex + 1) % Pois.Count];
        return Task.CompletedTask;
    }

    private Task ToggleHeatmapAsync()
    {
        IsHeatmapVisible = !IsHeatmapVisible;
        return Task.CompletedTask;
    }

    private void ApplySearch()
    {
        if (string.IsNullOrWhiteSpace(SearchText))
        {
            SearchResults.ReplaceRange(Pois);
            SelectedPoi ??= Pois.FirstOrDefault();
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
        OnPropertyChanged(nameof(SelectedPoiTitle));
        OnPropertyChanged(nameof(SelectedPoiDescription));
        OnPropertyChanged(nameof(SelectedPoiAddress));
    }
}
