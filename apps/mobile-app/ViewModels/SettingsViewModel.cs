using System.Collections.ObjectModel;
using VinhKhanh.MobileApp.Helpers;
using VinhKhanh.MobileApp.Models;
using VinhKhanh.MobileApp.Services;

namespace VinhKhanh.MobileApp.ViewModels;

public sealed class SettingsViewModel : LocalizedViewModelBase
{
    private readonly IFoodStreetDataService _dataService;
    private readonly IAutoNarrationService _autoNarrationService;
    private readonly AsyncCommand<LanguageOption> _selectLanguageCommand;
    private bool _isAutoNarrationEnabled;
    private bool _isRestoringSettingsState;

    public SettingsViewModel(
        IFoodStreetDataService dataService,
        IAppLanguageService languageService,
        IAutoNarrationService autoNarrationService)
        : base(languageService)
    {
        _dataService = dataService;
        _autoNarrationService = autoNarrationService;
        _selectLanguageCommand = new(SelectLanguageAsync);
    }

    public ObservableCollection<LanguageOption> Languages { get; } = [];
    public ObservableCollection<SettingsMenuItem> MenuItems { get; } = [];

    public bool IsAutoNarrationEnabled
    {
        get => _isAutoNarrationEnabled;
        set
        {
            if (!SetProperty(ref _isAutoNarrationEnabled, value) || _isRestoringSettingsState)
            {
                return;
            }

            _ = _autoNarrationService.SetEnabledAsync(value);
        }
    }

    public string HeaderTitleText => LanguageService.GetText("settings_title");
    public string LanguageTitleText => LanguageService.GetText("settings_language_title");
    public string AutoNarrationTitleText => LanguageService.GetText("settings_auto_narration_title");
    public string AutoNarrationDescriptionText => LanguageService.GetText("settings_auto_narration_description");
    public string PublicModeTitleText => LanguageService.GetText("brand_title");
    public string PublicModeDescriptionText => LanguageService.GetText("settings_public_mode_description");
    public string MoreTitleText => LanguageService.GetText("settings_more_title");
    public string PremiumBadgeText => LanguageService.GetText("premium_badge");

    public AsyncCommand<LanguageOption> SelectLanguageCommand => _selectLanguageCommand;

    public async Task LoadAsync()
        => await LoadSettingsAsync();

    protected override async Task ReloadLocalizedStateAsync()
    {
        RestoreAutoNarrationState();
        SyncSelectedLanguage();
        MenuItems.ReplaceRange(await _dataService.GetSettingsMenuAsync());
    }

    private async Task LoadSettingsAsync()
    {
        RestoreAutoNarrationState();
        await _dataService.EnsureAllowedLanguageSelectionAsync();
        Languages.ReplaceRange(await _dataService.GetLanguagesAsync());
        MenuItems.ReplaceRange(await _dataService.GetSettingsMenuAsync());
        RefreshLocalizedBindings();
    }

    private void RestoreAutoNarrationState()
    {
        _isRestoringSettingsState = true;
        IsAutoNarrationEnabled = _autoNarrationService.IsEnabled;
        _isRestoringSettingsState = false;
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

    private async Task SelectLanguageAsync(LanguageOption? language)
    {
        if (language is null)
        {
            return;
        }

        if (language.IsLocked)
        {
            await ShowLockedLanguageMessageAsync(language);
            Languages.ReplaceRange(await _dataService.GetLanguagesAsync());
            return;
        }

        var normalizedCode = AppLanguage.NormalizeCode(language.Code);
        if (string.Equals(normalizedCode, LanguageService.CurrentLanguage, StringComparison.OrdinalIgnoreCase))
        {
            Languages.ReplaceRange(await _dataService.GetLanguagesAsync());
            return;
        }

        foreach (var item in Languages)
        {
            item.IsSelected = string.Equals(
                AppLanguage.NormalizeCode(item.Code),
                normalizedCode,
                StringComparison.OrdinalIgnoreCase);
        }

        await LanguageService.SetLanguageAsync(normalizedCode);
    }

    private async Task ShowLockedLanguageMessageAsync(LanguageOption language)
    {
        if (Shell.Current is null)
        {
            return;
        }

        var premiumOffer = await _dataService.GetPremiumOfferAsync();
        var message = string.Format(
            LanguageService.CurrentCulture,
            LanguageService.GetText("premium_upgrade_required_message"),
            language.DisplayName,
            premiumOffer.PriceUsd);

        await Shell.Current.DisplayAlertAsync(
            LanguageService.GetText("premium_upgrade_required_title"),
            message,
            LanguageService.GetText("common_ok"));
    }
}
