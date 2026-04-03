using System.Collections.ObjectModel;
using Microsoft.Maui.ApplicationModel;
using VinhKhanh.MobileApp.Helpers;
using VinhKhanh.MobileApp.Models;
using VinhKhanh.MobileApp.Services;

namespace VinhKhanh.MobileApp.ViewModels;

public sealed class SettingsViewModel : BaseViewModel
{
    private readonly IFoodStreetDataService _dataService;
    private readonly IAppLanguageService _languageService;
    private UserProfileCard? _profile;

    public SettingsViewModel(
        IFoodStreetDataService dataService,
        IAppLanguageService languageService)
    {
        _dataService = dataService;
        _languageService = languageService;
        _languageService.LanguageChanged += (_, _) =>
            MainThread.BeginInvokeOnMainThread(async () => await RefreshLocalizedStateAsync());
    }

    public ObservableCollection<LanguageOption> Languages { get; } = [];
    public ObservableCollection<SettingsMenuItem> MenuItems { get; } = [];

    public UserProfileCard? Profile
    {
        get => _profile;
        private set => SetProperty(ref _profile, value);
    }

    public string HeaderTitleText => _languageService.GetText("settings_title");
    public string AccountTitleText => _languageService.GetText("settings_account");
    public string LanguageTitleText => _languageService.GetText("qr_choose_language");
    public string UserNameLabelText => _languageService.GetText("settings_user_name");
    public string ContactLabelText => _languageService.GetText("settings_contact");
    public string LogoutText => _languageService.GetText("settings_logout");

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

        OnPropertyChanged(nameof(HeaderTitleText));
        OnPropertyChanged(nameof(AccountTitleText));
        OnPropertyChanged(nameof(LanguageTitleText));
        OnPropertyChanged(nameof(UserNameLabelText));
        OnPropertyChanged(nameof(ContactLabelText));
        OnPropertyChanged(nameof(LogoutText));
    }

    private async Task SelectLanguageAsync(LanguageOption? language)
    {
        if (language is null)
        {
            return;
        }

        await _languageService.SetLanguageAsync(language.Code);
    }
}
