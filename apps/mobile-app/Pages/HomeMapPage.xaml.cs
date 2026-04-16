using System.ComponentModel;
using System.Globalization;
using System.Text;
using System.Text.Json;
using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.Storage;
using VinhKhanh.MobileApp.Helpers;
using VinhKhanh.MobileApp.ViewModels;

namespace VinhKhanh.MobileApp.Pages;

public partial class HomeMapPage : ContentPage, IQueryAttributable
{
    private const string MapTemplateFileName = "openstreetmap-map.html";
    private const string LeafletCssFileName = "leaflet.css";
    private const string LeafletJsFileName = "leaflet.js";
    private const string MapStatePlaceholder = "__MAP_STATE_BASE64__";
    private const string LeafletCssPlaceholder = "__LEAFLET_CSS__";
    private const string LeafletJsPlaceholder = "__LEAFLET_JS__";
    private const string TourPanelHeightAnimationName = "HomeMapPage.TourPanel.Height";
    private static readonly TimeSpan AutoRefreshInterval = TimeSpan.FromSeconds(5);
    private const uint TourPanelAnimationDuration = 240;
    private const double TourPanelCollapsedHeightMin = 118;
    private const double TourPanelCollapsedHeightMax = 156;
    private const double TourPanelExpandedHeightPreviewMin = 218;
    private const double TourPanelExpandedHeightActiveMin = 264;
    private const double TourPanelExpandedHeightMax = 336;
    private const double TourPanelEntranceOffset = 28;
    private const double BrowseViewportTopInset = 180;
    private const double BrowseViewportBottomInset = 168;
    private const double TourViewportTopInset = 230;
    private const double TourViewportBottomInsetFallback = 176;

    private readonly JsonSerializerOptions _jsonOptions = new(JsonSerializerDefaults.Web);
    private readonly HomeMapViewModel _viewModel;
    private readonly LocalizedPageBindingSubscription _localizedPageBinding;
    private bool _isMapReady;
    private bool _isTourPanelPresented;
    private bool _isTourPanelExpanded;
    private bool _isTourPanelPanning;
    private CancellationTokenSource? _autoRefreshLoopCancellation;
    private string? _pendingPoiId;
    private string? _pendingPreviewTourId;
    private string? _pendingStartTourId;
    private bool _shouldResumeActiveTour;
    private double _tourPanelCollapsedHeight = 148;
    private double _tourPanelExpandedHeight = 286;
    private double _tourPanelPanStartHeight = 148;
    private double _lastAllocatedWidth;
    private double _lastAllocatedHeight;
    private string _tourPanelToken = string.Empty;

    public HomeMapPage()
    {
        InitializeComponent();
        _viewModel = ServiceHelper.GetService<HomeMapViewModel>();
        BindingContext = _viewModel;
        _localizedPageBinding = new(this);
        _viewModel.PropertyChanged += OnViewModelPropertyChanged;
        InitializeTourPanelVisualState();
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        _localizedPageBinding.Rebind();
        _viewModel.ActivateNarrationContext();
        StartAutoRefreshLoop();
        await _viewModel.LoadAsync();
        if (!string.IsNullOrWhiteSpace(_pendingStartTourId))
        {
            await _viewModel.StartTourByIdAsync(_pendingStartTourId);
            _pendingStartTourId = null;
            _pendingPreviewTourId = null;
            _shouldResumeActiveTour = false;
        }
        else if (!string.IsNullOrWhiteSpace(_pendingPreviewTourId))
        {
            await _viewModel.PreviewTourByIdAsync(_pendingPreviewTourId);
            _pendingPreviewTourId = null;
            _shouldResumeActiveTour = false;
        }
        else if (_shouldResumeActiveTour)
        {
            await _viewModel.ResumeActiveTourAsync();
            _shouldResumeActiveTour = false;
        }

        if (!string.IsNullOrWhiteSpace(_pendingPoiId))
        {
            await _viewModel.SelectPoiByIdAsync(_pendingPoiId);
            _pendingPoiId = null;
        }

        await _viewModel.StartLocationTrackingAsync();
        await RenderMapAsync();
        await PoiDetailBottomSheet.SetPresentedAsync(_viewModel.IsBottomSheetVisible);
        await SyncTourPanelAsync(animated: false, refitTour: false);
    }

    protected override async void OnDisappearing()
    {
        base.OnDisappearing();
        StopAutoRefreshLoop();
        AbortTourPanelAnimations();
        await _viewModel.StopLocationTrackingAsync();
        await _viewModel.SuspendNarrationAsync();
    }

    protected override void OnSizeAllocated(double width, double height)
    {
        base.OnSizeAllocated(width, height);

        if (width <= 0 || height <= 0)
        {
            return;
        }

        if (Math.Abs(width - _lastAllocatedWidth) < 0.5 &&
            Math.Abs(height - _lastAllocatedHeight) < 0.5)
        {
            return;
        }

        _lastAllocatedWidth = width;
        _lastAllocatedHeight = height;

        MainThread.BeginInvokeOnMainThread(() => _ = RefreshTourPanelLayoutAsync(
            refitTour: TourPanelOverlay.IsVisible && _isTourPanelExpanded && _viewModel.HasVisibleTour));
    }

    private void StartAutoRefreshLoop()
    {
        StopAutoRefreshLoop();
        _autoRefreshLoopCancellation = new CancellationTokenSource();
        _ = RunAutoRefreshLoopAsync(_autoRefreshLoopCancellation.Token);
    }

    private void StopAutoRefreshLoop()
    {
        _autoRefreshLoopCancellation?.Cancel();
        _autoRefreshLoopCancellation?.Dispose();
        _autoRefreshLoopCancellation = null;
    }

    private async Task RunAutoRefreshLoopAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var timer = new PeriodicTimer(AutoRefreshInterval);
            while (await timer.WaitForNextTickAsync(cancellationToken))
            {
                await MainThread.InvokeOnMainThreadAsync(() => _viewModel.RefreshIfNeededAsync());
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
    }

    public void ApplyQueryAttributes(IDictionary<string, object> query)
    {
        if (query.TryGetValue("poiId", out var poiId))
        {
            _pendingPoiId = poiId?.ToString();
            if (!string.IsNullOrWhiteSpace(_pendingPoiId))
            {
                _pendingPoiId = Uri.UnescapeDataString(_pendingPoiId);
            }
        }

        if (query.TryGetValue("tourPreviewId", out var previewTourId))
        {
            _pendingPreviewTourId = previewTourId?.ToString();
            if (!string.IsNullOrWhiteSpace(_pendingPreviewTourId))
            {
                _pendingPreviewTourId = Uri.UnescapeDataString(_pendingPreviewTourId);
            }
        }

        if (query.TryGetValue("startTourId", out var startTourId))
        {
            _pendingStartTourId = startTourId?.ToString();
            if (!string.IsNullOrWhiteSpace(_pendingStartTourId))
            {
                _pendingStartTourId = Uri.UnescapeDataString(_pendingStartTourId);
            }
        }

        _shouldResumeActiveTour =
            query.TryGetValue("resumeActiveTour", out var resumeActiveTour) &&
            bool.TryParse(resumeActiveTour?.ToString(), out var shouldResume) &&
            shouldResume;
    }

    private async void OnMapNavigated(object? sender, WebNavigatedEventArgs e)
    {
        if (e.Result != WebNavigationResult.Success)
        {
            return;
        }

        _isMapReady = true;
        await RefreshMapStateAsync();
        await UpdateMapViewportInsetsAsync(refitTour: _viewModel.HasVisibleTour && _viewModel.IsTourPanelVisible);
        if (_viewModel.HasSimulationRoute)
        {
            await FitToSimulationRouteAsync();
        }
    }

    private async void OnMapNavigating(object? sender, WebNavigatingEventArgs e)
    {
        if (!Uri.TryCreate(e.Url, UriKind.Absolute, out var uri) ||
            !string.Equals(uri.Scheme, "vkfood", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        e.Cancel = true;

        if (string.Equals(uri.Host, "dismiss-detail", StringComparison.OrdinalIgnoreCase))
        {
            await _viewModel.CloseBottomSheetCommand.ExecuteAsync();
            return;
        }

        if (string.Equals(uri.Host, "set-mock-location", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(uri.Host, "set-simulated-location", StringComparison.OrdinalIgnoreCase))
        {
            if (TryReadCoordinate(uri, "lat", out var latitude) &&
                TryReadCoordinate(uri, "lng", out var longitude))
            {
                await _viewModel.SetMockLocationAsync(latitude, longitude);
            }

            return;
        }

        if (!string.Equals(uri.Host, "select-poi", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var poiId = Uri.UnescapeDataString(uri.AbsolutePath.Trim('/'));
        if (!string.IsNullOrWhiteSpace(poiId))
        {
            await _viewModel.SelectPoiByIdAsync(poiId);
        }
    }

    private async void OnCenterTapped(object? sender, TappedEventArgs e)
    {
        if (_viewModel.SelectedPoi is not null)
        {
            await FlyToCoordinateAsync(_viewModel.SelectedPoi.Latitude, _viewModel.SelectedPoi.Longitude, 17);
            return;
        }

        var userLocation = _viewModel.GetMapUserLocationState();
        if (userLocation.Latitude is double latitude &&
            userLocation.Longitude is double longitude)
        {
            await FlyToCoordinateAsync(latitude, longitude, 18);
        }
    }

    private async void OnNextPoiTapped(object? sender, TappedEventArgs e)
    {
        await _viewModel.SelectNextPoiCommand.ExecuteAsync();
    }

    private async void OnTourPanelHeaderTapped(object? sender, TappedEventArgs e)
    {
        if (!TourPanelOverlay.IsVisible || _isTourPanelPanning)
        {
            return;
        }

        await SetTourPanelExpandedAsync(!_isTourPanelExpanded, animated: true, refitTour: true);
    }

    private async void OnTourPanelPanUpdated(object? sender, PanUpdatedEventArgs e)
    {
        if (!TourPanelOverlay.IsVisible)
        {
            return;
        }

        switch (e.StatusType)
        {
            case GestureStatus.Started:
                EnsureTourPanelBounds();
                _isTourPanelPanning = true;
                _tourPanelPanStartHeight = ResolveCurrentTourPanelHeight();
                AbortTourPanelAnimations();
                TourPanelExpandedContent.IsVisible = true;
                break;

            case GestureStatus.Running:
                if (!_isTourPanelPanning)
                {
                    return;
                }

                var nextHeight = Math.Clamp(
                    _tourPanelPanStartHeight - e.TotalY,
                    _tourPanelCollapsedHeight,
                    _tourPanelExpandedHeight);
                TourPanelCard.HeightRequest = nextHeight;
                ApplyTourPanelExpansionProgress(CalculateTourPanelProgress(nextHeight));
                break;

            case GestureStatus.Completed:
                _isTourPanelPanning = false;
                await SnapTourPanelAfterPanAsync(e.TotalY);
                break;

            case GestureStatus.Canceled:
                _isTourPanelPanning = false;
                await SetTourPanelExpandedAsync(_isTourPanelExpanded, animated: true, refitTour: true);
                break;
        }
    }

    private void InitializeTourPanelVisualState()
    {
        TourPanelOverlay.IsVisible = false;
        TourPanelOverlay.InputTransparent = true;
        TourPanelCard.HeightRequest = _tourPanelCollapsedHeight;
        TourPanelCard.MaximumHeightRequest = _tourPanelExpandedHeight;
        TourPanelCard.Opacity = 0;
        TourPanelCard.TranslationY = TourPanelEntranceOffset;
        TourPanelExpandedContent.IsVisible = false;
        TourPanelExpandedContent.Opacity = 0;
        TourPanelExpandedContent.TranslationY = 10;
    }

    private async Task SyncTourPanelAsync(bool animated, bool refitTour)
    {
        var nextToken = ResolveTourPanelToken();
        if (!string.Equals(_tourPanelToken, nextToken, StringComparison.Ordinal))
        {
            _tourPanelToken = nextToken;
            _isTourPanelExpanded = false;
        }

        if (_viewModel.IsTourPanelVisible)
        {
            if (_isTourPanelPresented && TourPanelOverlay.IsVisible)
            {
                await RefreshTourPanelLayoutAsync(refitTour);
                return;
            }

            await PresentTourPanelAsync(animated, refitTour);
            return;
        }

        await HideTourPanelAsync(animated, refitTour);
    }

    private async Task PresentTourPanelAsync(bool animated, bool refitTour)
    {
        EnsureTourPanelBounds();
        _isTourPanelPresented = true;
        TourPanelOverlay.InputTransparent = false;

        var targetHeight = _isTourPanelExpanded ? _tourPanelExpandedHeight : _tourPanelCollapsedHeight;
        TourPanelCard.HeightRequest = targetHeight;
        ApplyTourPanelExpansionProgress(_isTourPanelExpanded ? 1 : 0);

        if (TourPanelOverlay.IsVisible)
        {
            await UpdateMapViewportInsetsAsync(refitTour);
            return;
        }

        AbortTourPanelAnimations();
        TourPanelOverlay.IsVisible = true;

        if (!animated)
        {
            TourPanelCard.Opacity = 1;
            TourPanelCard.TranslationY = 0;
            await UpdateMapViewportInsetsAsync(refitTour);
            return;
        }

        TourPanelCard.Opacity = 0;
        TourPanelCard.TranslationY = TourPanelEntranceOffset;
        await Task.WhenAll(
            TourPanelCard.FadeToAsync(1, 170, Easing.CubicOut),
            TourPanelCard.TranslateToAsync(0, 0, 230, Easing.CubicOut));
        await UpdateMapViewportInsetsAsync(refitTour);
    }

    private async Task HideTourPanelAsync(bool animated, bool refitTour)
    {
        _isTourPanelPresented = false;
        _isTourPanelExpanded = false;
        _isTourPanelPanning = false;
        _tourPanelToken = ResolveTourPanelToken();

        if (!TourPanelOverlay.IsVisible)
        {
            await UpdateMapViewportInsetsAsync(refitTour: false);
            return;
        }

        AbortTourPanelAnimations();
        TourPanelOverlay.InputTransparent = true;

        if (animated)
        {
            await Task.WhenAll(
                TourPanelCard.FadeToAsync(0, 120, Easing.CubicIn),
                TourPanelCard.TranslateToAsync(0, TourPanelEntranceOffset, 180, Easing.CubicIn));
        }
        else
        {
            TourPanelCard.Opacity = 0;
            TourPanelCard.TranslationY = TourPanelEntranceOffset;
        }

        TourPanelOverlay.IsVisible = false;
        TourPanelCard.HeightRequest = _tourPanelCollapsedHeight;
        ApplyTourPanelExpansionProgress(0);
        TourPanelExpandedContent.IsVisible = false;
        await UpdateMapViewportInsetsAsync(refitTour && _viewModel.HasVisibleTour);
    }

    private async Task RefreshTourPanelLayoutAsync(bool refitTour)
    {
        if (!TourPanelOverlay.IsVisible)
        {
            await UpdateMapViewportInsetsAsync(refitTour: false);
            return;
        }

        var nextToken = ResolveTourPanelToken();
        if (!string.Equals(_tourPanelToken, nextToken, StringComparison.Ordinal))
        {
            _tourPanelToken = nextToken;
            _isTourPanelExpanded = false;
        }

        EnsureTourPanelBounds();
        TourPanelCard.HeightRequest = _isTourPanelExpanded ? _tourPanelExpandedHeight : _tourPanelCollapsedHeight;
        ApplyTourPanelExpansionProgress(_isTourPanelExpanded ? 1 : 0);
        await UpdateMapViewportInsetsAsync(refitTour);
    }

    private async Task SetTourPanelExpandedAsync(bool isExpanded, bool animated, bool refitTour)
    {
        if (!TourPanelOverlay.IsVisible)
        {
            return;
        }

        EnsureTourPanelBounds();
        _isTourPanelExpanded = isExpanded;
        TourPanelExpandedContent.IsVisible = true;

        var targetHeight = isExpanded ? _tourPanelExpandedHeight : _tourPanelCollapsedHeight;
        if (animated)
        {
            await AnimateTourPanelHeightAsync(targetHeight, TourPanelAnimationDuration, Easing.CubicInOut);
        }
        else
        {
            TourPanelCard.HeightRequest = targetHeight;
            ApplyTourPanelExpansionProgress(isExpanded ? 1 : 0);
        }

        if (!isExpanded)
        {
            TourPanelExpandedContent.IsVisible = false;
        }

        await UpdateMapViewportInsetsAsync(refitTour);
    }

    private async Task SnapTourPanelAfterPanAsync(double totalY)
    {
        var currentHeight = ResolveCurrentTourPanelHeight();
        var midpoint = (_tourPanelCollapsedHeight + _tourPanelExpandedHeight) / 2;
        var shouldExpand = totalY < -32
            ? true
            : totalY > 32
                ? false
                : currentHeight >= midpoint;

        await SetTourPanelExpandedAsync(shouldExpand, animated: true, refitTour: true);
    }

    private void EnsureTourPanelBounds()
    {
        var hostHeight = Height > 0 ? Height : TourPanelOverlay.Height;
        if (hostHeight <= 0)
        {
            return;
        }

        var collapsedHeight = Math.Clamp(
            hostHeight * 0.19,
            TourPanelCollapsedHeightMin,
            TourPanelCollapsedHeightMax);
        var expandedHeight = Math.Clamp(
            hostHeight * (_viewModel.IsTourActiveMode ? 0.36 : 0.31),
            _viewModel.IsTourActiveMode ? TourPanelExpandedHeightActiveMin : TourPanelExpandedHeightPreviewMin,
            TourPanelExpandedHeightMax);

        _tourPanelExpandedHeight = Math.Max(
            expandedHeight,
            collapsedHeight + (_viewModel.IsTourActiveMode ? 120 : 86));
        _tourPanelCollapsedHeight = Math.Min(collapsedHeight, _tourPanelExpandedHeight - 72);

        TourPanelCard.MaximumHeightRequest = _tourPanelExpandedHeight;
    }

    private double ResolveCurrentTourPanelHeight()
    {
        if (TourPanelCard.Height > 0)
        {
            return TourPanelCard.Height;
        }

        if (TourPanelCard.HeightRequest > 0)
        {
            return TourPanelCard.HeightRequest;
        }

        return _tourPanelCollapsedHeight;
    }

    private double CalculateTourPanelProgress(double currentHeight)
    {
        var delta = Math.Max(1, _tourPanelExpandedHeight - _tourPanelCollapsedHeight);
        return Math.Clamp((currentHeight - _tourPanelCollapsedHeight) / delta, 0, 1);
    }

    private void ApplyTourPanelExpansionProgress(double progress)
    {
        var clampedProgress = Math.Clamp(progress, 0, 1);
        TourPanelExpandedContent.IsVisible = clampedProgress > 0.01 || _isTourPanelExpanded || _isTourPanelPanning;
        TourPanelExpandedContent.Opacity = clampedProgress;
        TourPanelExpandedContent.TranslationY = 10 * (1 - clampedProgress);
        TourPanelGrabber.Opacity = 0.68 + (clampedProgress * 0.2);

        if (clampedProgress <= 0.01 && !_isTourPanelExpanded && !_isTourPanelPanning)
        {
            TourPanelExpandedContent.IsVisible = false;
        }
    }

    private async Task AnimateTourPanelHeightAsync(double targetHeight, uint duration, Easing easing)
    {
        var startHeight = ResolveCurrentTourPanelHeight();
        if (Math.Abs(startHeight - targetHeight) < 0.5)
        {
            TourPanelCard.HeightRequest = targetHeight;
            ApplyTourPanelExpansionProgress(CalculateTourPanelProgress(targetHeight));
            return;
        }

        var completion = new TaskCompletionSource<bool>();
        TourPanelCard.AbortAnimation(TourPanelHeightAnimationName);

        var animation = new Animation(
            callback: value =>
            {
                TourPanelCard.HeightRequest = value;
                ApplyTourPanelExpansionProgress(CalculateTourPanelProgress(value));
            },
            start: startHeight,
            end: targetHeight,
            easing: easing);

        animation.Commit(
            owner: TourPanelCard,
            name: TourPanelHeightAnimationName,
            rate: 16,
            length: duration,
            easing: easing,
            finished: (_, _) =>
            {
                TourPanelCard.HeightRequest = targetHeight;
                ApplyTourPanelExpansionProgress(CalculateTourPanelProgress(targetHeight));
                completion.TrySetResult(true);
            });

        await completion.Task;
    }

    private void AbortTourPanelAnimations()
    {
        TourPanelCard.AbortAnimation(TourPanelHeightAnimationName);
        TourPanelCard.CancelAnimations();
        TourPanelExpandedContent.CancelAnimations();
    }

    private async Task UpdateMapViewportInsetsAsync(bool refitTour)
    {
        if (!_isMapReady)
        {
            return;
        }

        var tourBottomInset = _viewModel.HasVisibleTour && TourPanelOverlay.IsVisible
            ? Math.Round(ResolveCurrentTourPanelHeight() + 28)
            : TourViewportBottomInsetFallback;

        var viewportInsets = new
        {
            browseTop = BrowseViewportTopInset,
            browseBottom = BrowseViewportBottomInset,
            tourTop = TourViewportTopInset,
            tourBottom = tourBottomInset
        };

        var viewportInsetsLiteral = JsonSerializer.Serialize(viewportInsets, _jsonOptions);
        await MapWebView.EvaluateJavaScriptAsync(
            $"window.vkFoodMap && window.vkFoodMap.setViewportInsets({viewportInsetsLiteral});");

        if (refitTour && _viewModel.HasVisibleTour && !_viewModel.HasVisibleBottomSheet)
        {
            await MapWebView.EvaluateJavaScriptAsync("window.vkFoodMap && window.vkFoodMap.fitToTour();");
        }
    }

    private string ResolveTourPanelToken()
        => _viewModel.VisibleTour is null
            ? string.Empty
            : $"{_viewModel.TourMode}:{_viewModel.VisibleTour.Id}";

    private async Task RenderMapAsync()
    {
        _isMapReady = false;
        var template = await ReadRawAssetTextAsync(MapTemplateFileName);
        var leafletCss = await ReadRawAssetTextAsync(LeafletCssFileName);
        var leafletJs = await ReadRawAssetTextAsync(LeafletJsFileName);

        var payload = new
        {
            featuredLabel = _viewModel.FeaturedBadgeText,
            selectedPoiId = _viewModel.SelectedPoi?.Id,
            activePoiId = _viewModel.CurrentActivePoiId,
            userLocation = _viewModel.GetMapUserLocationState(),
            allowLocationMockSelection = true,
            routeSimulation = _viewModel.GetMapRouteSimulationState(),
            currentTour = _viewModel.GetCurrentTourMapState(),
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
                isFeatured = poi.IsFeatured
            })
        };

        var json = JsonSerializer.Serialize(payload, _jsonOptions);
        var encodedJson = Convert.ToBase64String(Encoding.UTF8.GetBytes(json));
        MapWebView.Source = new HtmlWebViewSource
        {
            Html = template
                .Replace(LeafletCssPlaceholder, leafletCss, StringComparison.Ordinal)
                .Replace(LeafletJsPlaceholder, leafletJs, StringComparison.Ordinal)
                .Replace(MapStatePlaceholder, encodedJson, StringComparison.Ordinal)
        };
    }

    private static async Task<string> ReadRawAssetTextAsync(string fileName)
    {
        await using var stream = await FileSystem.OpenAppPackageFileAsync(fileName);
        using var reader = new StreamReader(stream);
        return await reader.ReadToEndAsync();
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(HomeMapViewModel.MapDataVersion):
                MainThread.BeginInvokeOnMainThread(() => _ = RenderMapAsync());
                break;

            case nameof(HomeMapViewModel.SelectedPoi):
                MainThread.BeginInvokeOnMainThread(async () =>
                {
                    await RefreshMapStateAsync();
                    if (_viewModel.SelectedPoi is not null)
                    {
                        await FlyToCoordinateAsync(_viewModel.SelectedPoi.Latitude, _viewModel.SelectedPoi.Longitude, 17);
                    }
                });
                break;

            case nameof(HomeMapViewModel.UserLocationVersion):
                MainThread.BeginInvokeOnMainThread(() => _ = RefreshMapStateAsync());
                break;

            case nameof(HomeMapViewModel.SimulationMapVersion):
                MainThread.BeginInvokeOnMainThread(async () =>
                {
                    await RefreshMapStateAsync();
                    if (_viewModel.HasSimulationRoute)
                    {
                        await FitToSimulationRouteAsync();
                    }
                });
                break;

            case nameof(HomeMapViewModel.IsTourPanelVisible):
                MainThread.BeginInvokeOnMainThread(() => _ = SyncTourPanelAsync(animated: true, refitTour: true));
                break;

            case nameof(HomeMapViewModel.ActiveTour):
            case nameof(HomeMapViewModel.PreviewTour):
            case nameof(HomeMapViewModel.TourMode):
            case nameof(HomeMapViewModel.IsTourActiveMode):
                MainThread.BeginInvokeOnMainThread(async () =>
                {
                    await RefreshMapStateAsync();
                    await RefreshTourPanelLayoutAsync(refitTour: false);
                });
                break;

            case nameof(HomeMapViewModel.TourMapVersion):
                MainThread.BeginInvokeOnMainThread(async () =>
                {
                    await RefreshMapStateAsync();
                    await RefreshTourPanelLayoutAsync(refitTour: _viewModel.HasVisibleTour && _viewModel.IsTourPanelVisible);
                });
                break;

            case nameof(HomeMapViewModel.IsBottomSheetVisible):
                MainThread.BeginInvokeOnMainThread(() => _ = PoiDetailBottomSheet.SetPresentedAsync(_viewModel.IsBottomSheetVisible));
                break;

            case nameof(HomeMapViewModel.SelectedPoiDetail):
                MainThread.BeginInvokeOnMainThread(() => _ = PoiDetailBottomSheet.AnimateContentRefreshAsync());
                break;

            case "":
                MainThread.BeginInvokeOnMainThread(() => _ = RefreshMapStateAsync());
                break;
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
        var activePoiIdLiteral = _viewModel.CurrentActivePoiId is null
            ? "null"
            : JsonSerializer.Serialize(_viewModel.CurrentActivePoiId, _jsonOptions);
        var userLocationStateLiteral = JsonSerializer.Serialize(_viewModel.GetMapUserLocationState(), _jsonOptions);
        var routeSimulationLiteral = JsonSerializer.Serialize(_viewModel.GetMapRouteSimulationState(), _jsonOptions);
        var tourStateLiteral = JsonSerializer.Serialize(_viewModel.GetCurrentTourMapState(), _jsonOptions);
        await MapWebView.EvaluateJavaScriptAsync($"window.vkFoodMap && window.vkFoodMap.selectPoi({selectedPoiIdLiteral});");
        await MapWebView.EvaluateJavaScriptAsync($"window.vkFoodMap && window.vkFoodMap.setActivePoi({activePoiIdLiteral});");
        await MapWebView.EvaluateJavaScriptAsync($"window.vkFoodMap && window.vkFoodMap.setUserLocation({userLocationStateLiteral});");
        await MapWebView.EvaluateJavaScriptAsync($"window.vkFoodMap && window.vkFoodMap.setRouteSimulation({routeSimulationLiteral});");
        await MapWebView.EvaluateJavaScriptAsync($"window.vkFoodMap && window.vkFoodMap.setTourState({tourStateLiteral});");
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

    private async Task FitToSimulationRouteAsync()
    {
        if (!_isMapReady)
        {
            return;
        }

        await MapWebView.EvaluateJavaScriptAsync("window.vkFoodMap && window.vkFoodMap.fitToSimulationRoute();");
    }

    private static bool TryReadCoordinate(Uri uri, string key, out double value)
    {
        value = default;
        if (string.IsNullOrWhiteSpace(uri.Query))
        {
            return false;
        }

        var pairs = uri.Query.TrimStart('?')
            .Split('&', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        foreach (var pair in pairs)
        {
            var segments = pair.Split('=', 2);
            if (segments.Length != 2 ||
                !string.Equals(segments[0], key, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            return double.TryParse(
                Uri.UnescapeDataString(segments[1]),
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out value);
        }

        return false;
    }
}
