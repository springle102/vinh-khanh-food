using VinhKhanh.MobileApp.Helpers;
using VinhKhanh.MobileApp.ViewModels;

namespace VinhKhanh.MobileApp.Pages;

public partial class DiscoverToursPage : ContentPage
{
    private readonly DiscoverToursViewModel _viewModel;

    public DiscoverToursPage()
    {
        InitializeComponent();
        _viewModel = ServiceHelper.GetService<DiscoverToursViewModel>();
        BindingContext = _viewModel;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await _viewModel.LoadAsync();
    }
}
