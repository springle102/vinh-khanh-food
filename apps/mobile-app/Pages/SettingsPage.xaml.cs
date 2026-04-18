using VinhKhanh.MobileApp.Helpers;
using VinhKhanh.MobileApp.ViewModels;

namespace VinhKhanh.MobileApp.Pages;

public partial class SettingsPage : ContentPage, IQueryAttributable
{
    private readonly SettingsViewModel _viewModel;
    private readonly LocalizedPageBindingSubscription _localizedPageBinding;
    private bool _shouldAutoDownload;

    public SettingsPage()
    {
        InitializeComponent();
        _viewModel = ServiceHelper.GetService<SettingsViewModel>();
        BindingContext = _viewModel;
        _localizedPageBinding = new(this);
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        _localizedPageBinding.Rebind();
        await _viewModel.LoadAsync();
        if (_shouldAutoDownload)
        {
            _shouldAutoDownload = false;
            await _viewModel.StartOfflineDownloadAsync();
        }
    }

    public void ApplyQueryAttributes(IDictionary<string, object> query)
    {
        _shouldAutoDownload =
            query.TryGetValue("autoDownload", out var autoDownloadValue) &&
            bool.TryParse(autoDownloadValue?.ToString(), out var shouldAutoDownload) &&
            shouldAutoDownload;
    }
}
