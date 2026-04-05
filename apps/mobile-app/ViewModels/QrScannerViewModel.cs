using VinhKhanh.MobileApp.Helpers;
using VinhKhanh.MobileApp.Services;

namespace VinhKhanh.MobileApp.ViewModels;

public sealed class QrScannerViewModel : LocalizedViewModelBase
{
    public QrScannerViewModel(IAppLanguageService languageService)
        : base(languageService)
    {
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

    public Task NavigateFromQrAsync(string? qrCode)
    {
        if (string.IsNullOrWhiteSpace(qrCode))
        {
            return Task.CompletedTask;
        }

        var route = $"{AppRoutes.Root(AppRoutes.LanguageSelection)}?qrCode={Uri.EscapeDataString(qrCode.Trim())}";
        return Shell.Current.GoToAsync(route);
    }
}
