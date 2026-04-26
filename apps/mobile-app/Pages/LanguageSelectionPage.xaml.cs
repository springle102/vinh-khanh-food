using VinhKhanh.MobileApp.Helpers;
using VinhKhanh.MobileApp.ViewModels;

namespace VinhKhanh.MobileApp.Pages;

public partial class LanguageSelectionPage : ContentPage
{
    private readonly LanguageSelectionViewModel _viewModel;
    private readonly LocalizedPageBindingSubscription _localizedPageBinding;

    public LanguageSelectionPage()
    {
        InitializeComponent();
        _viewModel = ServiceHelper.GetService<LanguageSelectionViewModel>();
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
