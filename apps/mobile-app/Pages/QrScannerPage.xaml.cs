using Microsoft.Maui.ApplicationModel;
using VinhKhanh.MobileApp.Helpers;
using VinhKhanh.MobileApp.ViewModels;
using ZXing.Net.Maui;

namespace VinhKhanh.MobileApp.Pages;

public partial class QrScannerPage : ContentPage
{
    private readonly QrScannerViewModel _viewModel;
    private readonly LocalizedPageBindingSubscription _localizedPageBinding;
    private bool _hasNavigated;

    public QrScannerPage()
    {
        InitializeComponent();
        _viewModel = ServiceHelper.GetService<QrScannerViewModel>();
        BindingContext = _viewModel;
        _localizedPageBinding = new(this);
        BarcodeReader.Options = new BarcodeReaderOptions
        {
            AutoRotate = true,
            Formats = BarcodeFormats.TwoDimensional,
            Multiple = false
        };
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        _localizedPageBinding.Rebind();
        _hasNavigated = false;

        var cameraStatus = await Permissions.RequestAsync<Permissions.Camera>();
        if (cameraStatus != PermissionStatus.Granted)
        {
            await DisplayAlertAsync(
                _viewModel.CameraPermissionTitleText,
                _viewModel.CameraPermissionMessageText,
                _viewModel.OkText);
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
        => await Shell.Current.GoToAsync(AppRoutes.Root(AppRoutes.HomeMap));

    private void OnTorchTapped(object? sender, TappedEventArgs e)
        => BarcodeReader.IsTorchOn = !BarcodeReader.IsTorchOn;

    private async void OnManualTapped(object? sender, TappedEventArgs e)
    {
        var code = await DisplayPromptAsync(
            _viewModel.ManualPromptTitleText,
            _viewModel.ManualPromptMessageText,
            _viewModel.ManualPromptAcceptText,
            _viewModel.ManualPromptCancelText,
            _viewModel.ManualPromptPlaceholderText);
        if (string.IsNullOrWhiteSpace(code))
        {
            return;
        }

        _hasNavigated = true;
        await _viewModel.NavigateFromQrAsync(code.Trim());
    }
}
