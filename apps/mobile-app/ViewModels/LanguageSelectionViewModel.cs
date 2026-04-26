using System.Collections.ObjectModel;
using VinhKhanh.MobileApp.Helpers;
using VinhKhanh.MobileApp.Models;
using VinhKhanh.MobileApp.Services;

namespace VinhKhanh.MobileApp.ViewModels;

public sealed class LanguageSelectionViewModel : LocalizedViewModelBase
{
    private readonly IFoodStreetDataService _dataService;

    public LanguageSelectionViewModel(
        IFoodStreetDataService dataService,
        IAppLanguageService languageService)
        : base(languageService)
    {
        _dataService = dataService;
    }

    public ObservableCollection<LanguageOption> Languages { get; } = [];

    public string BackgroundImageUrl => _dataService.GetBackdropImageUrl();
    public string BrandTitleText => LanguageService.GetText("brand_title");
    public string TitleText => LanguageService.GetText("language_selection_title");
    public string SubtitleText => LanguageService.GetText("language_selection_subtitle");
    public string ContinueText => LanguageService.GetText("common_continue");

    public AsyncCommand<LanguageOption> SelectLanguageCommand => new(SelectLanguageAsync);
    public AsyncCommand ContinueCommand => new(ContinueAsync);

    public async Task LoadAsync()
    {
        await RefreshAsync();
        OnPropertyChanged(nameof(BackgroundImageUrl));
    }

    protected override Task ReloadLocalizedStateAsync()
    {
        SyncSelectedLanguage();
        return Task.CompletedTask;
    }

    private async Task RefreshAsync()
    {
        await _dataService.EnsureAllowedLanguageSelectionAsync();
        Languages.ReplaceRange(await _dataService.GetLanguagesAsync());
        SyncSelectedLanguage();
        RefreshLocalizedBindings();
    }

    private async Task SelectLanguageAsync(LanguageOption? language)
    {
        if (language is null)
        {
            return;
        }

        foreach (var item in Languages)
        {
            item.IsSelected = string.Equals(item.Code, language.Code, StringComparison.OrdinalIgnoreCase);
        }

        await LanguageService.SetLanguageAsync(language.Code);
    }

    private async Task ContinueAsync()
    {
        if (!LanguageService.HasSavedLanguageSelection)
        {
            var selectedLanguageCode = Languages.FirstOrDefault(item => item.IsSelected)?.Code ?? LanguageService.CurrentLanguage;
            await LanguageService.SetLanguageAsync(selectedLanguageCode);
        }

        await Shell.Current.GoToAsync(AppRoutes.Root(AppRoutes.HomeMap));
    }

    private void SyncSelectedLanguage()
    {
        var currentLanguageCode = AppLanguage.NormalizeCode(LanguageService.CurrentLanguage);
        foreach (var language in Languages)
        {
            language.IsSelected = string.Equals(
                AppLanguage.NormalizeCode(language.Code),
                currentLanguageCode,
                StringComparison.OrdinalIgnoreCase);
        }
    }
}
