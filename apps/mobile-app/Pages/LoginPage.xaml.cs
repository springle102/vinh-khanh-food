using VinhKhanh.MobileApp.Helpers;
using VinhKhanh.MobileApp.ViewModels;

namespace VinhKhanh.MobileApp.Pages;

public partial class LoginPage : ContentPage
{
    private readonly LoginViewModel _viewModel;
    private readonly LocalizedPageBindingSubscription _localizedPageBinding;

    public LoginPage()
    {
        InitializeComponent();
        _viewModel = ServiceHelper.GetService<LoginViewModel>();
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
