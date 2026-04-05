using System.Collections.ObjectModel;
using VinhKhanh.MobileApp.Helpers;
using VinhKhanh.MobileApp.Models;
using VinhKhanh.MobileApp.Services;

namespace VinhKhanh.MobileApp.ViewModels;

public sealed class SettingsViewModel : LocalizedViewModelBase
{
    private readonly IFoodStreetDataService _dataService;
    private UserProfileCard? _profile;

    public SettingsViewModel(
        IFoodStreetDataService dataService,
        IAppLanguageService languageService)
        : base(languageService)
    {
        _dataService = dataService;
    }

    public ObservableCollection<LanguageOption> Languages { get; } = [];
    public ObservableCollection<SettingsMenuItem> MenuItems { get; } = [];

    public UserProfileCard? Profile
    {
        get => _profile;
        private set => SetProperty(ref _profile, value);
    }

    public string HeaderTitleText => LanguageService.GetText("settings_title");
    public string AccountTitleText => LanguageService.GetText("settings_account");
    public string LanguageTitleText => LanguageService.GetText("settings_language_title");
    public string UserNameLabelText => LanguageService.GetText("settings_user_name");
    public string ContactLabelText => LanguageService.GetText("settings_contact");
    public string LogoutText => LanguageService.GetText("settings_logout");

    public AsyncCommand<LanguageOption> SelectLanguageCommand => new(SelectLanguageAsync);
    public AsyncCommand LogoutCommand => new(() => Shell.Current.GoToAsync(AppRoutes.Root(AppRoutes.Login)));

    public async Task LoadAsync()
    {
        await RefreshLocalizedStateAsync();
    }

    private async Task RefreshLocalizedStateAsync()
    {
        Profile = await _dataService.GetUserProfileAsync();
        Languages.ReplaceRange(await _dataService.GetLanguagesAsync());
        MenuItems.ReplaceRange(await _dataService.GetSettingsMenuAsync());
        RefreshLocalizedBindings();
    }

    private async Task SelectLanguageAsync(LanguageOption? language)
    {
        if (language is null)
        {
            return;
        }

        await LanguageService.SetLanguageAsync(language.Code);
    }

    protected override async Task ReloadLocalizedStateAsync()
        => await RefreshLocalizedStateAsync();
}
