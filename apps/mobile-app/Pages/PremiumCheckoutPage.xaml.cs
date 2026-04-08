using VinhKhanh.MobileApp.Helpers;
using VinhKhanh.MobileApp.ViewModels;

namespace VinhKhanh.MobileApp.Pages;

public partial class PremiumCheckoutPage : ContentPage, IQueryAttributable
{
    private readonly PremiumCheckoutViewModel _viewModel;

    public PremiumCheckoutPage()
    {
        InitializeComponent();
        _viewModel = ServiceHelper.GetService<PremiumCheckoutViewModel>();
        BindingContext = _viewModel;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await _viewModel.LoadAsync();
    }

    public void ApplyQueryAttributes(IDictionary<string, object> query)
    {
        query.TryGetValue("source", out var source);
        query.TryGetValue("preferredLanguageCode", out var preferredLanguageCode);
        query.TryGetValue("pendingPoiId", out var pendingPoiId);

        _viewModel.SetNavigationContext(
            source?.ToString(),
            preferredLanguageCode?.ToString(),
            pendingPoiId?.ToString());
    }
}
