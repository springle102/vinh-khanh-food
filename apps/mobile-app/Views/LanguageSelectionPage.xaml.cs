using VinhKhanh.MobileApp.Helpers;
using VinhKhanh.MobileApp.ViewModels;

namespace VinhKhanh.MobileApp;

public partial class LanguageSelectionPage : ContentPage
{
    private readonly LanguageSelectionViewModel _viewModel;

    public LanguageSelectionPage()
    {
        InitializeComponent();
        _viewModel = ServiceHelper.GetService<LanguageSelectionViewModel>();
        BindingContext = _viewModel;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await _viewModel.LoadAsync();
    }
}
