using System.Collections.ObjectModel;
using VinhKhanh.MobileApp.Helpers;
using VinhKhanh.MobileApp.Models;
using VinhKhanh.MobileApp.Services;

namespace VinhKhanh.MobileApp.ViewModels;

public sealed class MyTourViewModel : LocalizedViewModelBase
{
    private readonly IFoodStreetDataService _dataService;
    private TourPlan? _tour;

    public MyTourViewModel(
        IFoodStreetDataService dataService,
        IAppLanguageService languageService)
        : base(languageService)
    {
        _dataService = dataService;
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

    public string HeaderTitleText => LanguageService.GetText("tour_title");
    public string CreateTourText => LanguageService.GetText("tour_create");
    public string CheckpointTitleText => LanguageService.GetText("tour_checkpoints");

    public double ProgressValue => Tour?.ProgressValue ?? 0;
    public string ProgressText => Tour?.ProgressText ?? "0 / 0";
    public string SummaryText => Tour?.SummaryText ?? string.Empty;

    public async Task LoadAsync()
    {
        Tour = await _dataService.GetTourPlanAsync();
        Stops.ReplaceRange(Tour.Stops);
        Checkpoints.ReplaceRange(Tour.Checkpoints);
        RefreshLocalizedBindings();
    }

    protected override async Task ReloadLocalizedStateAsync()
        => await LoadAsync();
}
