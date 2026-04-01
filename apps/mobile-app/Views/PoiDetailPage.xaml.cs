using VinhKhanh.MobileApp.Helpers;
using VinhKhanh.MobileApp.ViewModels;

namespace VinhKhanh.MobileApp;

[QueryProperty(nameof(PoiId), "poiId")]
[QueryProperty(nameof(Slug), "slug")]
[QueryProperty(nameof(QrCode), "qrCode")]
public partial class PoiDetailPage : ContentPage
{
    private readonly PoiDetailViewModel _viewModel;
    private string? _poiId;
    private string? _slug;
    private string? _qrCode;

    public string? PoiId
    {
        get => _poiId;
        set => _poiId = Uri.UnescapeDataString(value ?? string.Empty);
    }

    public string? Slug
    {
        get => _slug;
        set => _slug = Uri.UnescapeDataString(value ?? string.Empty);
    }

    public string? QrCode
    {
        get => _qrCode;
        set => _qrCode = Uri.UnescapeDataString(value ?? string.Empty);
    }

    public PoiDetailPage()
    {
        InitializeComponent();
        _viewModel = ServiceHelper.GetService<PoiDetailViewModel>();
        BindingContext = _viewModel;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await _viewModel.LoadByIdAsync(_poiId, _slug, _qrCode);
    }

    private async void OnBackTapped(object? sender, TappedEventArgs e)
    {
        if (Shell.Current.Navigation.NavigationStack.Count > 1)
        {
            await Shell.Current.GoToAsync("..");
            return;
        }

        await Shell.Current.GoToAsync(AppRoutes.Root(AppRoutes.Home));
    }
}
