using VinhKhanh.MobileApp.Helpers;
using VinhKhanh.MobileApp.ViewModels;

namespace VinhKhanh.MobileApp.Pages;

public partial class DiscoverToursPage : ContentPage
{
    private readonly DiscoverToursViewModel _viewModel;
    private readonly LocalizedPageBindingSubscription _localizedPageBinding;

    public DiscoverToursPage()
    {
        InitializeComponent();
        _viewModel = ServiceHelper.GetService<DiscoverToursViewModel>();
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
