using VinhKhanh.MobileApp.Helpers;
using VinhKhanh.MobileApp.Services;

namespace VinhKhanh.MobileApp.ViewModels;

public sealed class QrScannerViewModel : LocalizedViewModelBase
{
    private readonly IFoodStreetDataService _dataService;

    public QrScannerViewModel(
        IFoodStreetDataService dataService,
        IAppLanguageService languageService)
        : base(languageService)
    {
        _dataService = dataService;
    }

    public string ScreenTitleText => LanguageService.GetText("qr_scanner_title");
    public string InstructionText => LanguageService.GetText("qr_scanner_instruction");
    public string ManualEntryTitleText => LanguageService.GetText("qr_scanner_manual_title");
    public string ManualEntryDescriptionText => LanguageService.GetText("qr_scanner_manual_description");
    public string CameraPermissionTitleText => LanguageService.GetText("qr_camera_permission_title");
    public string CameraPermissionMessageText => LanguageService.GetText("qr_camera_permission_message");
    public string OkText => LanguageService.GetText("common_ok");
    public string ManualPromptTitleText => LanguageService.GetText("qr_manual_prompt_title");
    public string ManualPromptMessageText => LanguageService.GetText("qr_manual_prompt_message");
    public string ManualPromptAcceptText => LanguageService.GetText("qr_manual_prompt_accept");
    public string ManualPromptCancelText => LanguageService.GetText("qr_manual_prompt_cancel");
    public string ManualPromptPlaceholderText => LanguageService.GetText("qr_manual_prompt_placeholder");
    public string PendingLabelText => LanguageService.GetText("language_selection_pending_label");

    public async Task NavigateFromQrAsync(string? qrCode)
    {
        if (string.IsNullOrWhiteSpace(qrCode))
        {
            return;
        }

        var trimmedCode = qrCode.Trim();
        var poiId = ResolvePoiId(trimmedCode);
        if (!string.IsNullOrWhiteSpace(poiId))
        {
            await _dataService.TrackQrScanAsync(poiId, trimmedCode, LanguageService.CurrentLanguage);
            await Shell.Current.GoToAsync($"{AppRoutes.Root(AppRoutes.HomeMap)}?poiId={Uri.EscapeDataString(poiId)}");
            return;
        }

        await Shell.Current.GoToAsync(AppRoutes.Root(AppRoutes.HomeMap));
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
