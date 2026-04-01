using VinhKhanh.MobileApp.Helpers;
using VinhKhanh.MobileApp.ViewModels;

namespace VinhKhanh.MobileApp;

[QueryProperty(nameof(InitialSearch), "search")]
public partial class PoiListPage : ContentPage
{
    private readonly PoiListViewModel _viewModel;
    private string? _initialSearch;

    public string? InitialSearch
    {
        get => _initialSearch;
        set => _initialSearch = value;
    }

    public PoiListPage()
    {
        InitializeComponent();
        _viewModel = ServiceHelper.GetService<PoiListViewModel>();
        BindingContext = _viewModel;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await _viewModel.LoadAsync(_initialSearch);
        _initialSearch = null;
    }

    private async void OnScrollCategoriesLeft(object? sender, TappedEventArgs e)
        => await CategoryScrollView.ScrollToAsync(Math.Max(0, CategoryScrollView.ScrollX - 140), 0, true);

    private async void OnScrollCategoriesRight(object? sender, TappedEventArgs e)
        => await CategoryScrollView.ScrollToAsync(CategoryScrollView.ScrollX + 140, 0, true);
}
