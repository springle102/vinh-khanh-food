using System.ComponentModel;
using System.Globalization;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.Networking;
using Microsoft.Maui.Storage;
using VinhKhanh.MobileApp.Helpers;
using VinhKhanh.MobileApp.Models;
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
    private const string MapHtmlBaseUrl = "https://appassets.vinhkhanh.local/";
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
    private const double MinMapContainerSize = 48;
    private static readonly SemaphoreSlim MapTemplateLock = new(1, 1);
    private static string? _cachedMapHtmlTemplate;

    private readonly JsonSerializerOptions _jsonOptions = new(JsonSerializerDefaults.Web);
    private readonly HomeMapViewModel _viewModel;
    private readonly LocalizedPageBindingSubscription _localizedPageBinding;
    private readonly ILogger<HomeMapPage> _logger;
    private readonly SemaphoreSlim _mapRenderLock = new(1, 1);
    private bool _isMapReady;
    private bool _isMapNavigationComplete;
    private bool _hasPendingMapContentRefresh;
    private int _mapRenderGeneration;
    private int _mapReadyGeneration;
    private int _mapAutoRetryCount;
    private string _lastMapContentSignature = string.Empty;
    private string _mapStatusKind = string.Empty;
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
        _logger = ServiceHelper.GetService<ILogger<HomeMapPage>>();
        BindingContext = _viewModel;
        _localizedPageBinding = new(this);
        _viewModel.PropertyChanged += OnViewModelPropertyChanged;
        InitializeTourPanelVisualState();
        _ = WarmMapTemplateAsync();
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        _localizedPageBinding.Rebind();
        _viewModel.ActivateNarrationContext();
        StartAutoRefreshLoop();
        ShowMapStatus(
            "Đang tải bản đồ...",
            "Ứng dụng đang tải dữ liệu POI và khởi tạo lớp bản đồ.");
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
        await EnsureMapRenderedAsync();
        await PoiDetailBottomSheet.SetPresentedAsync(_viewModel.IsBottomSheetVisible);
        await SyncTourPanelAsync(animated: false, refitTour: false);
        await InvalidateMapSizeAsync("appearing");
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

        MainThread.BeginInvokeOnMainThread(async () =>
        {
            await RefreshTourPanelLayoutAsync(
                refitTour: TourPanelOverlay.IsVisible && _isTourPanelExpanded && _viewModel.HasVisibleTour);
            await InvalidateMapSizeAsync("page-size-allocated");
        });
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
            _isMapNavigationComplete = false;
            _isMapReady = false;
            _logger.LogError("[MapInit] WebView navigation failed. result={Result}", e.Result);
            ShowMapStatus(
                "Không thể tải bản đồ",
                "WebView không mở được nội dung bản đồ. Hãy thử mở lại ứng dụng hoặc kiểm tra log dev.");
            return;
        }

        _isMapNavigationComplete = true;
        _logger.LogInformation(
            "[MapInit] WebView navigation completed. generation={Generation}; width={Width}; height={Height}",
            _mapRenderGeneration,
            MapWebView.Width,
            MapWebView.Height);
        await ProbeMapReadyAsync("webview-navigated");
    }

    private async void OnMapNavigating(object? sender, WebNavigatingEventArgs e)
    {
        if (!Uri.TryCreate(e.Url, UriKind.Absolute, out var uri) ||
            !string.Equals(uri.Scheme, "vkfood", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        e.Cancel = true;

        if (string.Equals(uri.Host, "map-event", StringComparison.OrdinalIgnoreCase))
        {
            await HandleMapEventAsync(uri);
            return;
        }

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

    private async Task HandleMapEventAsync(Uri uri)
    {
        var parameters = ParseQueryParameters(uri);
        parameters.TryGetValue("type", out var eventType);
        parameters.TryGetValue("detail", out var detail);
        eventType ??= "unknown";

        _logger.LogInformation(
            "[MapEvent] type={EventType}; detail={Detail}; generation={Generation}; ready={Ready}",
            eventType,
            detail ?? string.Empty,
            _mapRenderGeneration,
            _isMapReady);

        switch (eventType)
        {
            case "ready":
                await MarkMapReadyAsync("host-ready");
                break;

            case "tile-url":
                _logger.LogInformation("[MapTile] Active tile url. detail={Detail}", detail ?? string.Empty);
                break;

            case "tile-error":
                _logger.LogWarning("[MapTile] Tile provider reported an error. detail={Detail}", detail ?? string.Empty);
                ShowMapStatus(
                    "Không tải được lớp bản đồ",
                    "Ứng dụng vẫn giữ nền và POI nếu có dữ liệu. Vui lòng kiểm tra mạng hoặc tile provider trong log dev.",
                    "tile-error");
                break;

            case "poi-sync":
                _logger.LogInformation("[MarkerRender] Browser marker sync. detail={Detail}", detail ?? string.Empty);
                break;

            case "resize":
                _logger.LogDebug("[MapInit] Leaflet size invalidated. detail={Detail}", detail ?? string.Empty);
                break;

            case "map-error":
                _isMapReady = false;
                _logger.LogError("[MapInit] Leaflet initialization failed. detail={Detail}", detail ?? string.Empty);
                ShowMapStatus(
                    "Không thể khởi tạo bản đồ",
                    "Leaflet gặp lỗi khi render. Chi tiết đã được ghi vào log dev để xử lý dữ liệu hoặc tile.",
                    "fatal");
                if (_mapAutoRetryCount < 1)
                {
                    _mapAutoRetryCount += 1;
                    await Task.Delay(250);
                    await EnsureMapRenderedAsync(force: true);
                }
                break;
        }
    }

    private async Task MarkMapReadyAsync(string reason)
    {
        _isMapReady = true;
        _mapAutoRetryCount = 0;
        _mapReadyGeneration = _mapRenderGeneration;
        _logger.LogInformation(
            "[MapInit] Leaflet API ready. reason={Reason}; generation={Generation}; pendingRefresh={PendingRefresh}; width={Width}; height={Height}",
            reason,
            _mapReadyGeneration,
            _hasPendingMapContentRefresh,
            MapWebView.Width,
            MapWebView.Height);

        await InvalidateMapSizeAsync(reason);
        if (_hasPendingMapContentRefresh)
        {
            await RefreshMapContentAsync();
        }
        else
        {
            await RefreshMapStateAsync();
        }

        await UpdateMapViewportInsetsAsync(refitTour: _viewModel.HasVisibleTour && _viewModel.IsTourPanelVisible);
        if (_viewModel.HasSimulationRoute)
        {
            await FitToSimulationRouteAsync();
        }
    }

    private async Task ProbeMapReadyAsync(string reason)
    {
        if (!_isMapNavigationComplete)
        {
            return;
        }

        await Task.Delay(120);
        var result = await EvaluateMapScriptAsync(
            "(() => { try { return window.vkFoodMap && window.vkFoodMap.isReady && window.vkFoodMap.isReady() ? 'ready' : 'missing-map-api'; } catch (error) { return 'probe-error:' + (error && error.message ? error.message : error); } })()",
            $"probe-ready:{reason}");
        if (IsJavaScriptReady(result))
        {
            await MarkMapReadyAsync($"probe:{reason}");
            return;
        }

        _logger.LogWarning(
            "[MapInit] WebView loaded but Leaflet API is not ready yet. reason={Reason}; result={Result}",
            reason,
            result ?? "null");
        ShowMapStatus(
            "Đang khởi tạo bản đồ...",
            "WebView đã tải xong nhưng Leaflet chưa báo sẵn sàng. Ứng dụng sẽ tự thử đồng bộ lại.",
            "loading");
    }

    private async Task<string?> EvaluateMapScriptAsync(string script, string operation, bool critical = false)
    {
        try
        {
            return await MapWebView.EvaluateJavaScriptAsync(script);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[MapJs] JavaScript operation failed. operation={Operation}", operation);
            if (critical)
            {
                ShowMapStatus(
                    "Không thể cập nhật bản đồ",
                    "Một lệnh đồng bộ POI/marker bị lỗi. Chi tiết đã được ghi vào log dev.",
                    "fatal");
            }

            return null;
        }
    }

    private async Task InvalidateMapSizeAsync(string reason)
    {
        if (!_isMapReady)
        {
            return;
        }

        if (MapWebView.Width < MinMapContainerSize || MapWebView.Height < MinMapContainerSize)
        {
            _logger.LogWarning(
                "[MapInit] Map container is too small during invalidate. reason={Reason}; width={Width}; height={Height}",
                reason,
                MapWebView.Width,
                MapWebView.Height);
            return;
        }

        var reasonLiteral = JsonSerializer.Serialize(reason, _jsonOptions);
        await EvaluateMapScriptAsync(
            $"window.vkFoodMap && window.vkFoodMap.invalidateSize && window.vkFoodMap.invalidateSize({reasonLiteral});",
            $"invalidate-size:{reason}");
    }

    private void ShowMapStatus(string title, string description, string kind = "loading")
    {
        _mapStatusKind = kind;
        MapStatusTitleLabel.Text = title;
        MapStatusDescriptionLabel.Text = description;
        MapStatusOverlay.IsVisible = true;
    }

    private void HideMapStatus(params string[] allowedKinds)
    {
        if (allowedKinds.Length > 0 &&
            !allowedKinds.Any(kind => string.Equals(kind, _mapStatusKind, StringComparison.OrdinalIgnoreCase)))
        {
            return;
        }

        _mapStatusKind = string.Empty;
        MapStatusOverlay.IsVisible = false;
    }

    private void UpdateMapDataStatus(int sourcePoiCount, int validPoiCount)
    {
        if (sourcePoiCount == 0)
        {
            ShowMapStatus(
                "Chưa có dữ liệu bản đồ/POI",
                "API hoặc bộ lọc hiện tại chưa trả về POI nào. Bản đồ vẫn giữ nền thay vì trắng toàn bộ.",
                "empty");
            return;
        }

        if (validPoiCount == 0)
        {
            ShowMapStatus(
                "Chưa có POI hợp lệ để hiển thị",
                "Có dữ liệu POI nhưng tất cả tọa độ đều không hợp lệ. Kiểm tra log dev để biết POI nào bị loại.",
                "invalid-poi");
            return;
        }

        HideMapStatus("loading", "empty", "invalid-poi");
    }

    private static bool IsJavaScriptOk(string? result)
        => result is not null &&
           result.Contains("ok", StringComparison.OrdinalIgnoreCase);

    private static bool IsJavaScriptReady(string? result)
        => result is not null &&
           result.Contains("ready", StringComparison.OrdinalIgnoreCase);

    private static Dictionary<string, string> ParseQueryParameters(Uri uri)
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(uri.Query))
        {
            return values;
        }

        foreach (var pair in uri.Query.TrimStart('?')
                     .Split('&', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var segments = pair.Split('=', 2);
            if (segments.Length != 2)
            {
                continue;
            }

            values[Uri.UnescapeDataString(segments[0])] = Uri.UnescapeDataString(segments[1]);
        }

        return values;
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
        await EvaluateMapScriptAsync(
            $"window.vkFoodMap && window.vkFoodMap.setViewportInsets({viewportInsetsLiteral});",
            "set-viewport-insets");
        await InvalidateMapSizeAsync("viewport-insets");

        if (refitTour && _viewModel.HasVisibleTour && !_viewModel.HasVisibleBottomSheet)
        {
            await EvaluateMapScriptAsync("window.vkFoodMap && window.vkFoodMap.fitToTour();", "fit-tour");
        }
    }

    private string ResolveTourPanelToken()
        => _viewModel.VisibleTour is null
            ? string.Empty
            : $"{_viewModel.TourMode}:{_viewModel.VisibleTour.Id}";

    private async Task RenderMapAsync(bool force = false)
    {
        await _mapRenderLock.WaitAsync();
        try
        {
            if (!force && MapWebView.Source is not null)
            {
                return;
            }

            await RenderMapCoreAsync();
        }
        finally
        {
            _mapRenderLock.Release();
        }
    }

    private async Task RenderMapCoreAsync()
    {
        _isMapReady = false;
        _isMapNavigationComplete = false;
        _hasPendingMapContentRefresh = true;
        _mapRenderGeneration += 1;
        var generation = _mapRenderGeneration;
        var payload = BuildMapPayload("initial-render");
        _logger.LogInformation(
            "[MapInit] Rendering map webview. generation={Generation}; language={LanguageCode}; sourcePois={SourcePoiCount}; validPois={ValidPoiCount}; rejectedPois={RejectedPoiCount}; selectedPoiId={SelectedPoiId}; activePoiId={ActivePoiId}; allowRemoteTiles={AllowRemoteTiles}; containerWidth={Width}; containerHeight={Height}",
            generation,
            _viewModel.CurrentLanguageCode,
            payload.SourcePoiCount,
            payload.ValidPoiCount,
            payload.RejectedPoiCount,
            _viewModel.SelectedPoi?.Id ?? string.Empty,
            _viewModel.CurrentActivePoiId ?? string.Empty,
            Connectivity.Current.NetworkAccess is not NetworkAccess.None,
            MapWebView.Width,
            MapWebView.Height);
        UpdateMapDataStatus(payload.SourcePoiCount, payload.ValidPoiCount);
        var template = await GetMapHtmlTemplateAsync();
        var json = JsonSerializer.Serialize(payload.State, _jsonOptions);
        var encodedJson = Convert.ToBase64String(Encoding.UTF8.GetBytes(json));
        MapWebView.Source = new HtmlWebViewSource
        {
            BaseUrl = MapHtmlBaseUrl,
            Html = template
                .Replace(MapStatePlaceholder, encodedJson, StringComparison.Ordinal)
        };
    }

    private async Task EnsureMapRenderedAsync(bool force = false)
    {
        if (!force && MapWebView.Source is not null)
        {
            return;
        }

        await RenderMapAsync(force);
    }

    private MapPayload BuildMapPayload(string reason)
    {
        var poiPayload = BuildPoiPayload(reason);
        var state = new
        {
            featuredLabel = _viewModel.FeaturedBadgeText,
            selectedPoiId = _viewModel.SelectedPoi?.Id,
            activePoiId = _viewModel.CurrentActivePoiId,
            userLocation = _viewModel.GetMapUserLocationState(),
            allowRemoteTiles = Connectivity.Current.NetworkAccess is not NetworkAccess.None,
            allowLocationMockSelection = true,
            routeSimulation = _viewModel.GetMapRouteSimulationState(),
            currentTour = _viewModel.GetCurrentTourMapState(),
            pois = poiPayload.Items
        };

        return new MapPayload(
            state,
            poiPayload.Items,
            poiPayload.SourceCount,
            poiPayload.ValidCount,
            poiPayload.RejectedCount,
            poiPayload.Signature);
    }

    private PoiPayload BuildPoiPayload(string reason)
    {
        var mapPois = string.IsNullOrWhiteSpace(_viewModel.SearchText)
            ? _viewModel.Pois
            : _viewModel.SearchResults;

        var sourcePois = mapPois.ToList();
        var items = new List<object>(sourcePois.Count);
        var rejectedCount = 0;

        foreach (var poi in sourcePois)
        {
            if (!TryBuildPoiMapItem(poi, out var mapItem, out var rejectReason))
            {
                rejectedCount += 1;
                _logger.LogWarning(
                    "[MarkerRender] Rejected invalid POI coordinate. reason={Reason}; poiId={PoiId}; title={Title}; latitude={Latitude}; longitude={Longitude}",
                    rejectReason,
                    poi.Id,
                    poi.Title,
                    poi.Latitude,
                    poi.Longitude);
                continue;
            }

            items.Add(mapItem);
        }

        var signature = string.Join(
            "|",
            items.Select(item => JsonSerializer.Serialize(item, _jsonOptions)));
        _logger.LogInformation(
            "[MarkerRender] Built POI payload. reason={Reason}; language={LanguageCode}; searchText={SearchText}; sourcePois={SourcePoiCount}; validPois={ValidPoiCount}; rejectedPois={RejectedPoiCount}",
            reason,
            _viewModel.CurrentLanguageCode,
            _viewModel.SearchText ?? string.Empty,
            sourcePois.Count,
            items.Count,
            rejectedCount);

        return new PoiPayload(items, sourcePois.Count, items.Count, rejectedCount, signature);
    }

    private static bool TryBuildPoiMapItem(PoiLocation poi, out object mapItem, out string rejectReason)
    {
        mapItem = new { };
        rejectReason = string.Empty;

        if (string.IsNullOrWhiteSpace(poi.Id))
        {
            rejectReason = "missing-id";
            return false;
        }

        if (!TryNormalizeLatitude(poi.Latitude, out var latitude, out rejectReason) ||
            !TryNormalizeLongitude(poi.Longitude, out var longitude, out rejectReason))
        {
            return false;
        }

        var triggerRadius = double.IsFinite(poi.TriggerRadius) && poi.TriggerRadius >= 20d
            ? poi.TriggerRadius
            : 20d;
        mapItem = new
        {
            id = poi.Id,
            title = poi.Title ?? string.Empty,
            address = poi.Address ?? string.Empty,
            category = poi.Category ?? string.Empty,
            status = poi.IsFeatured ? "featured" : "standard",
            latitude,
            longitude,
            isFeatured = poi.IsFeatured,
            triggerRadius
        };

        return true;
    }

    private static bool TryNormalizeLatitude(double value, out double latitude, out string rejectReason)
    {
        latitude = value;
        if (!double.IsFinite(value))
        {
            rejectReason = "latitude-not-finite";
            return false;
        }

        if (value is < -90d or > 90d)
        {
            rejectReason = "latitude-out-of-range";
            return false;
        }

        rejectReason = string.Empty;
        return true;
    }

    private static bool TryNormalizeLongitude(double value, out double longitude, out string rejectReason)
    {
        longitude = value;
        if (!double.IsFinite(value))
        {
            rejectReason = "longitude-not-finite";
            return false;
        }

        if (value is < -180d or > 180d)
        {
            rejectReason = "longitude-out-of-range";
            return false;
        }

        rejectReason = string.Empty;
        return true;
    }

    private async Task RefreshMapContentAsync()
    {
        if (!_isMapReady)
        {
            _hasPendingMapContentRefresh = true;
            _logger.LogDebug(
                "[MarkerRender] Map content refresh queued because map API is not ready. navigationComplete={NavigationComplete}; generation={Generation}",
                _isMapNavigationComplete,
                _mapRenderGeneration);
            await EnsureMapRenderedAsync();
            return;
        }

        var payload = BuildMapPayload("refresh");
        UpdateMapDataStatus(payload.SourcePoiCount, payload.ValidPoiCount);
        if (string.Equals(_lastMapContentSignature, payload.Signature, StringComparison.Ordinal))
        {
            _logger.LogDebug(
                "[MarkerRender] POI payload signature unchanged. validPois={ValidPoiCount}; selectedPoiId={SelectedPoiId}; activePoiId={ActivePoiId}",
                payload.ValidPoiCount,
                _viewModel.SelectedPoi?.Id ?? string.Empty,
                _viewModel.CurrentActivePoiId ?? string.Empty);
        }

        var payloadLiteral = JsonSerializer.Serialize(new
        {
            featuredLabel = _viewModel.FeaturedBadgeText,
            pois = payload.PoiItems
        }, _jsonOptions);

        var result = await EvaluateMapScriptAsync(
            $"(() => {{ if (!window.vkFoodMap || !window.vkFoodMap.setPoiData) return 'missing-map-api'; window.vkFoodMap.setPoiData({payloadLiteral}); return 'ok'; }})()",
            "set-poi-data",
            critical: true);
        if (!IsJavaScriptOk(result))
        {
            _hasPendingMapContentRefresh = true;
            _isMapReady = false;
            _logger.LogWarning(
                "[MarkerRender] Map API rejected POI update. result={Result}; sourcePois={SourcePoiCount}; validPois={ValidPoiCount}",
                result ?? "null",
                payload.SourcePoiCount,
                payload.ValidPoiCount);
            ShowMapStatus(
                "Bản đồ chưa sẵn sàng",
                "Ứng dụng sẽ tự đồng bộ lại POI ngay khi Leaflet khởi tạo xong.");
            await ProbeMapReadyAsync("set-poi-data-missing-api");
            return;
        }

        _lastMapContentSignature = payload.Signature;
        _hasPendingMapContentRefresh = false;
        await RefreshMapStateAsync();
        await InvalidateMapSizeAsync("poi-refresh");
    }

    private static async Task<string> ReadRawAssetTextAsync(string fileName)
    {
        await using var stream = await FileSystem.OpenAppPackageFileAsync(fileName);
        using var reader = new StreamReader(stream);
        return await reader.ReadToEndAsync();
    }

    private static Task WarmMapTemplateAsync() => GetMapHtmlTemplateAsync();

    private static async Task<string> GetMapHtmlTemplateAsync()
    {
        if (_cachedMapHtmlTemplate is not null)
        {
            return _cachedMapHtmlTemplate;
        }

        await MapTemplateLock.WaitAsync();
        try
        {
            if (_cachedMapHtmlTemplate is not null)
            {
                return _cachedMapHtmlTemplate;
            }

            var templateTask = ReadRawAssetTextAsync(MapTemplateFileName);
            var leafletCssTask = ReadRawAssetTextAsync(LeafletCssFileName);
            var leafletJsTask = ReadRawAssetTextAsync(LeafletJsFileName);
            await Task.WhenAll(templateTask, leafletCssTask, leafletJsTask);

            _cachedMapHtmlTemplate = templateTask.Result
                .Replace(LeafletCssPlaceholder, leafletCssTask.Result, StringComparison.Ordinal)
                .Replace(LeafletJsPlaceholder, leafletJsTask.Result, StringComparison.Ordinal);

            return _cachedMapHtmlTemplate;
        }
        finally
        {
            MapTemplateLock.Release();
        }
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(HomeMapViewModel.MapDataVersion):
                MainThread.BeginInvokeOnMainThread(() => _ = RefreshMapContentAsync());
                break;

            case nameof(HomeMapViewModel.MapContentVersion):
                MainThread.BeginInvokeOnMainThread(() => _ = RefreshMapContentAsync());
                break;

            case nameof(HomeMapViewModel.SelectedPoi):
                MainThread.BeginInvokeOnMainThread(async () =>
                {
                    await RefreshMapStateAsync();
                    if (_viewModel.SelectedPoi is not null &&
                        _viewModel.ConsumeSelectedPoiMapCenterRequest())
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
        await EvaluateMapScriptAsync($"window.vkFoodMap && window.vkFoodMap.selectPoi({selectedPoiIdLiteral});", "select-poi");
        await EvaluateMapScriptAsync($"window.vkFoodMap && window.vkFoodMap.setActivePoi({activePoiIdLiteral});", "set-active-poi");
        await EvaluateMapScriptAsync($"window.vkFoodMap && window.vkFoodMap.setUserLocation({userLocationStateLiteral});", "set-user-location");
        await EvaluateMapScriptAsync($"window.vkFoodMap && window.vkFoodMap.setRouteSimulation({routeSimulationLiteral});", "set-route-simulation");
        await EvaluateMapScriptAsync($"window.vkFoodMap && window.vkFoodMap.setTourState({tourStateLiteral});", "set-tour-state");

        if (_viewModel.SelectedPoi is not null &&
            _viewModel.ConsumeSelectedPoiMapCenterRequest())
        {
            await FlyToCoordinateAsync(_viewModel.SelectedPoi.Latitude, _viewModel.SelectedPoi.Longitude, 17);
        }
    }

    private async Task FlyToCoordinateAsync(double latitude, double longitude, int zoom)
    {
        if (!_isMapReady)
        {
            return;
        }

        var latitudeLiteral = latitude.ToString(CultureInfo.InvariantCulture);
        var longitudeLiteral = longitude.ToString(CultureInfo.InvariantCulture);
        await EvaluateMapScriptAsync($"window.vkFoodMap && window.vkFoodMap.flyToCoordinate({latitudeLiteral}, {longitudeLiteral}, {zoom}, true);", "fly-to-coordinate");
    }

    private async Task FitToSimulationRouteAsync()
    {
        if (!_isMapReady)
        {
            return;
        }

        await EvaluateMapScriptAsync("window.vkFoodMap && window.vkFoodMap.fitToSimulationRoute();", "fit-simulation-route");
    }

    private sealed record MapPayload(
        object State,
        IReadOnlyList<object> PoiItems,
        int SourcePoiCount,
        int ValidPoiCount,
        int RejectedPoiCount,
        string Signature);

    private sealed record PoiPayload(
        IReadOnlyList<object> Items,
        int SourceCount,
        int ValidCount,
        int RejectedCount,
        string Signature);

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
