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
    public ObservableCollection<SettingsMenuItem> MenuItems { get; } = [];

    public string HeaderTitleText => LanguageService.GetText("settings_title");
    public string LanguageTitleText => LanguageService.GetText("settings_language_title");
    public string PublicModeTitleText => LanguageService.GetText("brand_title");
    public string PublicModeDescriptionText => AppLanguage.NormalizeCode(LanguageService.CurrentLanguage) switch
    {
        "vi" => "Ung dung mo truc tiep vao ban do POI, khong can dang nhap va khong luu tai khoan khach hang.",
        "en" => "The app opens straight to the POI map, with no sign-in and no customer account required.",
        "zh-CN" => "应用会直接打开 POI 地图，无需登录，也不需要客户账号。",
        "ko" => "앱은 바로 POI 지도로 열리며 로그인이나 고객 계정이 필요하지 않습니다.",
        "ja" => "アプリはそのまま POI マップを開き、ログインや利用者アカウントは不要です。",
        _ => "The app opens straight to the POI map, with no sign-in and no customer account required."
    };
    public string MoreTitleText => AppLanguage.NormalizeCode(LanguageService.CurrentLanguage) switch
    {
        "vi" => "Thong tin them",
        "en" => "More information",
        "zh-CN" => "更多信息",
        "ko" => "추가 정보",
        "ja" => "追加情報",
        _ => "More information"
    };

    public AsyncCommand<LanguageOption> SelectLanguageCommand => _selectLanguageCommand;

    public async Task LoadAsync()
    {
        await RefreshLocalizedStateAsync();
    }

    protected override async Task ReloadLocalizedStateAsync()
        => await RefreshLocalizedStateAsync();

    private async Task RefreshLocalizedStateAsync()
    {
        await _dataService.EnsureAllowedLanguageSelectionAsync();
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

        foreach (var item in Languages)
        {
            item.IsSelected = string.Equals(item.Code, language.Code, StringComparison.OrdinalIgnoreCase);
        }

        await LanguageService.SetLanguageAsync(language.Code);
    }
}
