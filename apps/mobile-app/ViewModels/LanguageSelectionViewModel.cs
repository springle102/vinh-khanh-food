using System.Collections.ObjectModel;
using VinhKhanh.MobileApp.Helpers;
using VinhKhanh.MobileApp.Models;
using VinhKhanh.MobileApp.Services;

namespace VinhKhanh.MobileApp.ViewModels;

public sealed class LanguageSelectionViewModel : LocalizedViewModelBase
{
    private readonly IFoodStreetDataService _dataService;
    private string? _pendingQrCode;

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
    public string ScanSuccessText => HasPendingQrCode
        ? LanguageService.GetText("qr_success_title")
        : LanguageService.GetText("language_selection_title");
    public string ChooseLanguageText => HasPendingQrCode
        ? LanguageService.GetText("qr_choose_language")
        : LanguageService.GetText("language_selection_subtitle");
    public string ContinueText => LanguageService.GetText("qr_continue");
    public string PendingLabelText => LanguageService.GetText("language_selection_pending_label");
    public bool HasPendingQrCode => !string.IsNullOrWhiteSpace(_pendingQrCode);
    public string PendingQrCodeText => _pendingQrCode ?? string.Empty;

    public AsyncCommand<LanguageOption> SelectLanguageCommand => new(SelectLanguageAsync);
    public AsyncCommand ContinueCommand => new(ContinueAsync);

    public async Task LoadAsync()
    {
        await RefreshAsync();
        OnPropertyChanged(nameof(BackgroundImageUrl));
    }

    public void SetPendingQrCode(string? qrCode)
    {
        _pendingQrCode = string.IsNullOrWhiteSpace(qrCode)
            ? null
            : Uri.UnescapeDataString(qrCode.Trim());
        RefreshLocalizedBindings();
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

        var route = AppRoutes.Root(AppRoutes.HomeMap);
        var poiId = ResolvePoiId(_pendingQrCode);
        if (!string.IsNullOrWhiteSpace(poiId))
        {
            route = $"{AppRoutes.Root(AppRoutes.HomeMap)}?poiId={Uri.EscapeDataString(poiId)}";
        }

        await Shell.Current.GoToAsync(route);
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
