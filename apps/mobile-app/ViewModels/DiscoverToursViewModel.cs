using System.Collections.ObjectModel;
using VinhKhanh.MobileApp.Helpers;
using VinhKhanh.MobileApp.Models;
using VinhKhanh.MobileApp.Services;

namespace VinhKhanh.MobileApp.ViewModels;

public sealed class TourDiscoverCardViewModel
{
    public string Id { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string Theme { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public string SupportingText { get; init; } = string.Empty;
    public string DurationText { get; init; } = string.Empty;
    public string StopCountText { get; init; } = string.Empty;
    public string StatusText { get; init; } = string.Empty;
    public string ProgressText { get; init; } = string.Empty;
    public string HighlightStopsText { get; init; } = string.Empty;
    public string PrimaryActionText { get; init; } = string.Empty;
    public string SecondaryActionText { get; init; } = string.Empty;
    public bool IsActiveSession { get; init; }

    public bool HasTheme
        => !string.IsNullOrWhiteSpace(Theme) &&
           !string.Equals(Theme, Name, StringComparison.OrdinalIgnoreCase);

    public bool HasSupportingText => !string.IsNullOrWhiteSpace(SupportingText);
    public bool HasStatusText => !string.IsNullOrWhiteSpace(StatusText);
    public bool HasProgressText => !string.IsNullOrWhiteSpace(ProgressText);
    public bool HasHighlightStops => !string.IsNullOrWhiteSpace(HighlightStopsText);

    public string StatusBackgroundColor => IsActiveSession ? "#FFF4E8" : "#F5EFE8";
    public string StatusTextColor => IsActiveSession ? "#A85D10" : "#7A5130";
}

public sealed class DiscoverToursViewModel : LocalizedViewModelBase
{
    private readonly IFoodStreetDataService _dataService;
    private readonly ITourStateService _tourStateService;
    private TourDiscoverCardViewModel? _activeTour;

    public DiscoverToursViewModel(
        IFoodStreetDataService dataService,
        IAppLanguageService languageService,
        ITourStateService tourStateService)
        : base(languageService)
    {
        _dataService = dataService;
        _tourStateService = tourStateService;
        BackCommand = new(GoBackAsync);
        ResumeActiveTourCommand = new(ResumeActiveTourAsync);
        OpenMyTourCommand = new(OpenMyTourAsync);
        PrimaryActionCommand = new(ExecutePrimaryActionAsync);
        SecondaryActionCommand = new(ExecuteSecondaryActionAsync);
    }

    public ObservableCollection<TourDiscoverCardViewModel> Tours { get; } = [];

    public TourDiscoverCardViewModel? ActiveTour
    {
        get => _activeTour;
        private set
        {
            if (SetProperty(ref _activeTour, value))
            {
                OnPropertyChanged(nameof(HasActiveTour));
                OnPropertyChanged(nameof(ActiveTourName));
                OnPropertyChanged(nameof(ActiveTourMetaText));
                OnPropertyChanged(nameof(ShowEmptyState));
            }
        }
    }

    public bool HasActiveTour => ActiveTour is not null;
    public bool HasTours => Tours.Count > 0;
    public bool ShowEmptyState => !IsBusy && Tours.Count == 0 && ActiveTour is null;

    public string HeaderTitleText => LanguageService.GetText("tour_discover_page_title");
    public string HeaderSubtitleText => LanguageService.GetText("tour_discover_page_subtitle");
    public string ActiveSectionTitleText => LanguageService.GetText("tour_discover_active_title");
    public string ActiveSectionSubtitleText => LanguageService.GetText("tour_discover_active_subtitle");
    public string SectionTitleText => LanguageService.GetText("tour_discover_section_title");
    public string HighlightsTitleText => LanguageService.GetText("tour_discover_highlights");
    public string EmptyTitleText => LanguageService.GetText("tour_discover_empty_title");
    public string EmptySubtitleText => LanguageService.GetText("tour_discover_empty_subtitle");
    public string ResumeOnMapText => LanguageService.GetText("tour_action_resume_on_map");
    public string OpenMyTourText => LanguageService.GetText("tour_action_open_my_tour");
    public string SavedProgressText => LanguageService.GetText("tour_status_active_saved");

    public string ActiveTourName => ActiveTour?.Name ?? string.Empty;

    public string ActiveTourMetaText
    {
        get
        {
            if (ActiveTour is null)
            {
                return string.Empty;
            }

            var segments = new[]
            {
                ActiveTour.DurationText,
                ActiveTour.StopCountText,
                ActiveTour.ProgressText
            };

            return string.Join(" • ", segments.Where(item => !string.IsNullOrWhiteSpace(item)));
        }
    }

    public AsyncCommand BackCommand { get; }
    public AsyncCommand ResumeActiveTourCommand { get; }
    public AsyncCommand OpenMyTourCommand { get; }
    public AsyncCommand<TourDiscoverCardViewModel> PrimaryActionCommand { get; }
    public AsyncCommand<TourDiscoverCardViewModel> SecondaryActionCommand { get; }

    public async Task LoadAsync()
    {
        if (IsBusy)
        {
            return;
        }

        IsBusy = true;
        OnPropertyChanged(nameof(ShowEmptyState));
        try
        {
            await _dataService.RefreshDataIfChangedAsync();
            var publishedTours = await _dataService.GetPublishedToursAsync();
            var activeSession = await _tourStateService.GetActiveTourAsync();
            var cards = new List<TourDiscoverCardViewModel>();

            foreach (var tour in publishedTours)
            {
                var isActiveSession = activeSession is not null &&
                    string.Equals(activeSession.TourId, tour.Id, StringComparison.OrdinalIgnoreCase);
                var plan = await _dataService.GetTourPlanAsync(
                    tour.Id,
                    isActiveSession ? activeSession?.CompletedPoiIds : null);

                cards.Add(CreateCard(tour, plan, isActiveSession));
            }

            var activeCard = cards.FirstOrDefault(item => item.IsActiveSession);
            if (activeCard is not null)
            {
                cards.Remove(activeCard);
            }

            Tours.ReplaceRange(cards);
            ActiveTour = activeCard;
            OnPropertyChanged(nameof(HasTours));
            OnPropertyChanged(nameof(ShowEmptyState));
        }
        finally
        {
            IsBusy = false;
            OnPropertyChanged(nameof(ShowEmptyState));
            RefreshLocalizedBindings();
        }
    }

    protected override async Task ReloadLocalizedStateAsync()
        => await LoadAsync();

    private async Task ExecutePrimaryActionAsync(TourDiscoverCardViewModel? tour)
    {
        if (tour is null)
        {
            return;
        }

        if (tour.IsActiveSession)
        {
            await ResumeActiveTourAsync();
            return;
        }

        await NavigateToMapAsync($"startTourId={Uri.EscapeDataString(tour.Id)}");
    }

    private async Task ExecuteSecondaryActionAsync(TourDiscoverCardViewModel? tour)
    {
        if (tour is null)
        {
            return;
        }

        if (tour.IsActiveSession)
        {
            await OpenMyTourAsync();
            return;
        }

        await NavigateToMapAsync($"tourPreviewId={Uri.EscapeDataString(tour.Id)}");
    }

    private async Task ResumeActiveTourAsync()
    {
        if (!HasActiveTour)
        {
            return;
        }

        await NavigateToMapAsync("resumeActiveTour=true");
    }

    private async Task OpenMyTourAsync()
    {
        if (Shell.Current is null)
        {
            return;
        }

        await Shell.Current.GoToAsync(AppRoutes.MyTour);
    }

    private async Task GoBackAsync()
    {
        if (Shell.Current is null)
        {
            return;
        }

        try
        {
            await Shell.Current.GoToAsync("..");
        }
        catch
        {
            await Shell.Current.GoToAsync(AppRoutes.Root(AppRoutes.HomeMap));
        }
    }

    private async Task NavigateToMapAsync(string query)
    {
        if (Shell.Current is null)
        {
            return;
        }

        await Shell.Current.GoToAsync($"{AppRoutes.Root(AppRoutes.HomeMap)}?{query}");
    }

    private TourDiscoverCardViewModel CreateCard(TourCatalogItem catalog, TourPlan plan, bool isActiveSession)
    {
        var description = FirstNonEmpty(plan.Description, catalog.Description, plan.SummaryText);
        var supportingText = FirstNonEmpty(
            string.Equals(catalog.Description, description, StringComparison.OrdinalIgnoreCase)
                ? null
                : catalog.Description,
            string.Equals(plan.SummaryText, description, StringComparison.OrdinalIgnoreCase)
                ? null
                : plan.SummaryText);
        var highlightStopsText = string.Join(
            " • ",
            plan.Checkpoints
                .Take(3)
                .Select(item => $"{item.Order}. {item.Title}"));

        return new TourDiscoverCardViewModel
        {
            Id = catalog.Id,
            Name = FirstNonEmpty(catalog.Name, plan.Title),
            Theme = FirstNonEmpty(catalog.Theme, plan.Theme),
            Description = description,
            SupportingText = supportingText,
            DurationText = FirstNonEmpty(plan.DurationText, catalog.DurationText),
            StopCountText = FirstNonEmpty(catalog.StopCountText),
            StatusText = isActiveSession
                ? LanguageService.GetText("tour_status_active_saved")
                : string.Empty,
            ProgressText = isActiveSession ? plan.ProgressText : string.Empty,
            HighlightStopsText = highlightStopsText,
            PrimaryActionText = LanguageService.GetText(
                isActiveSession
                    ? "tour_discover_action_continue"
                    : "tour_action_start"),
            SecondaryActionText = LanguageService.GetText(
                isActiveSession
                    ? "tour_action_open_my_tour"
                    : "tour_discover_action_view_map"),
            IsActiveSession = isActiveSession
        };
    }

    private static string FirstNonEmpty(params string?[] values)
        => values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))?.Trim() ?? string.Empty;
}
