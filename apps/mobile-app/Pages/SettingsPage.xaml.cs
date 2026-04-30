using Microsoft.Extensions.Logging;
using VinhKhanh.MobileApp.Helpers;
using VinhKhanh.MobileApp.ViewModels;

namespace VinhKhanh.MobileApp.Pages;

public partial class SettingsPage : ContentPage
{
    private readonly SettingsViewModel _viewModel;
    private readonly LocalizedPageBindingSubscription _localizedPageBinding;
    private readonly ILogger<SettingsPage> _logger;

    public SettingsPage()
    {
        InitializeComponent();
        _viewModel = ServiceHelper.GetService<SettingsViewModel>();
        _logger = ServiceHelper.GetService<ILogger<SettingsPage>>();
        BindingContext = _viewModel;
        _localizedPageBinding = new(this);
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        _localizedPageBinding.Rebind();
        try
        {
            await _viewModel.LoadAsync();
            _viewModel.StartLiveRefresh();
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Settings page failed to load.");
            _viewModel.StartLiveRefresh();
        }
    }

    protected override void OnDisappearing()
    {
        _viewModel.StopLiveRefresh();
        base.OnDisappearing();
    }
}
