using System.ComponentModel;
using System.Globalization;
using System.Text;
using System.Text.Json;
using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.Storage;
using VinhKhanh.MobileApp.Helpers;
using VinhKhanh.MobileApp.ViewModels;

namespace VinhKhanh.MobileApp.Pages;

public partial class HomeMapPage : ContentPage
{
    private const string MapTemplateFileName = "openstreetmap-map.html";

    private readonly JsonSerializerOptions _jsonOptions = new(JsonSerializerDefaults.Web);
    private readonly HomeMapViewModel _viewModel;
    private bool _isMapReady;

    public HomeMapPage()
    {
        InitializeComponent();
        _viewModel = ServiceHelper.GetService<HomeMapViewModel>();
        BindingContext = _viewModel;
        _viewModel.PropertyChanged += OnViewModelPropertyChanged;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await _viewModel.LoadAsync();
        await RenderMapAsync();
    }

    private async void OnMapNavigated(object? sender, WebNavigatedEventArgs e)
    {
        if (e.Result != WebNavigationResult.Success)
        {
            return;
        }

        _isMapReady = true;
        await RefreshMapStateAsync();
    }

    private async void OnMapNavigating(object? sender, WebNavigatingEventArgs e)
    {
        if (!Uri.TryCreate(e.Url, UriKind.Absolute, out var uri) ||
            !string.Equals(uri.Scheme, "vkfood", StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(uri.Host, "select-poi", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        e.Cancel = true;
        var poiId = Uri.UnescapeDataString(uri.AbsolutePath.Trim('/'));
        if (!string.IsNullOrWhiteSpace(poiId))
        {
            await _viewModel.SelectPoiByIdAsync(poiId);
        }
    }

    private async void OnToggleHeatmapTapped(object? sender, TappedEventArgs e)
    {
        await _viewModel.ToggleHeatmapCommand.ExecuteAsync();
    }

    private async void OnCenterTapped(object? sender, TappedEventArgs e)
    {
        if (_viewModel.SelectedPoi is not null)
        {
            await FlyToCoordinateAsync(_viewModel.SelectedPoi.Latitude, _viewModel.SelectedPoi.Longitude, 17);
        }
    }

    private async void OnNextPoiTapped(object? sender, TappedEventArgs e)
    {
        await _viewModel.SelectNextPoiCommand.ExecuteAsync();
    }

    private async Task RenderMapAsync()
    {
        _isMapReady = false;
        await using var stream = await FileSystem.OpenAppPackageFileAsync(MapTemplateFileName);
        using var reader = new StreamReader(stream);
        var template = await reader.ReadToEndAsync();

        var payload = new
        {
            selectedPoiId = _viewModel.SelectedPoi?.Id,
            isHeatmapVisible = _viewModel.IsHeatmapVisible,
            pois = _viewModel.Pois.Select(poi => new
            {
                id = poi.Id,
                title = poi.Title,
                description = poi.ShortDescription,
                address = poi.Address,
                category = poi.Category,
                status = poi.IsFeatured ? "featured" : "standard",
                latitude = poi.Latitude,
                longitude = poi.Longitude,
                isFeatured = poi.IsFeatured,
                heatIntensity = poi.HeatIntensity
            }),
            heatPoints = _viewModel.HeatPoints.Select(point => new
            {
                latitude = point.Latitude,
                longitude = point.Longitude,
                intensity = point.Intensity
            })
        };

        var json = JsonSerializer.Serialize(payload, _jsonOptions);
        var encodedJson = Convert.ToBase64String(Encoding.UTF8.GetBytes(json));
        MapWebView.Source = new HtmlWebViewSource
        {
            Html = template.Replace("__MAP_STATE_BASE64__", encodedJson, StringComparison.Ordinal)
        };
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(HomeMapViewModel.SelectedPoi) or nameof(HomeMapViewModel.IsHeatmapVisible) or "")
        {
            MainThread.BeginInvokeOnMainThread(() => _ = RefreshMapStateAsync());
        }
    }

    private async Task RefreshMapStateAsync()
    {
        if (!_isMapReady)
        {
            return;
        }

        var selectedPoiIdLiteral = _viewModel.SelectedPoi?.Id is null
            ? "null"
            : JsonSerializer.Serialize(_viewModel.SelectedPoi.Id, _jsonOptions);
        await MapWebView.EvaluateJavaScriptAsync($"window.vkFoodMap && window.vkFoodMap.selectPoi({selectedPoiIdLiteral});");
        await MapWebView.EvaluateJavaScriptAsync($"window.vkFoodMap && window.vkFoodMap.setHeatmapVisible({(_viewModel.IsHeatmapVisible ? "true" : "false")});");
    }

    private async Task FlyToCoordinateAsync(double latitude, double longitude, int zoom)
    {
        if (!_isMapReady)
        {
            return;
        }

        var latitudeLiteral = latitude.ToString(CultureInfo.InvariantCulture);
        var longitudeLiteral = longitude.ToString(CultureInfo.InvariantCulture);
        await MapWebView.EvaluateJavaScriptAsync($"window.vkFoodMap && window.vkFoodMap.flyToCoordinate({latitudeLiteral}, {longitudeLiteral}, {zoom}, true);");
    }
}
