using VinhKhanh.MobileApp.Helpers;
using VinhKhanh.MobileApp.ViewModels;

namespace VinhKhanh.MobileApp.Pages;

public partial class QRSuccessLanguagePage : ContentPage
{
    private readonly QRSuccessLanguageViewModel _viewModel;

    public QRSuccessLanguagePage()
    {
        InitializeComponent();
        _viewModel = ServiceHelper.GetService<QRSuccessLanguageViewModel>();
        BindingContext = _viewModel;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await _viewModel.LoadAsync();
    }
}
