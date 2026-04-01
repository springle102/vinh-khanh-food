using System.Collections.ObjectModel;
using VinhKhanh.MobileApp.Helpers;
using VinhKhanh.MobileApp.Models;
using VinhKhanh.MobileApp.Services;

namespace VinhKhanh.MobileApp.ViewModels;

public sealed class MyTourViewModel : BaseViewModel
{
    private readonly IFoodStreetDataService _dataService;
    private readonly IAppLanguageService _languageService;
    private string _loadedLanguage = string.Empty;
    private TourPlan? _tour;

    public MyTourViewModel(
        IFoodStreetDataService dataService,
        IAppLanguageService languageService)
    {
        _dataService = dataService;
        _languageService = languageService;
        _languageService.LanguageChanged += (_, _) => RefreshLocalizedTexts();
    }

    public ObservableCollection<TourStop> Stops { get; } = [];
    public ObservableCollection<TourCheckpoint> Checkpoints { get; } = [];

    public TourPlan? Tour
    {
        get => _tour;
        private set
        {
            if (SetProperty(ref _tour, value))
            {
                OnPropertyChanged(nameof(ProgressValue));
                OnPropertyChanged(nameof(ProgressText));
                OnPropertyChanged(nameof(SummaryText));
            }
        }
    }

    public string HeaderTitleText => _languageService.GetText("tour_title");
    public string CreateTourText => _languageService.GetText("tour_create");
    public string CheckpointTitleText => _languageService.GetText("tour_checkpoints");

    public double ProgressValue => Tour?.ProgressValue ?? 0;
    public string ProgressText => Tour?.ProgressText ?? "0 / 0";
    public string SummaryText => Tour?.SummaryText ?? string.Empty;

    public async Task LoadAsync()
    {
        if (Tour is null || !string.Equals(_loadedLanguage, _languageService.CurrentLanguage, StringComparison.OrdinalIgnoreCase))
        {
            Tour = await _dataService.GetTourPlanAsync();
            Stops.ReplaceRange(Tour.Stops);
            Checkpoints.ReplaceRange(Tour.Checkpoints);
            _loadedLanguage = _languageService.CurrentLanguage;
        }

        RefreshLocalizedTexts();
    }

    private void RefreshLocalizedTexts()
    {
        OnPropertyChanged(nameof(HeaderTitleText));
        OnPropertyChanged(nameof(CreateTourText));
        OnPropertyChanged(nameof(CheckpointTitleText));
    }
}
