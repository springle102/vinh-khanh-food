using System.Collections.ObjectModel;
using VinhKhanh.MobileApp.Helpers;
using VinhKhanh.MobileApp.Models;
using VinhKhanh.MobileApp.Services;

namespace VinhKhanh.MobileApp.ViewModels;

public sealed class SettingsViewModel : LocalizedViewModelBase
{
    private readonly IFoodStreetDataService _dataService;
    private readonly AsyncCommand<LanguageOption> _selectLanguageCommand;

    public SettingsViewModel(
        IFoodStreetDataService dataService,
        IAppLanguageService languageService)
        : base(languageService)
    {
        _dataService = dataService;
        _selectLanguageCommand = new(SelectLanguageAsync);
    }

    public ObservableCollection<LanguageOption> Languages { get; } = [];

    public string HeaderTitleText => LanguageService.GetText("settings_title");
    public string LanguageTitleText => LanguageService.GetText("settings_language_title");
    public string PremiumBadgeText => LanguageService.GetText("premium_badge");

    public AsyncCommand<LanguageOption> SelectLanguageCommand => _selectLanguageCommand;

    public async Task LoadAsync()
        => await LoadSettingsAsync();

    protected override async Task ReloadLocalizedStateAsync()
        => Languages.ReplaceRange(await _dataService.GetLanguagesAsync());

    private async Task LoadSettingsAsync()
    {
        await _dataService.EnsureAllowedLanguageSelectionAsync();
        Languages.ReplaceRange(await _dataService.GetLanguagesAsync());
        RefreshLocalizedBindings();
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
