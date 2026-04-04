using System.Collections.ObjectModel;
using Microsoft.Maui.ApplicationModel;
using VinhKhanh.MobileApp.Helpers;
using VinhKhanh.MobileApp.Models;
using VinhKhanh.MobileApp.Services;

namespace VinhKhanh.MobileApp.ViewModels;

public sealed class LanguageSelectionViewModel : BaseViewModel
{
    private readonly IFoodStreetDataService _dataService;
    private readonly IAppLanguageService _languageService;
    private string? _pendingQrCode;

    public LanguageSelectionViewModel(
        IFoodStreetDataService dataService,
        IAppLanguageService languageService)
    {
        _dataService = dataService;
        _languageService = languageService;
        _languageService.LanguageChanged += (_, _) =>
            MainThread.BeginInvokeOnMainThread(async () =>
            {
                await LoadAsync();
                RefreshLocalizedTexts();
            });
    }

    public ObservableCollection<LanguageOption> Languages { get; } = [];

    public string BackgroundImageUrl => _dataService.GetBackdropImageUrl();
    public string BrandTitleText => _languageService.GetText("brand_title");
    public string ScanSuccessText => HasPendingQrCode
        ? _languageService.GetText("qr_success_title")
        : _languageService.GetText("language_selection_title");
    public string ChooseLanguageText => HasPendingQrCode
        ? _languageService.GetText("qr_choose_language")
        : _languageService.GetText("language_selection_subtitle");
    public string ContinueText => _languageService.GetText("qr_continue");
    public string PendingLabelText => _languageService.GetText("language_selection_pending_label");
    public bool HasPendingQrCode => !string.IsNullOrWhiteSpace(_pendingQrCode);
    public string PendingQrCodeText => _pendingQrCode ?? string.Empty;

    public AsyncCommand<LanguageOption> SelectLanguageCommand => new(SelectLanguageAsync);
    public AsyncCommand ContinueCommand => new(ContinueAsync);

    public async Task LoadAsync()
    {
        Languages.ReplaceRange(await _dataService.GetLanguagesAsync());
        OnPropertyChanged(nameof(BackgroundImageUrl));
    }

    public void SetPendingQrCode(string? qrCode)
    {
        _pendingQrCode = string.IsNullOrWhiteSpace(qrCode)
            ? null
            : Uri.UnescapeDataString(qrCode.Trim());
        RefreshLocalizedTexts();
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

        await _languageService.SetLanguageAsync(language.Code);
    }

    private async Task ContinueAsync()
    {
        if (!_languageService.HasSavedLanguageSelection)
        {
            var selectedLanguageCode = Languages.FirstOrDefault(item => item.IsSelected)?.Code ?? _languageService.CurrentLanguage;
            await _languageService.SetLanguageAsync(selectedLanguageCode);
        }

        var route = AppRoutes.Root(AppRoutes.Login);
        var poiId = ResolvePoiId(_pendingQrCode);
        if (!string.IsNullOrWhiteSpace(poiId))
        {
            route = $"{AppRoutes.Root(AppRoutes.HomeMap)}?poiId={Uri.EscapeDataString(poiId)}";
        }

        await Shell.Current.GoToAsync(route);
    }

    private void RefreshLocalizedTexts()
    {
        OnPropertyChanged(nameof(BrandTitleText));
        OnPropertyChanged(nameof(ScanSuccessText));
        OnPropertyChanged(nameof(ChooseLanguageText));
        OnPropertyChanged(nameof(ContinueText));
        OnPropertyChanged(nameof(PendingLabelText));
        OnPropertyChanged(nameof(HasPendingQrCode));
        OnPropertyChanged(nameof(PendingQrCodeText));
    }

    private static string? ResolvePoiId(string? qrCode)
    {
        if (string.IsNullOrWhiteSpace(qrCode))
        {
            return null;
        }

        var trimmed = qrCode.Trim();
        if (!Uri.TryCreate(trimmed, UriKind.Absolute, out var uri))
        {
            return trimmed;
        }

        var query = uri.Query.TrimStart('?')
            .Split('&', StringSplitOptions.RemoveEmptyEntries)
            .Select(part => part.Split('=', 2))
            .FirstOrDefault(part => part.Length == 2 && string.Equals(part[0], "poiId", StringComparison.OrdinalIgnoreCase));
        if (query is not null)
        {
            return Uri.UnescapeDataString(query[1]);
        }

        var lastSegment = uri.Segments.LastOrDefault()?.Trim('/');
        return string.IsNullOrWhiteSpace(lastSegment) ? trimmed : lastSegment;
    }
}
