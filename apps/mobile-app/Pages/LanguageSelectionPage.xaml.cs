using VinhKhanh.MobileApp.Helpers;
using VinhKhanh.MobileApp.ViewModels;

namespace VinhKhanh.MobileApp.Pages;

public partial class LanguageSelectionPage : ContentPage, IQueryAttributable
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

    public void ApplyQueryAttributes(IDictionary<string, object> query)
    {
        if (!query.TryGetValue("qrCode", out var qrCode))
        {
            _viewModel.SetPendingQrCode(null);
            return;
        }

        _viewModel.SetPendingQrCode(qrCode?.ToString());
    }
}
