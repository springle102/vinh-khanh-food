using System.ComponentModel;
using System.Globalization;
using System.Text;
using System.Text.Json;
using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Storage;
using VinhKhanh.MobileApp.Helpers;
using VinhKhanh.MobileApp.ViewModels;

namespace VinhKhanh.MobileApp;

public partial class MapPage : ContentPage
{
    private const string MapTemplateFileName = "openstreetmap-map.html";
    private const string LeafletCssFileName = "leaflet.css";
    private const string LeafletJsFileName = "leaflet.js";
    private const string MapStatePlaceholder = "__MAP_STATE_BASE64__";
    private const string LeafletCssPlaceholder = "__LEAFLET_CSS__";
    private const string LeafletJsPlaceholder = "__LEAFLET_JS__";

    private readonly JsonSerializerOptions _jsonOptions = new(JsonSerializerDefaults.Web);
    private readonly MapViewModel _viewModel;
    private bool _isMapReady;

    public MapPage()
    {
        InitializeComponent();
        _viewModel = ServiceHelper.GetService<MapViewModel>();
        BindingContext = _viewModel;
        _viewModel.PropertyChanged += OnViewModelPropertyChanged;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await _viewModel.LoadAsync();
        await RenderMapAsync();
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        _isMapReady = false;
    }

    private async void OnMapNavigated(object? sender, WebNavigatedEventArgs e)
    {
        if (e.Result != WebNavigationResult.Success)
        {
            return;
        }

        _isMapReady = true;
        await RefreshMapSelectionAsync();
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
        if (string.IsNullOrWhiteSpace(poiId))
        {
            return;
        }

        var poi = _viewModel.Pois.FirstOrDefault(item => string.Equals(item.Id, poiId, StringComparison.Ordinal));
        if (poi is null)
        {
            return;
        }

        await _viewModel.SelectPoiAsync(poi);
        await RefreshMapSelectionAsync();
    }

    private async void OnLocateTapped(object? sender, TappedEventArgs e)
    {
        if (_viewModel.CurrentLocation is not null)
        {
            await FlyToCoordinateAsync(_viewModel.CurrentLocation.Latitude, _viewModel.CurrentLocation.Longitude, 16);
            return;
        }

        if (_viewModel.SelectedPoi is not null)
        {
            await FlyToCoordinateAsync(_viewModel.SelectedPoi.Latitude, _viewModel.SelectedPoi.Longitude, 16);
        }
    }

    private async Task RenderMapAsync()
    {
        _isMapReady = false;
        var html = await BuildMapHtmlAsync();
        MapWebView.Source = new HtmlWebViewSource
        {
            Html = html
        };
    }

    private async Task<string> BuildMapHtmlAsync()
    {
        var template = await ReadRawAssetTextAsync(MapTemplateFileName);
        var leafletCss = await ReadRawAssetTextAsync(LeafletCssFileName);
        var leafletJs = await ReadRawAssetTextAsync(LeafletJsFileName);

        var payload = new MapPageState
        {
            CurrentLocation = _viewModel.CurrentLocation is null
                ? null
                : new MapCoordinate
                {
                    Latitude = _viewModel.CurrentLocation.Latitude,
                    Longitude = _viewModel.CurrentLocation.Longitude
                },
            SelectedPoiId = _viewModel.SelectedPoi?.Id,
            Pois = _viewModel.Pois.Select(poi => new MapPoiMarker
            {
                Id = poi.Id,
                Title = poi.Title,
                Address = poi.Address,
                Description = poi.ShortDescription,
                Category = poi.Category,
                Latitude = poi.Latitude,
                Longitude = poi.Longitude,
                IsFeatured = poi.IsFeatured
            }).ToList()
        };

        var json = JsonSerializer.Serialize(payload, _jsonOptions);
        var encodedJson = Convert.ToBase64String(Encoding.UTF8.GetBytes(json));
        return template
            .Replace(LeafletCssPlaceholder, leafletCss, StringComparison.Ordinal)
            .Replace(LeafletJsPlaceholder, leafletJs, StringComparison.Ordinal)
            .Replace(MapStatePlaceholder, encodedJson, StringComparison.Ordinal);
    }

    private static async Task<string> ReadRawAssetTextAsync(string fileName)
    {
        await using var stream = await FileSystem.OpenAppPackageFileAsync(fileName);
        using var reader = new StreamReader(stream);
        return await reader.ReadToEndAsync();
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(MapViewModel.SelectedPoi) or nameof(MapViewModel.CurrentLocation) or "")
        {
            MainThread.BeginInvokeOnMainThread(() => _ = RefreshMapSelectionAsync());
        }
    }

    private async Task RefreshMapSelectionAsync()
    {
        if (!_isMapReady)
        {
            return;
        }

        await UpdateSelectedPoiAsync();
        await UpdateCurrentLocationAsync();
    }

    private async Task UpdateSelectedPoiAsync()
    {
        var selectedPoiIdLiteral = _viewModel.SelectedPoi?.Id is null
            ? "null"
            : JsonSerializer.Serialize(_viewModel.SelectedPoi.Id, _jsonOptions);

        await MapWebView.EvaluateJavaScriptAsync($"window.vkFoodMap && window.vkFoodMap.selectPoi({selectedPoiIdLiteral});");
    }

    private async Task UpdateCurrentLocationAsync()
    {
        if (_viewModel.CurrentLocation is null)
        {
            await MapWebView.EvaluateJavaScriptAsync("window.vkFoodMap && window.vkFoodMap.setCurrentLocation(null, null);");
            return;
        }

        await FlyToCoordinateAsync(_viewModel.CurrentLocation.Latitude, _viewModel.CurrentLocation.Longitude, null, shouldAnimate: false, updateCurrentLocationOnly: true);
    }

    private async Task FlyToCoordinateAsync(double latitude, double longitude, int? zoom, bool shouldAnimate = true, bool updateCurrentLocationOnly = false)
    {
        if (!_isMapReady)
        {
            return;
        }

        var latitudeLiteral = latitude.ToString(CultureInfo.InvariantCulture);
        var longitudeLiteral = longitude.ToString(CultureInfo.InvariantCulture);

        await MapWebView.EvaluateJavaScriptAsync($"window.vkFoodMap && window.vkFoodMap.setCurrentLocation({latitudeLiteral}, {longitudeLiteral});");

        if (updateCurrentLocationOnly)
        {
            return;
        }

        var zoomLiteral = zoom.HasValue ? zoom.Value.ToString(CultureInfo.InvariantCulture) : "null";
        var animateLiteral = shouldAnimate ? "true" : "false";
        await MapWebView.EvaluateJavaScriptAsync($"window.vkFoodMap && window.vkFoodMap.flyToCoordinate({latitudeLiteral}, {longitudeLiteral}, {zoomLiteral}, {animateLiteral});");
    }

    private sealed class MapPageState
    {
        public List<MapPoiMarker> Pois { get; set; } = [];
        public MapCoordinate? CurrentLocation { get; set; }
        public string? SelectedPoiId { get; set; }
    }

    private sealed class MapPoiMarker
    {
        public string Id { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public string Address { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string Category { get; set; } = string.Empty;
        public double Latitude { get; set; }
        public double Longitude { get; set; }
        public bool IsFeatured { get; set; }
    }

    private sealed class MapCoordinate
    {
        public double Latitude { get; set; }
        public double Longitude { get; set; }
    }
}
