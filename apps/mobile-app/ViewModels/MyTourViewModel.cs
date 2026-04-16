using System.Collections.ObjectModel;
using VinhKhanh.MobileApp.Helpers;
using VinhKhanh.MobileApp.Models;
using VinhKhanh.MobileApp.Services;

namespace VinhKhanh.MobileApp.ViewModels;

public sealed class MyTourViewModel : LocalizedViewModelBase
{
    private readonly IFoodStreetDataService _dataService;
    private readonly ITourStateService _tourStateService;
    private TourPlan? _tour;

    public MyTourViewModel(
        IFoodStreetDataService dataService,
        IAppLanguageService languageService,
        ITourStateService tourStateService)
        : base(languageService)
    {
        _dataService = dataService;
        _tourStateService = tourStateService;
        OpenMapCommand = new(OpenMapAsync);
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
    public string CreateTourText => LanguageService.GetText("tour_action_resume_on_map");
    public string CheckpointTitleText => LanguageService.GetText("tour_checkpoints");

    public double ProgressValue => Tour?.ProgressValue ?? 0;
    public string ProgressText => Tour?.ProgressText ?? "0 / 0";
    public string SummaryText => Tour?.SummaryText ?? string.Empty;
    public AsyncCommand OpenMapCommand { get; }

    public async Task LoadAsync()
        => await ReloadTourAsync(refreshDataIfChanged: true);

    protected override async Task ReloadLocalizedStateAsync()
        => await ReloadTourAsync(refreshDataIfChanged: false);

    private async Task ReloadTourAsync(bool refreshDataIfChanged)
    {
        if (refreshDataIfChanged)
        {
            await _dataService.RefreshDataIfChangedAsync();
        }

        var activeSession = await _tourStateService.GetActiveTourAsync();
        Tour = activeSession is not null && !string.IsNullOrWhiteSpace(activeSession.TourId)
            ? await _dataService.GetTourPlanAsync(activeSession.TourId, activeSession.CompletedPoiIds)
            : await _dataService.GetTourPlanAsync();
        Stops.ReplaceRange(Tour.Stops);
        Checkpoints.ReplaceRange(Tour.Checkpoints);
        RefreshLocalizedBindings();
    }

    private async Task OpenMapAsync()
    {
        if (Shell.Current is null)
        {
            return;
        }

        await Shell.Current.GoToAsync($"{AppRoutes.Root(AppRoutes.HomeMap)}?resumeActiveTour=true");
    }
}
