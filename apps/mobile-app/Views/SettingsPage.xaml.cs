using VinhKhanh.MobileApp.Helpers;
using VinhKhanh.MobileApp.Models;
using VinhKhanh.MobileApp.ViewModels;

namespace VinhKhanh.MobileApp;

public partial class SettingsPage : ContentPage
{
    private readonly SettingsViewModel _viewModel;
    private bool _isInitializing;

    public SettingsPage()
    {
        InitializeComponent();
        _viewModel = ServiceHelper.GetService<SettingsViewModel>();
        BindingContext = _viewModel;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        _isInitializing = true;
        await _viewModel.LoadAsync();
        AutoNarrationSwitch.IsToggled = _viewModel.Settings.AutoNarrationEnabled;
        PreparedAudioSwitch.IsToggled = _viewModel.Settings.PreferPreparedAudio;
        _isInitializing = false;
    }

    private async void OnAutoNarrationToggled(object? sender, ToggledEventArgs e)
    {
        if (_isInitializing)
        {
            return;
        }

        await _viewModel.UpdateAutoNarrationAsync(e.Value);
    }

    private async void OnPreparedAudioToggled(object? sender, ToggledEventArgs e)
    {
        if (_isInitializing)
        {
            return;
        }

        await _viewModel.UpdatePreparedAudioAsync(e.Value);
    }

    private async void OnVoiceTapped(object? sender, TappedEventArgs e)
    {
        var options = _viewModel.Voices.Select(item => item.DisplayName).ToArray();
        var choice = await DisplayActionSheetAsync("Chọn giọng đọc", "Hủy", null, options);
        var selectedVoice = _viewModel.Voices.FirstOrDefault(item => string.Equals(item.DisplayName, choice, StringComparison.Ordinal));
        if (selectedVoice is null)
        {
            return;
        }

        await _viewModel.SelectVoiceAsync(selectedVoice);
    }

    private async void OnPlaceholderTapped(object? sender, TappedEventArgs e)
        => await DisplayAlertAsync("Thông báo", "Mục này mình đã giữ chỗ theo thiết kế và có thể nối logic tiếp ở bước sau.", "OK");

    private async void OnAboutTapped(object? sender, TappedEventArgs e)
        => await DisplayAlertAsync("Về ứng dụng", "Vinh Khánh Food Guide 1.0.0\nThiết kế mobile mới, backend và database giữ theo hệ thống cũ.", "OK");
}

