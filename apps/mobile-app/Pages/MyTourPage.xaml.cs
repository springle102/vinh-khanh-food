using VinhKhanh.MobileApp.Helpers;
using VinhKhanh.MobileApp.ViewModels;

namespace VinhKhanh.MobileApp.Pages;

public partial class MyTourPage : ContentPage
{
    private readonly MyTourViewModel _viewModel;

    public MyTourPage()
    {
        InitializeComponent();
        _viewModel = ServiceHelper.GetService<MyTourViewModel>();
        BindingContext = _viewModel;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await _viewModel.LoadAsync();
    }
}
