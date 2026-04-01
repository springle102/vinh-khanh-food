using System.Collections.ObjectModel;
using VinhKhanh.MobileApp.Helpers;
using VinhKhanh.MobileApp.Models;
using VinhKhanh.MobileApp.Services;

namespace VinhKhanh.MobileApp.ViewModels;

public sealed class QRSuccessLanguageViewModel : BaseViewModel
{
    private readonly IFoodStreetDataService _dataService;
    private readonly IAppLanguageService _languageService;

    public QRSuccessLanguageViewModel(
        IFoodStreetDataService dataService,
        IAppLanguageService languageService)
    {
        _dataService = dataService;
        _languageService = languageService;
        _languageService.LanguageChanged += (_, _) => RefreshAllBindings();
    }

    public ObservableCollection<LanguageOption> Languages { get; } = [];

    public string BackgroundImageUrl => _dataService.GetBackdropImageUrl();
    public string ScanSuccessText => _languageService.GetText("qr_success_title");
    public string ChooseLanguageText => _languageService.GetText("qr_choose_language");
    public string ContinueText => _languageService.GetText("qr_continue");

    public AsyncCommand<LanguageOption> SelectLanguageCommand => new(SelectLanguageAsync);
    public AsyncCommand ContinueCommand => new(ContinueAsync);

    public async Task LoadAsync()
    {
        var languages = await _dataService.GetLanguagesAsync();
        Languages.ReplaceRange(languages);
    }

    private async Task SelectLanguageAsync(LanguageOption? language)
    {
        if (language is null)
        {
            return;
        }

        foreach (var item in Languages)
        {
            item.IsSelected = item.Code == language.Code;
        }

        await _languageService.SetLanguageAsync(language.Code);
        RefreshAllBindings();
    }

    private Task ContinueAsync()
        => Shell.Current.GoToAsync(AppRoutes.Root(AppRoutes.Login));
}
