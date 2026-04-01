using VinhKhanh.MobileApp.Helpers;
using VinhKhanh.MobileApp.ViewModels;

namespace VinhKhanh.MobileApp;

public partial class SplashPage : ContentPage
{
    private readonly SplashViewModel _viewModel;
    private bool _initialized;

    public SplashPage()
    {
        InitializeComponent();
        _viewModel = ServiceHelper.GetService<SplashViewModel>();
        BindingContext = _viewModel;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        if (_initialized)
        {
            return;
        }

        _initialized = true;
        var route = await _viewModel.InitializeAsync();
        await Shell.Current.GoToAsync(route);
    }
}
