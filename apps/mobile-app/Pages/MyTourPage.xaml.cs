using VinhKhanh.MobileApp.Helpers;
using VinhKhanh.MobileApp.ViewModels;

namespace VinhKhanh.MobileApp.Pages;

public partial class MyTourPage : ContentPage
{
    private readonly MyTourViewModel _viewModel;
    private readonly LocalizedPageBindingSubscription _localizedPageBinding;

    public MyTourPage()
    {
        InitializeComponent();
        _viewModel = ServiceHelper.GetService<MyTourViewModel>();
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
