using VinhKhanh.MobileApp.Helpers;
using VinhKhanh.MobileApp.ViewModels;

namespace VinhKhanh.MobileApp;

public partial class LanguageSelectionPage : ContentPage
{
    private readonly LanguageSelectionViewModel _viewModel;
    private bool _loaded;

    public LanguageSelectionPage()
    {
        InitializeComponent();
        _viewModel = ServiceHelper.GetService<LanguageSelectionViewModel>();
        BindingContext = _viewModel;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        if (_loaded)
        {
            return;
        }

        _loaded = true;
        await _viewModel.LoadAsync();
    }
}
