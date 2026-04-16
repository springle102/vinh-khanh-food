using VinhKhanh.MobileApp.Helpers;
using VinhKhanh.MobileApp.ViewModels;

namespace VinhKhanh.MobileApp.Pages;

public partial class SettingsPage : ContentPage
{
    private readonly SettingsViewModel _viewModel;
    private readonly LocalizedPageBindingSubscription _localizedPageBinding;

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
    }
}
