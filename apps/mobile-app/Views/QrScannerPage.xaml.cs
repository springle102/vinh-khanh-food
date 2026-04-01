using Microsoft.Maui.ApplicationModel;
using VinhKhanh.MobileApp.Helpers;
using VinhKhanh.MobileApp.ViewModels;
using ZXing.Net.Maui;

namespace VinhKhanh.MobileApp;

public partial class QrScannerPage : ContentPage
{
    private readonly QrScannerViewModel _viewModel;
    private bool _hasNavigated;

    public QrScannerPage()
    {
        InitializeComponent();
        _viewModel = ServiceHelper.GetService<QrScannerViewModel>();
        BindingContext = _viewModel;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        _hasNavigated = false;
        var cameraStatus = await Permissions.RequestAsync<Permissions.Camera>();
        if (cameraStatus != PermissionStatus.Granted)
        {
            await DisplayAlertAsync("QR", "Bạn cần cấp quyền camera để quét QR.", "OK");
        }
    }

    private void OnBarcodesDetected(object? sender, BarcodeDetectionEventArgs e)
    {
        if (_hasNavigated)
        {
            return;
        }

        var qrCode = e.Results.FirstOrDefault()?.Value;
        if (string.IsNullOrWhiteSpace(qrCode))
        {
            return;
        }

        _hasNavigated = true;
        MainThread.BeginInvokeOnMainThread(async () => await _viewModel.NavigateFromQrAsync(qrCode));
    }

    private async void OnCloseTapped(object? sender, TappedEventArgs e)
        => await Shell.Current.GoToAsync(AppRoutes.Root(AppRoutes.Home));

    private void OnTorchTapped(object? sender, TappedEventArgs e)
        => BarcodeReader.IsTorchOn = !BarcodeReader.IsTorchOn;

    private async void OnManualTapped(object? sender, TappedEventArgs e)
    {
        var code = await DisplayPromptAsync("Mã QR", "Nhập mã QR hoặc slug địa điểm", "Mở", "Hủy", "vd: poi-bbq-night");
        if (string.IsNullOrWhiteSpace(code))
        {
            return;
        }

        _viewModel.ManualCode = code.Trim();
        _viewModel.OpenManualCommand.Execute(null);
    }
}
