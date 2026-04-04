using Microsoft.Maui.ApplicationModel;
using VinhKhanh.MobileApp.Helpers;
using VinhKhanh.MobileApp.Services;

namespace VinhKhanh.MobileApp.ViewModels;

public sealed class QrScannerViewModel : BaseViewModel
{
    private readonly IAppLanguageService _languageService;

    public QrScannerViewModel(IAppLanguageService languageService)
    {
        _languageService = languageService;
        _languageService.LanguageChanged += (_, _) =>
            MainThread.BeginInvokeOnMainThread(RefreshLocalizedTexts);
    }

    public string ScreenTitleText => _languageService.GetText("qr_scanner_title");
    public string InstructionText => _languageService.GetText("qr_scanner_instruction");
    public string ManualEntryTitleText => _languageService.GetText("qr_scanner_manual_title");
    public string ManualEntryDescriptionText => _languageService.GetText("qr_scanner_manual_description");
    public string CameraPermissionTitleText => _languageService.GetText("qr_camera_permission_title");
    public string CameraPermissionMessageText => _languageService.GetText("qr_camera_permission_message");
    public string OkText => _languageService.GetText("common_ok");
    public string ManualPromptTitleText => _languageService.GetText("qr_manual_prompt_title");
    public string ManualPromptMessageText => _languageService.GetText("qr_manual_prompt_message");
    public string ManualPromptAcceptText => _languageService.GetText("qr_manual_prompt_accept");
    public string ManualPromptCancelText => _languageService.GetText("qr_manual_prompt_cancel");
    public string ManualPromptPlaceholderText => _languageService.GetText("qr_manual_prompt_placeholder");
    public string PendingLabelText => _languageService.GetText("language_selection_pending_label");

    public Task NavigateFromQrAsync(string? qrCode)
    {
        if (string.IsNullOrWhiteSpace(qrCode))
        {
            return Task.CompletedTask;
        }

        var route = $"{AppRoutes.Root(AppRoutes.LanguageSelection)}?qrCode={Uri.EscapeDataString(qrCode.Trim())}";
        return Shell.Current.GoToAsync(route);
    }

    private void RefreshLocalizedTexts()
    {
        OnPropertyChanged(nameof(ScreenTitleText));
        OnPropertyChanged(nameof(InstructionText));
        OnPropertyChanged(nameof(ManualEntryTitleText));
        OnPropertyChanged(nameof(ManualEntryDescriptionText));
        OnPropertyChanged(nameof(CameraPermissionTitleText));
        OnPropertyChanged(nameof(CameraPermissionMessageText));
        OnPropertyChanged(nameof(OkText));
        OnPropertyChanged(nameof(ManualPromptTitleText));
        OnPropertyChanged(nameof(ManualPromptMessageText));
        OnPropertyChanged(nameof(ManualPromptAcceptText));
        OnPropertyChanged(nameof(ManualPromptCancelText));
        OnPropertyChanged(nameof(ManualPromptPlaceholderText));
        OnPropertyChanged(nameof(PendingLabelText));
    }
}
