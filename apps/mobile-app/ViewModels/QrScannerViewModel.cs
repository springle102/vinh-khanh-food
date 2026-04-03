using VinhKhanh.MobileApp.Helpers;

namespace VinhKhanh.MobileApp.ViewModels;

public sealed class QrScannerViewModel : BaseViewModel
{
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
