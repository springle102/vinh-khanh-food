using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Maui.Devices;
using Microsoft.Maui.Networking;
using Microsoft.Maui.Storage;
using Microsoft.Extensions.Logging;
using VinhKhanh.Core.Mobile;
using VinhKhanh.MobileApp.Helpers;
using VinhKhanh.MobileApp.Models;

namespace VinhKhanh.MobileApp.Services;

public interface IFoodStreetDataService
{
    Task<IReadOnlyList<LanguageOption>> GetLanguagesAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<PoiLocation>> GetPoisAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<MapHeatPoint>> GetHeatPointsAsync();
    Task<PoiExperienceDetail?> GetPoiDetailAsync(string poiId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<TourCatalogItem>> GetPublishedToursAsync();
    Task<TourPlan> GetTourPlanAsync(string? tourId = null, IReadOnlyCollection<string>? completedPoiIds = null);
    Task<string> EnsureAllowedLanguageSelectionAsync();
    // ✅ NEW: Explicitly restore to allowed language when needed
    Task<string> RestoreToAllowedLanguageAsync();
    Task TrackPoiViewAsync(string poiId, string? languageCode = null, string source = "poi_detail");
    Task TrackAudioPlayAsync(string poiId, string? languageCode = null, string source = "audio_player", int? durationInSeconds = null);
    Task TrackQrScanAsync(string poiId, string? qrCode = null, string? languageCode = null);
    Task<bool> RefreshDataIfChangedAsync();
    string GetBackdropImageUrl();
}

public sealed partial class FoodStreetApiDataService : IFoodStreetDataService
{
    private const string AppSettingsFileName = "appsettings.json";
    private const string BootstrapEndpoint = "api/v1/bootstrap";
    private const string PublicBootstrapScope = "map";
    private const string SyncStateEndpoint = "api/v1/sync-state";
    private const string AppUsageEventsEndpoint = "api/v1/app-usage-events";
    private const string MobileSyncLogsEndpoint = "api/mobile/sync/logs";
    private const string DefaultBackdropImageUrl = "coverdefault.svg";
    private const int DefaultPremiumPriceUsd = 10;
    private static readonly TimeSpan SyncCheckInterval = TimeSpan.FromSeconds(60);

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

    private static readonly IReadOnlyDictionary<string, string> FallbackPoiImages =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["poi-snail-signature"] = DefaultBackdropImageUrl,
            ["poi-bbq-night"] = DefaultBackdropImageUrl,
            ["poi-sweet-lane"] = DefaultBackdropImageUrl
        };

#if false
    private static readonly IReadOnlyList<LanguageOption> LanguagesLegacy =
    [
        new() { Code = "vi", Flag = "🇻🇳", DisplayName = "Tiếng Việt", IsSelected = true },
        new() { Code = "en", Flag = "🇺🇸", DisplayName = "English" },
        new() { Code = "zh-CN", Flag = "🇨🇳", DisplayName = "中文" },
        new() { Code = "ko", Flag = "🇰🇷", DisplayName = "한국어" },
        new() { Code = "ja", Flag = "🇯🇵", DisplayName = "日本語" },
    ];

#endif

    private static readonly IReadOnlyList<LanguageOption> Languages = AppLanguage.SupportedLanguages
        .Select(item => new LanguageOption
        {
            Code = item.Code,
            Flag = item.Flag,
            DisplayName = item.DisplayName,
            IsSelected = string.Equals(item.Code, AppLanguage.DefaultLanguage, StringComparison.OrdinalIgnoreCase)
        })
        .ToList();

    private static readonly IReadOnlyList<PoiLocation> FallbackPois =
    [
        new()
        {
            Id = "poi-snail-signature",
            Title = "Quán Ốc Vĩnh Khánh Signature",
            ShortDescription = "Quán ốc đặc trưng với thực đơn đa dạng, phù hợp khách lần đầu đến khu phố.",
            Address = "42 Vĩnh Khánh, Phường Khánh Hội, TP.HCM",
            Category = "Ốc đặc sản",
            PriceRange = "80.000 - 280.000 VND",
            ThumbnailUrl = FallbackPoiImages["poi-snail-signature"],
            Latitude = 10.75803,
            Longitude = 106.70162,
            IsFeatured = true,
            HeatIntensity = 1.0,
            DistanceText = "45 phút"
        },
        new()
        {
            Id = "poi-bbq-night",
            Title = "Nhà Hàng Sushi Ko",
            ShortDescription = "Điểm hải sản & đồ sống nổi bật với sushi, sashimi và nhịp sống phố đêm.",
            Address = "126 Vĩnh Khánh, Phường Khánh Hội, TP.HCM",
            Category = "Hải sản & đồ sống",
            PriceRange = "120.000 - 350.000 VND",
            ThumbnailUrl = FallbackPoiImages["poi-bbq-night"],
            Latitude = 10.763724,
            Longitude = 106.701693,
            IsFeatured = true,
            HeatIntensity = 0.86,
            DistanceText = "50 phút"
        },
        new()
        {
            Id = "poi-sweet-lane",
            Title = "Hẻm Cà Phê Vĩnh Khánh",
            ShortDescription = "Điểm cà phê & trà phù hợp để nghỉ chân giữa hành trình ăn uống.",
            Address = "88/4 Vĩnh Khánh, Phường Vĩnh Hội, TP.HCM",
            Category = "Cà phê & trà",
            PriceRange = "25.000 - 75.000 VND",
            ThumbnailUrl = FallbackPoiImages["poi-sweet-lane"],
            Latitude = 10.75712,
            Longitude = 106.70302,
            IsFeatured = false,
            HeatIntensity = 0.62,
            DistanceText = "25 phút"
        }
    ];

    private static readonly IReadOnlyList<MapHeatPoint> FallbackHeatPoints =
    [
        new() { Latitude = 10.75803, Longitude = 106.70162, Intensity = 1.00 },
        new() { Latitude = 10.75850, Longitude = 106.70188, Intensity = 0.92 },
        new() { Latitude = 10.75900, Longitude = 106.70150, Intensity = 0.85 },
        new() { Latitude = 10.76020, Longitude = 106.70176, Intensity = 0.70 },
        new() { Latitude = 10.76310, Longitude = 106.70152, Intensity = 0.78 },
        new() { Latitude = 10.76372, Longitude = 106.70169, Intensity = 0.92 },
        new() { Latitude = 10.75712, Longitude = 106.70302, Intensity = 0.64 },
        new() { Latitude = 10.75672, Longitude = 106.70285, Intensity = 0.54 }
    ];

    private readonly SemaphoreSlim _bootstrapLock = new(1, 1);
    private readonly AsyncLocal<string?> _languageOverride = new();
    private readonly IAppLanguageService _languageService;
    private readonly IMobileApiBaseUrlService _apiBaseUrlService;
    private readonly IOfflineStorageService _offlineStorageService;
    private readonly IBundledOfflinePackageSeedService _bundledSeedService;
    private readonly IMobileDatasetRepository _mobileDatasetRepository;
    private readonly IMobileSyncQueueRepository _syncQueueRepository;
    private readonly ILogger<FoodStreetApiDataService> _logger;
    private AdminBootstrapDto? _bootstrapSource;
    private string? _bootstrapSourceLanguageCode;
    private BootstrapSnapshot? _bootstrapSnapshot;
    private string? _bootstrapSnapshotLanguageCode;
    private DataSyncStateDto? _syncState;
    private DateTimeOffset _lastSyncCheckAt = DateTimeOffset.MinValue;
    private MobileRuntimeAppSettings? _runtimeSettings;
    private HttpClient? _httpClient;
    private string? _resolvedBaseUrl;
    private readonly string _sessionId = Guid.NewGuid().ToString("N");
    private bool _localDatasetLoadAttempted;
    private bool _offlinePackageLoadAttempted;
    private readonly ConcurrentDictionary<string, Task<PoiExperienceDetail?>> _inflightPoiDetailLoads = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, PoiExperienceDetail> _poiDetailCache = new(StringComparer.OrdinalIgnoreCase);

    public FoodStreetApiDataService(
        IAppLanguageService languageService,
        IMobileApiBaseUrlService apiBaseUrlService,
        IOfflineStorageService offlineStorageService,
        IBundledOfflinePackageSeedService bundledSeedService,
        IMobileDatasetRepository mobileDatasetRepository,
        IMobileSyncQueueRepository syncQueueRepository,
        IOfflinePackageService offlinePackageService,
        ILogger<FoodStreetApiDataService> logger)
    {
        _languageService = languageService;
        _apiBaseUrlService = apiBaseUrlService;
        _offlineStorageService = offlineStorageService;
        _bundledSeedService = bundledSeedService;
        _mobileDatasetRepository = mobileDatasetRepository;
        _syncQueueRepository = syncQueueRepository;
        _logger = logger;
        offlinePackageService.StateChanged += OnOfflinePackageStateChanged;
    }

    public async Task<IReadOnlyList<LanguageOption>> GetLanguagesAsync(CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();
        var snapshot = await GetBootstrapSnapshotAsync(cancellationToken: cancellationToken);
        var source = snapshot?.SupportedLanguages.Count > 0
            ? snapshot.SupportedLanguages
            : BuildSupportedLanguages(null);

        var languages = source.Select(language =>
        {
            var normalizedCode = AppLanguage.NormalizeCode(language.Code);
            return new LanguageOption
            {
                Code = normalizedCode,
                Flag = language.Flag?.Trim() ?? string.Empty,
                DisplayName = language.DisplayName?.Trim() ?? normalizedCode,
                IsPremium = false,
                IsLocked = false,
                IsSelected = string.Equals(normalizedCode, _languageService.CurrentLanguage, StringComparison.OrdinalIgnoreCase)
            };
        }).ToList();

        _logger.LogInformation(
            "[MobilePerf] languages loaded. language={LanguageCode}; count={Count}; elapsedMs={ElapsedMs}",
            CurrentLanguageCode,
            languages.Count,
            stopwatch.Elapsed.TotalMilliseconds.ToString("0.0", CultureInfo.InvariantCulture));
        return languages;
    }

    public async Task<IReadOnlyList<PoiLocation>> GetPoisAsync(CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();
        var snapshot = await GetBootstrapSnapshotAsync(cancellationToken: cancellationToken);
        if (snapshot?.Pois.Count > 0)
        {
            _logger.LogInformation(
                "[MobilePerf] POI list loaded from bootstrap snapshot. language={LanguageCode}; poiCount={PoiCount}; elapsedMs={ElapsedMs}",
                CurrentLanguageCode,
                snapshot.Pois.Count,
                stopwatch.Elapsed.TotalMilliseconds.ToString("0.0", CultureInfo.InvariantCulture));
            return snapshot.Pois;
        }

        var fallbackPois = BuildLocalizedFallbackPois();
        _logger.LogWarning(
            "[BootstrapMap] Bootstrap snapshot has no POIs; using localized fallback. language={LanguageCode}; fallbackPoiCount={PoiCount}",
            CurrentLanguageCode,
            fallbackPois.Count);
        return fallbackPois;
    }

    public async Task<IReadOnlyList<MapHeatPoint>> GetHeatPointsAsync()
    {
        var snapshot = await GetBootstrapSnapshotAsync();
        if (snapshot?.HeatPoints.Count > 0)
        {
            return snapshot.HeatPoints;
        }

        return FallbackHeatPoints;
    }

    public async Task<PoiExperienceDetail?> GetPoiDetailAsync(string poiId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(poiId))
        {
            return null;
        }

        var requestedLanguageCode = SelectedLanguageCode;
        var version = _syncState?.Version ?? "none";
        var cacheKey = CreatePoiDetailCacheKey(poiId, requestedLanguageCode, version);
        if (_poiDetailCache.TryGetValue(cacheKey, out var cachedDetail))
        {
            _logger.LogInformation(
                "[PoiDetailCache] hit. poiId={PoiId}; language={LanguageCode}; version={Version}",
                poiId,
                requestedLanguageCode,
                version);
            return cachedDetail;
        }

        var startedNewLoad = false;
        var loadTask = _inflightPoiDetailLoads.GetOrAdd(
            cacheKey,
            _ =>
            {
                startedNewLoad = true;
                return LoadPoiDetailCoreAsync(poiId.Trim(), requestedLanguageCode, version, cancellationToken);
            });

        if (!startedNewLoad)
        {
            _logger.LogDebug(
                "[PoiDetailCache] dedupe inflight. poiId={PoiId}; language={LanguageCode}; version={Version}",
                poiId,
                requestedLanguageCode,
                version);
        }

        try
        {
            var detail = await loadTask.WaitAsync(cancellationToken);
            if (detail is not null)
            {
                _poiDetailCache[cacheKey] = detail;
            }

            return detail;
        }
        finally
        {
            if (loadTask.IsCompleted)
            {
                _inflightPoiDetailLoads.TryRemove(cacheKey, out _);
            }
        }
    }

    public async Task<IReadOnlyList<TourCatalogItem>> GetPublishedToursAsync()
    {
        var snapshot = await GetBootstrapSnapshotAsync();
        if (snapshot is null || (snapshot.Routes.Count == 0 && snapshot.Pois.Count == 0))
        {
            return BuildFallbackPublishedTours();
        }

        if (snapshot.Routes.Count == 0)
        {
            return [];
        }

        var poiLookup = snapshot.Pois.ToDictionary(item => item.Id, StringComparer.OrdinalIgnoreCase);
        return snapshot.Routes
            .OrderByDescending(item => item.UpdatedAt)
            .Select(route => CreateTourCatalogItem(route, poiLookup))
            .ToList();
    }

    public async Task<TourPlan> GetTourPlanAsync(string? tourId = null, IReadOnlyCollection<string>? completedPoiIds = null)
    {
        var snapshot = await GetBootstrapSnapshotAsync();
        var routePlan = TryBuildTourPlanFromSnapshot(snapshot, tourId, completedPoiIds);
        if (routePlan is not null)
        {
            return routePlan;
        }

        if (snapshot is null || snapshot.Pois.Count == 0)
        {
            return BuildFallbackTourPlan(tourId, completedPoiIds) ?? CreateEmptyTourPlan();
        }

        return CreateEmptyTourPlan();
    }

    private TourPlan CreateEmptyTourPlan()
        => new()
        {
            Id = string.Empty,
            Title = GetTourThemeText(),
            Theme = GetTourThemeText(),
            Description = GetTourDescriptionText(),
            CoverImageUrl = DefaultBackdropImageUrl,
            DurationText = string.Empty,
            ProgressValue = 0,
            ProgressText = FormatTourProgressText(0, 0),
            SummaryText = SelectLocalizedText(CreateLocalizedMap(
                "Chưa có lộ trình nào sẵn sàng từ hệ thống quản trị.",
                "No tour is currently available from the admin system.",
                "管理系统当前尚未提供可用路线。",
                "관리 시스템에서 현재 사용할 수 있는 투어 경로가 없습니다.",
                "現在、管理システムから利用可能なツアールートはありません。",
                "Aucun itinéraire n'est actuellement disponible depuis le système d'administration.")),
            Stops = [],
            Checkpoints = []
        };

    public async Task<UserProfileCard> GetUserProfileAsync()
        => await Task.FromResult(new UserProfileCard());
#if false
    public Task<IReadOnlyList<SettingsMenuItem>> GetSettingsMenuLegacyAsync()
        => Task.FromResult<IReadOnlyList<SettingsMenuItem>>(
        [
            new SettingsMenuItem { Icon = "🔔", Title = _languageService.GetText("settings_notifications") },
            new SettingsMenuItem { Icon = "💳", Title = _languageService.GetText("settings_cards") },
            new SettingsMenuItem { Icon = "🔒", Title = _languageService.GetText("settings_privacy") },
            new SettingsMenuItem { Icon = "❓", Title = _languageService.GetText("settings_support") }
        ]);

    public Task<IReadOnlyList<SettingsMenuItem>> GetSettingsMenuAsync()
        => Task.FromResult<IReadOnlyList<SettingsMenuItem>>(
        [
            new SettingsMenuItem { Icon = "\uD83D\uDCCD", Title = _languageService.GetText("home_poi_chip") },
            new SettingsMenuItem { Icon = "\uD83C\uDFA7", Title = _languageService.GetText("poi_detail_listen") },
            new SettingsMenuItem { Icon = "\u2753", Title = _languageService.GetText("settings_support") }
        ]);

#endif

    public string GetBackdropImageUrl()
        => _bootstrapSnapshot?.BackdropImageUrl ?? DefaultBackdropImageUrl;

    public Task TrackPoiViewAsync(string poiId, string? languageCode = null, string source = "poi_detail")
        => TrackUsageEventAsync("poi_view", poiId, languageCode, source, metadata: null, durationInSeconds: null);

    public Task TrackAudioPlayAsync(string poiId, string? languageCode = null, string source = "audio_player", int? durationInSeconds = null)
        => TrackUsageEventAsync("audio_play", poiId, languageCode, source, metadata: null, durationInSeconds);

    public Task TrackQrScanAsync(string poiId, string? qrCode = null, string? languageCode = null)
    {
        var metadata = string.IsNullOrWhiteSpace(qrCode)
            ? null
            : JsonSerializer.Serialize(new { qrCode = qrCode.Trim() });
        return TrackUsageEventAsync("qr_scan", poiId, languageCode, "qr_scanner", metadata, durationInSeconds: null);
    }

    public async Task<bool> RefreshDataIfChangedAsync()
    {
        var previousVersion = _syncState?.Version;
        var previousSnapshot = _bootstrapSnapshot;
        var snapshot = await GetBootstrapSnapshotAsync(forceSyncCheck: true);
        if (snapshot is null)
        {
            return false;
        }

        return !ReferenceEquals(previousSnapshot, snapshot) ||
               !string.Equals(previousVersion, _syncState?.Version, StringComparison.OrdinalIgnoreCase);
    }

    private async Task TrackUsageEventAsync(
        string eventType,
        string? poiId,
        string? languageCode,
        string source,
        string? metadata,
        int? durationInSeconds)
    {
        if (string.IsNullOrWhiteSpace(eventType))
        {
            return;
        }

        var normalizedEventType = MobileUsageEventTypes.Normalize(eventType);
        if (string.IsNullOrWhiteSpace(normalizedEventType))
        {
            return;
        }

        var usageEvent = new QueuedMobileUsageEvent(
            CreateUsageEventIdempotencyKey(normalizedEventType, poiId, metadata),
            normalizedEventType,
            string.IsNullOrWhiteSpace(poiId) ? null : poiId.Trim(),
            AppLanguage.NormalizeCode(languageCode ?? _languageService.CurrentLanguage),
            ResolvePlatformCode(),
            _sessionId,
            string.IsNullOrWhiteSpace(source) ? "mobile_app" : source.Trim(),
            metadata,
            durationInSeconds,
            DateTimeOffset.UtcNow);

        await _syncQueueRepository.EnqueueUsageEventAsync(usageEvent);

        if (!HasAnyNetworkAccess())
        {
            return;
        }

        var client = await GetClientAsync();
        if (client is not null)
        {
            await FlushUsageEventQueueAsync(client);
        }
    }

    private async Task FlushUsageEventQueueAsync(HttpClient client)
    {
        IReadOnlyList<QueuedMobileUsageEvent> pending;
        try
        {
            pending = await _syncQueueRepository.GetPendingUsageEventsAsync();
        }
        catch (Exception exception)
        {
            _logger.LogDebug(exception, "[SyncQueue] Unable to read pending usage events.");
            return;
        }

        if (pending.Count == 0)
        {
            return;
        }

        var request = new MobileUsageEventSyncRequest(
            pending.Select(item => new MobileUsageEventSyncItem(
                item.IdempotencyKey,
                item.EventType,
                item.PoiId,
                item.LanguageCode,
                item.Platform,
                item.SessionId,
                item.Source,
                item.Metadata,
                item.DurationInSeconds,
                item.OccurredAt)).ToList());

        try
        {
            using var response = await client.PostAsJsonAsync(MobileSyncLogsEndpoint, request, JsonOptions);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogDebug(
                    "[SyncQueue] Mobile sync endpoint returned status {StatusCode}. queueCount={QueueCount}",
                    (int)response.StatusCode,
                    pending.Count);
                return;
            }

            var envelope = await response.Content.ReadFromJsonAsync<ApiEnvelope<MobileUsageEventSyncResponse>>(JsonOptions);
            if (envelope?.Success != true || envelope.Data is null)
            {
                return;
            }

            var acceptedKeys = envelope.Data.Results
                .Where(item => item.Accepted)
                .Select(item => item.IdempotencyKey)
                .ToList();
            var failed = envelope.Data.Results
                .Where(item => !item.Accepted)
                .ToDictionary(
                    item => item.IdempotencyKey,
                    item => item.ErrorMessage ?? "rejected",
                    StringComparer.OrdinalIgnoreCase);

            await _syncQueueRepository.MarkUsageEventsSyncedAsync(acceptedKeys);
            await _syncQueueRepository.MarkUsageEventsFailedAsync(failed);

            _logger.LogInformation(
                "[SyncQueue] Usage events flushed. accepted={AcceptedCount}; rejected={RejectedCount}; pendingBefore={PendingCount}",
                envelope.Data.AcceptedCount,
                envelope.Data.RejectedCount,
                pending.Count);
        }
        catch (Exception exception)
        {
            _logger.LogDebug(exception, "[SyncQueue] Unable to flush usage event queue. queueCount={QueueCount}", pending.Count);
        }
    }

    private static string CreateUsageEventIdempotencyKey(string eventType, string? poiId, string? metadata)
    {
        var normalizedPoiId = string.IsNullOrWhiteSpace(poiId) ? "none" : poiId.Trim();
        var normalizedMetadata = string.IsNullOrWhiteSpace(metadata) ? "none" : metadata.Trim();
        var key = $"mobile-{eventType}-{normalizedPoiId}-{normalizedMetadata.Length}-{DateTimeOffset.UtcNow:yyyyMMddHHmmssfff}-{Guid.NewGuid():N}";
        return key.Length <= 80 ? key : key[..80];
    }
}

public sealed partial class FoodStreetApiDataService
{
    private static readonly (double LatitudeOffset, double LongitudeOffset)[] HeatOffsets =
    [
        (0.00018, 0.00006),
        (-0.00014, 0.00005),
        (0.00006, -0.00011),
        (-0.00008, -0.00007)
    ];

    private async Task<BootstrapSnapshot?> GetBootstrapSnapshotAsync(
        bool forceSyncCheck = false,
        CancellationToken cancellationToken = default)
    {
        await _bootstrapLock.WaitAsync(cancellationToken);
        try
        {
            var currentLanguageCode = SelectedLanguageCode;
            cancellationToken.ThrowIfCancellationRequested();
            await TryLoadLocalDatasetAsync(currentLanguageCode);
            await TryLoadOfflinePackageAsync(currentLanguageCode);

            var needsLanguageBootstrapRefresh =
                _bootstrapSource is not null &&
                !string.Equals(_bootstrapSourceLanguageCode, currentLanguageCode, StringComparison.OrdinalIgnoreCase);
            var needsLocalizedContentRefresh =
                _bootstrapSource is not null &&
                RequiresRemoteLocalizedBootstrap(_bootstrapSource, currentLanguageCode);
            if ((_bootstrapSnapshot is null ||
                 !string.Equals(_bootstrapSnapshotLanguageCode, currentLanguageCode, StringComparison.OrdinalIgnoreCase)) &&
                TryRebuildBootstrapSnapshotFromCache(currentLanguageCode, "language-change"))
            {
                if (!needsLanguageBootstrapRefresh &&
                    !needsLocalizedContentRefresh &&
                    !ShouldCheckSyncState(forceSyncCheck))
                {
                    return _bootstrapSnapshot;
                }
            }

            if (!HasAnyNetworkAccess())
            {
                return _bootstrapSnapshot;
            }

            var client = await GetClientAsync();
            if (client is null)
            {
                return _bootstrapSnapshot;
            }

            for (var attempt = 0; attempt < 3; attempt++)
            {
                currentLanguageCode = SelectedLanguageCode;
                needsLanguageBootstrapRefresh =
                    _bootstrapSource is not null &&
                    !string.Equals(_bootstrapSourceLanguageCode, currentLanguageCode, StringComparison.OrdinalIgnoreCase);
                needsLocalizedContentRefresh =
                    _bootstrapSource is not null &&
                    RequiresRemoteLocalizedBootstrap(_bootstrapSource, currentLanguageCode);
                if (_bootstrapSnapshot is null ||
                    !string.Equals(_bootstrapSnapshotLanguageCode, currentLanguageCode, StringComparison.OrdinalIgnoreCase) ||
                    needsLanguageBootstrapRefresh ||
                    needsLocalizedContentRefresh)
                {
                    await RefreshBootstrapSnapshotAsync(
                        client,
                        currentLanguageCode,
                        remoteSyncState: null,
                        cancellationToken,
                        reason: needsLocalizedContentRefresh
                            ? attempt == 0 ? "localized-content-missing" : "localized-content-retry"
                            : needsLanguageBootstrapRefresh
                            ? attempt == 0 ? "language-change" : "language-retry"
                            : attempt == 0 ? "initial" : "language-retry");

                    if (string.Equals(SelectedLanguageCode, currentLanguageCode, StringComparison.OrdinalIgnoreCase))
                    {
                        return _bootstrapSnapshot;
                    }

                    continue;
                }

                if (!ShouldCheckSyncState(forceSyncCheck))
                {
                    return _bootstrapSnapshot;
                }

                var remoteSyncState = await FetchSyncStateAsync(client, cancellationToken);
                _lastSyncCheckAt = DateTimeOffset.UtcNow;

                if (!string.Equals(SelectedLanguageCode, currentLanguageCode, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (remoteSyncState is null)
                {
                    _logger.LogDebug(
                        "Sync-state check failed. Reusing cached bootstrap version {Version}.",
                        _syncState?.Version ?? "none");
                    return _bootstrapSnapshot;
                }

                if (string.Equals(_syncState?.Version, remoteSyncState.Version, StringComparison.OrdinalIgnoreCase))
                {
                    _logger.LogDebug("Bootstrap snapshot is already current at version {Version}.", remoteSyncState.Version);
                    return _bootstrapSnapshot;
                }

                currentLanguageCode = SelectedLanguageCode;
                await RefreshBootstrapSnapshotAsync(client, currentLanguageCode, remoteSyncState, cancellationToken, "version-changed");

                if (string.Equals(SelectedLanguageCode, currentLanguageCode, StringComparison.OrdinalIgnoreCase))
                {
                    return _bootstrapSnapshot;
                }
            }

            return _bootstrapSnapshot;
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "Unable to refresh bootstrap snapshot. Reusing cached data if available.");
            return _bootstrapSnapshot;
        }
        finally
        {
            _bootstrapLock.Release();
        }
    }

    private bool ShouldCheckSyncState(bool forceSyncCheck)
        => forceSyncCheck || _bootstrapSnapshot is null || DateTimeOffset.UtcNow - _lastSyncCheckAt >= SyncCheckInterval;

    private async Task<DataSyncStateDto?> FetchSyncStateAsync(HttpClient client, CancellationToken cancellationToken)
    {
        using var response = await client.GetAsync(SyncStateEndpoint, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            return null;
        }

        var envelope = await response.Content.ReadFromJsonAsync<ApiEnvelope<DataSyncStateDto>>(JsonOptions, cancellationToken);
        return envelope?.Success == true ? envelope.Data : null;
    }

    private static string BuildBootstrapEndpoint(string? languageCode)
    {
        var query = new List<string>();
        var normalizedLanguageCode = AppLanguage.NormalizeCode(languageCode);
        if (!string.IsNullOrWhiteSpace(normalizedLanguageCode))
        {
            query.Add($"languageCode={Uri.EscapeDataString(normalizedLanguageCode)}");
        }

        query.Add($"scope={Uri.EscapeDataString(PublicBootstrapScope)}");

        return query.Count == 0
            ? BootstrapEndpoint
            : $"{BootstrapEndpoint}?{string.Join("&", query)}";
    }

    private bool TryRebuildBootstrapSnapshotFromCache(string requestedLanguageCode, string reason)
    {
        if (_bootstrapSource is null)
        {
            return false;
        }

        using var _ = BeginLanguageScope(requestedLanguageCode);
        var snapshot = CreateSnapshot(_bootstrapSource);

        if (!IsSelectedLanguage(requestedLanguageCode))
        {
            _logger.LogInformation(
                "Discarding stale cached bootstrap snapshot ({Reason}). Requested={RequestedLanguage}; Current={CurrentLanguage}.",
                reason,
                requestedLanguageCode,
                SelectedLanguageCode);
            return false;
        }

        _bootstrapSnapshot = snapshot;
        _bootstrapSnapshotLanguageCode = AppLanguage.NormalizeCode(requestedLanguageCode);

        _logger.LogInformation(
            "[BootstrapMap] Bootstrap snapshot rebuilt from cache ({Reason}). snapshotLanguage={LanguageCode}; sourceLanguage={SourceLanguageCode}; version={Version}; pois={PoiCount}; routes={RouteCount}",
            reason,
            _bootstrapSnapshotLanguageCode,
            _bootstrapSourceLanguageCode ?? "unknown",
            _syncState?.Version ?? "none",
            snapshot.Pois.Count,
            snapshot.Routes.Count);

        return true;
    }

    private async Task<BootstrapSnapshot?> RefreshBootstrapSnapshotAsync(
        HttpClient client,
        string requestedLanguageCode,
        DataSyncStateDto? remoteSyncState,
        CancellationToken cancellationToken,
        string reason)
    {
        var stopwatch = Stopwatch.StartNew();
        using var response = await client.GetAsync(BuildBootstrapEndpoint(requestedLanguageCode), cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            return _bootstrapSnapshot;
        }

        var bootstrapEnvelopeJson = await response.Content.ReadAsStringAsync(cancellationToken);
        var envelope = JsonSerializer.Deserialize<ApiEnvelope<AdminBootstrapDto>>(bootstrapEnvelopeJson, JsonOptions);
        if (envelope?.Success != true || envelope.Data is null)
        {
            return _bootstrapSnapshot;
        }

        if (!IsSelectedLanguage(requestedLanguageCode))
        {
            _logger.LogInformation(
                "Discarding stale bootstrap response ({Reason}). Requested={RequestedLanguage}; Current={CurrentLanguage}.",
                reason,
                requestedLanguageCode,
                SelectedLanguageCode);
            return _bootstrapSnapshot;
        }

        using var _ = BeginLanguageScope(requestedLanguageCode);
        var nextBootstrapSource = envelope.Data;
        var snapshot = CreateSnapshot(nextBootstrapSource);

        if (_bootstrapSnapshot is not null &&
            _bootstrapSnapshot.Pois.Count > 0 &&
            snapshot.Pois.Count == 0 &&
            snapshot.Routes.Count == 0)
        {
            _logger.LogWarning(
                "Ignoring empty bootstrap payload ({Reason}) because cached data is already available. Requested={RequestedLanguage}",
                reason,
                requestedLanguageCode);
            return _bootstrapSnapshot;
        }

        if (!IsSelectedLanguage(requestedLanguageCode))
        {
            _logger.LogInformation(
                "Discarding stale bootstrap snapshot ({Reason}). Requested={RequestedLanguage}; Current={CurrentLanguage}.",
                reason,
                requestedLanguageCode,
                SelectedLanguageCode);
            return _bootstrapSnapshot;
        }

        _bootstrapSource = nextBootstrapSource;
        _bootstrapSnapshot = snapshot;
        _bootstrapSnapshotLanguageCode = AppLanguage.NormalizeCode(requestedLanguageCode);
        _bootstrapSourceLanguageCode = AppLanguage.NormalizeCode(requestedLanguageCode);
        _syncState = nextBootstrapSource.SyncState ?? remoteSyncState;
        _lastSyncCheckAt = DateTimeOffset.UtcNow;

        try
        {
            await _mobileDatasetRepository.SaveBootstrapEnvelopeAsync(
                bootstrapEnvelopeJson,
                MobileDatasetConstants.DownloadedInstallationSource);
        }
        catch (Exception exception)
        {
            _logger.LogDebug(exception, "[OfflineDb] Unable to cache remote bootstrap in local SQLite.");
        }

        _logger.LogInformation(
            "[BootstrapFetch] Bootstrap snapshot refreshed ({Reason}). language={LanguageCode}; version={Version}; sourcePois={SourcePoiCount}; translations={TranslationCount}; audioGuides={AudioGuideCount}; mappedPois={PoiCount}; routes={RouteCount}; elapsedMs={ElapsedMs}",
            reason,
            _bootstrapSnapshotLanguageCode,
            _syncState?.Version ?? "none",
            nextBootstrapSource.Pois.Count,
            nextBootstrapSource.Translations.Count,
            nextBootstrapSource.AudioGuides.Count,
            snapshot.Pois.Count,
            snapshot.Routes.Count,
            stopwatch.Elapsed.TotalMilliseconds.ToString("0.0", CultureInfo.InvariantCulture));

        return _bootstrapSnapshot;
    }

    private async Task<PoiExperienceDetail?> LoadPoiDetailCoreAsync(
        string poiId,
        string requestedLanguageCode,
        string version,
        CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        var source = "none";
        try
        {
            PoiExperienceDetail? detail = null;
            if (HasAnyNetworkAccess())
            {
                var client = await GetClientAsync();
                if (client is not null)
                {
                    detail = await FetchRemotePoiDetailAsync(client, poiId, requestedLanguageCode, cancellationToken);
                    source = detail is null ? source : "remote-detail";
                }
            }

            detail ??= TryBuildPoiDetailFromBootstrapSource(poiId, requestedLanguageCode);
            if (detail is not null && source == "none")
            {
                source = "bootstrap-source";
            }

            detail ??= BuildFallbackPoiDetail(poiId);
            if (detail is not null && source == "none")
            {
                source = "fallback";
            }

            if (!IsSelectedLanguage(requestedLanguageCode))
            {
                _logger.LogInformation(
                    "[PoiDetailRace] Discarding stale detail load. poiId={PoiId}; requestedLanguage={RequestedLanguage}; currentLanguage={CurrentLanguage}; version={Version}; elapsedMs={ElapsedMs}",
                    poiId,
                    requestedLanguageCode,
                    SelectedLanguageCode,
                    version,
                    stopwatch.Elapsed.TotalMilliseconds.ToString("0.0", CultureInfo.InvariantCulture));
                return null;
            }

            _logger.LogInformation(
                "[PoiDetailPerf] detail loaded. poiId={PoiId}; language={LanguageCode}; version={Version}; source={Source}; hasDetail={HasDetail}; foodItems={FoodItemCount}; promotions={PromotionCount}; audioAssets={AudioAssetCount}; elapsedMs={ElapsedMs}",
                poiId,
                requestedLanguageCode,
                version,
                source,
                detail is not null,
                detail?.FoodItems.Count ?? 0,
                detail?.Promotions.Count ?? 0,
                detail?.AudioAssets.Values.Count ?? 0,
                stopwatch.Elapsed.TotalMilliseconds.ToString("0.0", CultureInfo.InvariantCulture));
            return detail;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            _logger.LogInformation(
                "[PoiDetailRace] detail request canceled. poiId={PoiId}; language={LanguageCode}; version={Version}; elapsedMs={ElapsedMs}",
                poiId,
                requestedLanguageCode,
                version,
                stopwatch.Elapsed.TotalMilliseconds.ToString("0.0", CultureInfo.InvariantCulture));
            throw;
        }
        catch (Exception exception)
        {
            _logger.LogWarning(
                exception,
                "[PoiDetailPerf] detail load failed. poiId={PoiId}; language={LanguageCode}; version={Version}; source={Source}; elapsedMs={ElapsedMs}",
                poiId,
                requestedLanguageCode,
                version,
                source,
                stopwatch.Elapsed.TotalMilliseconds.ToString("0.0", CultureInfo.InvariantCulture));
            return TryBuildPoiDetailFromBootstrapSource(poiId, requestedLanguageCode) ?? BuildFallbackPoiDetail(poiId);
        }
    }

    private async Task<PoiExperienceDetail?> FetchRemotePoiDetailAsync(
        HttpClient client,
        string poiId,
        string requestedLanguageCode,
        CancellationToken cancellationToken)
    {
        var endpoint = BuildPoiDetailEndpoint(poiId, requestedLanguageCode);
        using var response = await client.GetAsync(endpoint, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning(
                "[PoiDetailFetch] remote detail endpoint returned {StatusCode}. poiId={PoiId}; language={LanguageCode}",
                (int)response.StatusCode,
                poiId,
                requestedLanguageCode);
            return null;
        }

        var envelope = await response.Content.ReadFromJsonAsync<ApiEnvelope<PoiDetailDto>>(JsonOptions, cancellationToken);
        if (envelope?.Success != true || envelope.Data is null)
        {
            return null;
        }

        return BuildPoiDetailFromDto(envelope.Data, requestedLanguageCode);
    }

    private PoiExperienceDetail? TryBuildPoiDetailFromBootstrapSource(string poiId, string requestedLanguageCode)
    {
        var source = _bootstrapSource;
        if (source is null)
        {
            return null;
        }

        var poi = source.Pois.FirstOrDefault(item => string.Equals(item.Id, poiId, StringComparison.OrdinalIgnoreCase));
        if (poi is null)
        {
            return null;
        }

        var foodItems = source.FoodItems
            .Where(item => string.Equals(item.PoiId, poiId, StringComparison.OrdinalIgnoreCase))
            .ToList();
        var foodItemIds = foodItems
            .Select(item => item.Id)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var promotions = source.Promotions
            .Where(item => string.Equals(item.PoiId, poiId, StringComparison.OrdinalIgnoreCase))
            .ToList();
        var promotionIds = promotions
            .Select(item => item.Id)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var detailDto = new PoiDetailDto
        {
            Poi = poi,
            Translations = source.Translations
                .Where(item =>
                    string.Equals(item.EntityType, "poi", StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(item.EntityId, poiId, StringComparison.OrdinalIgnoreCase))
                .ToList(),
            AudioGuides = source.AudioGuides
                .Where(item =>
                    string.Equals(item.EntityType, "poi", StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(item.EntityId, poiId, StringComparison.OrdinalIgnoreCase))
                .ToList(),
            FoodItems = foodItems,
            FoodItemTranslations = source.Translations
                .Where(item =>
                    string.Equals(item.EntityType, "food_item", StringComparison.OrdinalIgnoreCase) &&
                    foodItemIds.Contains(item.EntityId))
                .ToList(),
            Promotions = promotions,
            PromotionTranslations = source.Translations
                .Where(item =>
                    string.Equals(item.EntityType, "promotion", StringComparison.OrdinalIgnoreCase) &&
                    promotionIds.Contains(item.EntityId))
                .ToList(),
            MediaAssets = source.MediaAssets
                .Where(item =>
                    (string.Equals(item.EntityType, "poi", StringComparison.OrdinalIgnoreCase) &&
                     string.Equals(item.EntityId, poiId, StringComparison.OrdinalIgnoreCase)) ||
                    (string.Equals(item.EntityType, "food_item", StringComparison.OrdinalIgnoreCase) &&
                     foodItemIds.Contains(item.EntityId)) ||
                    (string.Equals(item.EntityType, "promotion", StringComparison.OrdinalIgnoreCase) &&
                     promotionIds.Contains(item.EntityId)))
                .ToList()
        };

        return BuildPoiDetailFromDto(detailDto, requestedLanguageCode);
    }

    private PoiExperienceDetail? BuildPoiDetailFromDto(PoiDetailDto detailDto, string requestedLanguageCode)
    {
        if (detailDto.Poi is null || string.IsNullOrWhiteSpace(detailDto.Poi.Id))
        {
            return null;
        }

        using var _ = BeginLanguageScope(requestedLanguageCode);
        var categoriesById = _bootstrapSource?.Categories
            .Where(item => !string.IsNullOrWhiteSpace(item.Id))
            .ToDictionary(item => item.Id, item => item.Name, StringComparer.OrdinalIgnoreCase)
            ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var foodItemTranslationsById = detailDto.FoodItemTranslations
            .Where(item => string.Equals(item.EntityType, "food_item", StringComparison.OrdinalIgnoreCase))
            .GroupBy(item => item.EntityId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => (IReadOnlyList<TranslationDto>)group.ToList(), StringComparer.OrdinalIgnoreCase);
        var promotionTranslationsById = detailDto.PromotionTranslations
            .Where(item => string.Equals(item.EntityType, "promotion", StringComparison.OrdinalIgnoreCase))
            .GroupBy(item => item.EntityId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => (IReadOnlyList<TranslationDto>)group.ToList(), StringComparer.OrdinalIgnoreCase);
        var poiImages = BuildMediaImageLookupByEntityId(detailDto.MediaAssets, "poi");
        var foodItemImagesById = BuildMediaImageLookupByEntityId(detailDto.MediaAssets, "food_item");
        var foodImages = BuildFoodImagesByPoiId(detailDto.FoodItems, foodItemImagesById);

        return BuildPoiDetail(
            detailDto.Poi,
            GetCategoryName(categoriesById, detailDto.Poi.CategoryId),
            detailDto.Translations,
            detailDto.AudioGuides,
            detailDto.FoodItems,
            foodItemTranslationsById,
            detailDto.Promotions,
            promotionTranslationsById,
            poiImages,
            foodImages,
            foodItemImagesById);
    }

    private static string BuildPoiDetailEndpoint(string poiId, string languageCode)
        => $"api/v1/pois/{Uri.EscapeDataString(poiId)}/detail?languageCode={Uri.EscapeDataString(AppLanguage.NormalizeCode(languageCode))}";

    private static string CreatePoiDetailCacheKey(string poiId, string languageCode, string version)
        => string.Join(
            ":",
            "poi-detail",
            poiId.Trim().ToLowerInvariant(),
            AppLanguage.NormalizeCode(languageCode).ToLowerInvariant(),
            string.IsNullOrWhiteSpace(version) ? "none" : version.Trim().ToLowerInvariant());

    private string SelectedLanguageCode => AppLanguage.NormalizeCode(_languageService.CurrentLanguage);

    private bool IsSelectedLanguage(string languageCode)
        => string.Equals(
            SelectedLanguageCode,
            AppLanguage.NormalizeCode(languageCode),
            StringComparison.OrdinalIgnoreCase);

    private IDisposable BeginLanguageScope(string languageCode)
        => new LanguageScope(this, languageCode);

    private sealed class LanguageScope : IDisposable
    {
        private readonly FoodStreetApiDataService _owner;
        private readonly string? _previousLanguageCode;
        private bool _disposed;

        public LanguageScope(FoodStreetApiDataService owner, string languageCode)
        {
            _owner = owner;
            _previousLanguageCode = owner._languageOverride.Value;
            owner._languageOverride.Value = AppLanguage.NormalizeCode(languageCode);
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _owner._languageOverride.Value = _previousLanguageCode;
            _disposed = true;
        }
    }

    private async Task<HttpClient?> GetClientAsync()
    {
        var configuredBaseUrl = await _apiBaseUrlService.GetBaseUrlAsync();
        var nextBaseUrl = string.IsNullOrWhiteSpace(configuredBaseUrl)
            ? string.Empty
            : EnsureTrailingSlash(configuredBaseUrl);
        if (string.IsNullOrWhiteSpace(nextBaseUrl))
        {
            _logger.LogWarning("Mobile app has no API base URL configured. Shared backend data will not be loaded.");
            return null;
        }

        if (_httpClient is not null &&
            string.Equals(_resolvedBaseUrl, nextBaseUrl, StringComparison.OrdinalIgnoreCase))
        {
            return _httpClient;
        }

        _httpClient?.Dispose();
        var handler = new HttpClientHandler
        {
            ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator
        };

        _httpClient = new HttpClient(handler)
        {
            BaseAddress = new Uri(nextBaseUrl, UriKind.Absolute),
            Timeout = TimeSpan.FromSeconds(4)
        };
        _httpClient.DefaultRequestHeaders.CacheControl = new CacheControlHeaderValue
        {
            NoCache = true,
            NoStore = true
        };
        _httpClient.DefaultRequestHeaders.Pragma.ParseAdd("no-cache");
        _resolvedBaseUrl = nextBaseUrl;
        return _httpClient;
    }

    private static bool HasAnyNetworkAccess()
        => Connectivity.Current.NetworkAccess is not NetworkAccess.None;

    private async Task<MobileRuntimeAppSettings> LoadRuntimeSettingsAsync()
    {
        if (_runtimeSettings is not null)
        {
            return _runtimeSettings;
        }

        try
        {
            await using var stream = await FileSystem.OpenAppPackageFileAsync(AppSettingsFileName);
            using var reader = new StreamReader(stream);
            var content = await reader.ReadToEndAsync();
            _runtimeSettings = JsonSerializer.Deserialize<MobileRuntimeAppSettings>(content, JsonOptions) ?? new MobileRuntimeAppSettings();
        }
        catch
        {
            _runtimeSettings = new MobileRuntimeAppSettings();
        }

        return _runtimeSettings;
    }

    private BootstrapSnapshot CreateLegacySnapshot(AdminBootstrapDto bootstrap)
    {
        var categoriesById = bootstrap.Categories
            .Where(item => !string.IsNullOrWhiteSpace(item.Id))
            .ToDictionary(item => item.Id, item => item.Name, StringComparer.OrdinalIgnoreCase);
        var premiumOffer = BuildPremiumOffer(bootstrap.Settings);
        var supportedLanguages = BuildSupportedLanguages(bootstrap.Settings);
        var accessibleTranslations = bootstrap.Translations
            .Where(item => !string.IsNullOrWhiteSpace(item.LanguageCode))
            .ToList();

        var translationsByPoiId = accessibleTranslations
            .Where(item => string.Equals(item.EntityType, "poi", StringComparison.OrdinalIgnoreCase))
            .GroupBy(item => item.EntityId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.ToList(), StringComparer.OrdinalIgnoreCase);

        var translationsByFoodItemId = accessibleTranslations
            .Where(item => string.Equals(item.EntityType, "food_item", StringComparison.OrdinalIgnoreCase))
            .GroupBy(item => item.EntityId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => (IReadOnlyList<TranslationDto>)group.ToList(), StringComparer.OrdinalIgnoreCase);

        var translationsByPromotionId = accessibleTranslations
            .Where(item => string.Equals(item.EntityType, "promotion", StringComparison.OrdinalIgnoreCase))
            .GroupBy(item => item.EntityId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => (IReadOnlyList<TranslationDto>)group.ToList(), StringComparer.OrdinalIgnoreCase);

        var poiImages = BuildMediaImageLookupByEntityId(bootstrap.MediaAssets, "poi")
            .Where(pair => pair.Value.Count > 0)
            .ToDictionary(pair => pair.Key, pair => pair.Value[0], StringComparer.OrdinalIgnoreCase);
        var foodItemImagesById = BuildMediaImageLookupByEntityId(bootstrap.MediaAssets, "food_item");
        var foodImages = BuildFoodImagesByPoiId(bootstrap.FoodItems, foodItemImagesById)
            .Where(pair => pair.Value.Count > 0)
            .ToDictionary(pair => pair.Key, pair => pair.Value[0], StringComparer.OrdinalIgnoreCase);

        var orderedPois = bootstrap.Pois
            .Where(item => IsPublishedContent(item.Status) && IsValidLatitude(item.Lat) && IsValidLongitude(item.Lng))
            .OrderByDescending(item => item.Featured)
            .ThenByDescending(item => item.PopularityScore)
            .ThenBy(item => item.Slug, StringComparer.OrdinalIgnoreCase)
            .ToList();
        var publishedPoiIds = orderedPois
            .Select(item => item.Id)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var poiLocations = orderedPois.Select(poi =>
        {
            translationsByPoiId.TryGetValue(poi.Id, out var poiTranslations);
            var translation = SelectTranslation(poiTranslations, CurrentLanguageCode, "poi", poi.Id);
            var title = FirstNonEmpty(translation?.Title, CreateSafePoiTitleFallback(poi.Slug), poi.Id);
            var category = LocalizeCategory(GetCategoryName(categoriesById, poi.CategoryId));
            var shortDescription = FirstNonEmpty(translation?.ShortText, translation?.FullText);

            if (string.IsNullOrWhiteSpace(shortDescription))
            {
                shortDescription = $"{title} • {category}";
            }

            return new PoiLocation
            {
                Id = poi.Id,
                Title = title,
                ShortDescription = shortDescription,
                Address = LocalizeAddress(poi.Address),
                Category = category,
                PriceRange = poi.PriceRange,
                ThumbnailUrl = ResolvePoiImageUrl(poi.Id, poiImages, foodImages),
                Latitude = poi.Lat,
                Longitude = poi.Lng,
                IsFeatured = poi.Featured,
                TriggerRadius = double.IsFinite(poi.TriggerRadius) && poi.TriggerRadius >= 20d
                    ? poi.TriggerRadius
                    : 20d,
                Priority = Math.Max(0, poi.Priority),
                HeatIntensity = ResolveHeatIntensity(poi, bootstrap.UsageEvents),
                DistanceText = FormatVisitDuration(Math.Max(10, poi.AverageVisitDuration))
            };
        }).ToList();

        return new BootstrapSnapshot(
            poiLocations,
            BuildHeatPoints(orderedPois, bootstrap.UsageEvents),
            ResolveBackdropImageUrl(poiLocations, poiImages),
            new UserProfileCard(),
            new Dictionary<string, PoiExperienceDetail>(StringComparer.OrdinalIgnoreCase),
            premiumOffer,
            supportedLanguages,
            BuildRouteSnapshots(bootstrap.Routes, accessibleTranslations, publishedPoiIds));
    }

    private BootstrapSnapshot CreateSnapshot(AdminBootstrapDto bootstrap)
    {
        var categoriesById = bootstrap.Categories
            .Where(item => !string.IsNullOrWhiteSpace(item.Id))
            .ToDictionary(item => item.Id, item => item.Name, StringComparer.OrdinalIgnoreCase);
        var premiumOffer = BuildPremiumOffer(bootstrap.Settings);
        var supportedLanguages = BuildSupportedLanguages(bootstrap.Settings);
        _logger.LogDebug(
            "[BootstrapMap] Mapping bootstrap snapshot. requestedLanguage={RequestedLanguage}; sourceLanguage={SourceLanguage}; sourcePois={PoiCount}; sourceTranslations={TranslationCount}; sourceAudioGuides={AudioGuideCount}",
            CurrentLanguageCode,
            _bootstrapSourceLanguageCode ?? "unknown",
            bootstrap.Pois.Count,
            bootstrap.Translations.Count,
            bootstrap.AudioGuides.Count);

        var accessibleTranslations = bootstrap.Translations
            .Where(item => !string.IsNullOrWhiteSpace(item.LanguageCode))
            .ToList();
        var translationsByPoiId = accessibleTranslations
            .Where(item => string.Equals(item.EntityType, "poi", StringComparison.OrdinalIgnoreCase))
            .GroupBy(item => item.EntityId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.ToList(), StringComparer.OrdinalIgnoreCase);

        var poiImages = BuildMediaImageLookupByEntityId(bootstrap.MediaAssets, "poi");
        var foodItemImagesById = BuildMediaImageLookupByEntityId(bootstrap.MediaAssets, "food_item");
        var foodImages = BuildFoodImagesByPoiId(bootstrap.FoodItems, foodItemImagesById);
        var primaryPoiImages = poiImages
            .Where(pair => pair.Value.Count > 0)
            .ToDictionary(pair => pair.Key, pair => pair.Value[0], StringComparer.OrdinalIgnoreCase);
        var primaryFoodImages = foodImages
            .Where(pair => pair.Value.Count > 0)
            .ToDictionary(pair => pair.Key, pair => pair.Value[0], StringComparer.OrdinalIgnoreCase);

        var orderedPois = bootstrap.Pois
            .Where(item => IsPublishedContent(item.Status) && IsValidLatitude(item.Lat) && IsValidLongitude(item.Lng))
            .OrderByDescending(item => item.Featured)
            .ThenByDescending(item => item.PopularityScore)
            .ThenBy(item => item.Slug, StringComparer.OrdinalIgnoreCase)
            .ToList();
        var publishedPoiIds = orderedPois
            .Select(item => item.Id)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var poiLocations = orderedPois
            .Select(poi =>
            {
                translationsByPoiId.TryGetValue(poi.Id, out var poiTranslations);
                var category = LocalizeCategory(GetCategoryName(categoriesById, poi.CategoryId));
                return CreatePoiLocation(
                    poi,
                    category,
                    poiTranslations,
                    ResolvePoiImageUrl(poi.Id, primaryPoiImages, primaryFoodImages));
            })
            .ToList();

        return new BootstrapSnapshot(
            poiLocations,
            BuildHeatPoints(orderedPois, bootstrap.UsageEvents),
            ResolveBackdropImageUrl(poiLocations, primaryPoiImages),
            new UserProfileCard(),
            new Dictionary<string, PoiExperienceDetail>(StringComparer.OrdinalIgnoreCase),
            premiumOffer,
            supportedLanguages,
            BuildRouteSnapshots(bootstrap.Routes, accessibleTranslations, publishedPoiIds));
    }

    private TourPlan? TryBuildTourPlanFromSnapshot(
        BootstrapSnapshot? snapshot,
        string? tourId = null,
        IReadOnlyCollection<string>? completedPoiIds = null)
    {
        if (snapshot is null || snapshot.Routes.Count == 0)
        {
            return null;
        }

        var poiLookup = snapshot.Pois.ToDictionary(item => item.Id, StringComparer.OrdinalIgnoreCase);
        var route = snapshot.Routes
            .Where(item =>
                string.IsNullOrWhiteSpace(tourId) ||
                string.Equals(item.Id, tourId, StringComparison.OrdinalIgnoreCase))
            .Where(item => item.StopPoiIds.Count > 0)
            .OrderByDescending(item => item.UpdatedAt)
            .FirstOrDefault();
        if (route is null)
        {
            return null;
        }

        var stopPoiIds = route.StopPoiIds
            .Where(poiLookup.ContainsKey)
            .ToList();
        if (stopPoiIds.Count == 0)
        {
            return null;
        }

        var completedPoiIdSet = completedPoiIds?
            .Where(item => !string.IsNullOrWhiteSpace(item))
            .ToHashSet(StringComparer.OrdinalIgnoreCase)
            ?? new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var stops = stopPoiIds
            .Select((poiId, index) => CreateTourStop(poiLookup, poiId, index + 1))
            .ToList();
        var checkpoints = stopPoiIds
            .Select((poiId, index) =>
            {
                var distanceText = poiLookup.TryGetValue(poiId, out var poi)
                    ? poi.DistanceText
                    : FormatVisitDuration(15);
                return CreateCheckpoint(
                    poiLookup,
                    poiId,
                    index + 1,
                    distanceText,
                    completedPoiIdSet.Contains(poiId));
            })
            .ToList();
        var completedCount = checkpoints.Count(item => item.IsCompleted);
        var coverImageUrl = stopPoiIds
            .Select(poiId => poiLookup.TryGetValue(poiId, out var poi) ? poi.ThumbnailUrl : string.Empty)
            .FirstOrDefault(item => !string.IsNullOrWhiteSpace(item))
            ?? snapshot.BackdropImageUrl;

        return new TourPlan
        {
            Id = route.Id,
            Title = FirstNonEmpty(route.Name, route.Theme, GetTourThemeText()),
            Theme = FirstNonEmpty(route.Theme, route.Name, GetTourThemeText()),
            Description = FirstNonEmpty(route.Description, GetTourDescriptionText()),
            CoverImageUrl = coverImageUrl,
            DurationText = FormatVisitDuration(route.DurationMinutes),
            ProgressValue = checkpoints.Count == 0 ? 0 : (double)completedCount / checkpoints.Count,
            ProgressText = FormatTourProgressText(completedCount, checkpoints.Count),
            SummaryText = BuildRouteSummaryText(route, checkpoints.Count),
            Stops = stops,
            Checkpoints = checkpoints
        };
    }

    private TourCatalogItem CreateTourCatalogItem(
        RouteSnapshot route,
        IReadOnlyDictionary<string, PoiLocation> poiLookup)
    {
        var coverImageUrl = route.StopPoiIds
            .Select(poiId => poiLookup.TryGetValue(poiId, out var poi) ? poi.ThumbnailUrl : string.Empty)
            .FirstOrDefault(item => !string.IsNullOrWhiteSpace(item))
            ?? DefaultBackdropImageUrl;

        return new TourCatalogItem
        {
            Id = route.Id,
            Name = FirstNonEmpty(route.Name, route.Theme, GetTourThemeText()),
            Theme = FirstNonEmpty(route.Theme, route.Name, GetTourThemeText()),
            Description = BuildRouteSummaryText(route, route.StopPoiIds.Count),
            CoverImageUrl = coverImageUrl,
            DurationText = FormatVisitDuration(route.DurationMinutes),
            StopCountText = FormatTourStopCountText(route.StopPoiIds.Count),
            StopPoiIds = route.StopPoiIds
        };
    }

    private IReadOnlyList<LanguageOption> BuildSupportedLanguages(SystemSettingDto? settings)
    {
        var orderedCodes = new List<string>();
        var appSupportedLanguageCodes = AppLanguage.SupportedLanguages
            .Select(item => AppLanguage.NormalizeCode(item.Code))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        void AddCode(string? code)
        {
            var normalizedCode = AppLanguage.NormalizeCode(code);
            if (!string.IsNullOrWhiteSpace(normalizedCode) &&
                appSupportedLanguageCodes.Contains(normalizedCode) &&
                !orderedCodes.Contains(normalizedCode, StringComparer.OrdinalIgnoreCase))
            {
                orderedCodes.Add(normalizedCode);
            }
        }

        AddCode(_languageService.CurrentLanguage);

        foreach (var definition in AppLanguage.SupportedLanguages)
        {
            AddCode(definition.Code);
        }

        foreach (var code in settings?.SupportedLanguages ?? [])
        {
            AddCode(code);
        }

        if (orderedCodes.Count == 0)
        {
            orderedCodes.AddRange(AppLanguage.SupportedLanguages.Select(item => AppLanguage.NormalizeCode(item.Code)));
        }

        return orderedCodes
            .Select(code => CreateLanguageOption(code))
            .ToList();
    }

#if false
    private LanguageOption CreateLanguageOptionLegacy(string code)
    {
        var template = Languages.FirstOrDefault(item =>
            string.Equals(item.Code, code, StringComparison.OrdinalIgnoreCase));

        return new LanguageOption
        {
            Code = code,
            Flag = template?.Flag ?? "🌐",
            DisplayName = template?.DisplayName ?? code,
            IsSelected = string.Equals(code, _languageService.CurrentLanguage, StringComparison.OrdinalIgnoreCase)
        };
    }

#endif

    private LanguageOption CreateLanguageOption(string code, bool isPremium = false, bool isLocked = false)
    {
        var normalizedCode = AppLanguage.NormalizeCode(code);
        var template = Languages.FirstOrDefault(item =>
            string.Equals(item.Code, normalizedCode, StringComparison.OrdinalIgnoreCase));

        return new LanguageOption
        {
            Code = normalizedCode,
            Flag = template?.Flag?.Trim() ?? "🌐",
            DisplayName = template?.DisplayName?.Trim() ?? normalizedCode,
            IsPremium = isPremium,
            IsLocked = isLocked,
            IsSelected = string.Equals(normalizedCode, _languageService.CurrentLanguage, StringComparison.OrdinalIgnoreCase)
        };
    }

    private PremiumPurchaseOffer BuildPremiumOffer(SystemSettingDto? settings)
    {
        var supportedLanguages = (settings?.SupportedLanguages?.Count ?? 0) > 0
            ? settings!.SupportedLanguages.Select(AppLanguage.NormalizeCode).Distinct(StringComparer.OrdinalIgnoreCase).ToArray()
            : ["vi", "en", "zh-CN", "ko", "ja"];

        return new PremiumPurchaseOffer
        {
            PriceUsd = 0,
            FreeLanguageCodes = supportedLanguages,
            PremiumLanguageCodes = Array.Empty<string>()
        };
    }

    private IReadOnlySet<string> GetAllowedLanguageCodeSet(SystemSettingDto? settings)
    {
        var supportedLanguages = BuildSupportedLanguages(settings)
            .Select(item => AppLanguage.NormalizeCode(item.Code))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        return supportedLanguages.Count == 0
            ? new HashSet<string>(["vi", "en", "zh-CN", "ko", "ja"], StringComparer.OrdinalIgnoreCase)
            : supportedLanguages;
    }

    private CustomerUserDto? ResolveCurrentCustomerForAccess(IReadOnlyList<CustomerUserDto> customerUsers)
        => null;

    private IReadOnlyList<RouteSnapshot> BuildRouteSnapshots(
        IReadOnlyList<RouteDto> routes,
        IReadOnlyList<TranslationDto> translations,
        IReadOnlySet<string> publishedPoiIds)
    {
        var routeTranslationsById = translations
            .Where(item => string.Equals(item.EntityType, "route", StringComparison.OrdinalIgnoreCase))
            .GroupBy(item => item.EntityId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.ToList(), StringComparer.OrdinalIgnoreCase);

        return routes
            .Where(route => route.IsActive)
            .Select(route =>
            {
                routeTranslationsById.TryGetValue(route.Id, out var routeTranslations);
                var translation = SelectRouteTranslation(routeTranslations, route.Id);
                var stopPoiIds = route.StopPoiIds
                    .Where(publishedPoiIds.Contains)
                    .ToList();
                var localizedRouteTheme = LocalizeRouteTheme(route.Theme);

                return new RouteSnapshot(
                    route.Id,
                    FirstNonEmpty(
                        GetTranslationText(translation, value => value.Title),
                        localizedRouteTheme,
                        GetTourThemeText()),
                    FirstNonEmpty(localizedRouteTheme, GetTourThemeText()),
                    FirstNonEmpty(
                        GetTranslationText(translation, value => value.FullText, value => value.ShortText),
                        GetSourceTextForCurrentLanguage(route.Description)),
                    route.DurationMinutes,
                    route.UpdatedAt,
                    stopPoiIds);
            })
            .Where(route => route.StopPoiIds.Count > 0)
            .ToList();
    }

    private TranslationDto? SelectRouteTranslation(IReadOnlyList<TranslationDto>? translations, string routeId)
    {
        return SelectTranslation(translations, CurrentLanguageCode, "route", routeId);
    }

    private static IReadOnlyDictionary<string, IReadOnlyList<string>> BuildMediaImageLookupByEntityId(
        IReadOnlyList<MediaAssetDto> mediaAssets,
        string entityType)
    {
        return mediaAssets
            .Where(item =>
                string.Equals(item.EntityType, entityType, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(item.Type, "image", StringComparison.OrdinalIgnoreCase) &&
                !string.IsNullOrWhiteSpace(item.EntityId) &&
                !string.IsNullOrWhiteSpace(item.Url))
            .GroupBy(item => item.EntityId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => (IReadOnlyList<string>)group
                    .OrderByDescending(item => item.CreatedAt)
                    .Select(item => item.Url.Trim())
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList(),
                StringComparer.OrdinalIgnoreCase);
    }

    private static IReadOnlyDictionary<string, IReadOnlyList<string>> BuildFoodImagesByPoiId(
        IReadOnlyList<FoodItemDto> foodItems,
        IReadOnlyDictionary<string, IReadOnlyList<string>> foodItemImagesById)
    {
        return foodItems
            .Where(item => !string.IsNullOrWhiteSpace(item.PoiId))
            .GroupBy(item => item.PoiId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => (IReadOnlyList<string>)group
                    .SelectMany(item => EnumerateFoodItemImages(item, foodItemImagesById))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList(),
                StringComparer.OrdinalIgnoreCase);
    }

    private static IEnumerable<string> EnumerateFoodItemImages(
        FoodItemDto item,
        IReadOnlyDictionary<string, IReadOnlyList<string>> foodItemImagesById)
    {
        if (foodItemImagesById.TryGetValue(item.Id, out var mediaImages))
        {
            foreach (var mediaImage in mediaImages.Where(image => !string.IsNullOrWhiteSpace(image)))
            {
                yield return mediaImage.Trim();
            }
        }

        if (!string.IsNullOrWhiteSpace(item.ImageUrl))
        {
            yield return item.ImageUrl.Trim();
        }
    }

    private static bool IsPublishedContent(string? status)
        => string.Equals(status?.Trim(), "published", StringComparison.OrdinalIgnoreCase);

    private string BuildRouteSummaryText(RouteSnapshot route, int stopCount)
    {
        if (!string.IsNullOrWhiteSpace(route.Description))
        {
            return route.Description;
        }

        return SelectLocalizedText(CreateLocalizedMap(
            $"Tuyến này có {stopCount} điểm dừng đang được xuất bản trên app.",
            $"This route currently includes {stopCount} published stops in the app.",
            $"该路线当前包含 {stopCount} 个已在应用发布的站点。",
            $"이 경로에는 현재 앱에 게시된 정차 지점 {stopCount}곳이 포함됩니다.",
            $"このルートには現在、アプリ上で公開されている立ち寄り先が {stopCount} か所あります。",
            $"Cet itinéraire comprend actuellement {stopCount} étapes publiées dans l'application."));
    }

    private string FormatTourStopCountText(int stopCount)
        => SelectLocalizedText(CreateLocalizedMap(
            $"{stopCount} điểm dừng",
            $"{stopCount} stops",
            $"{stopCount} 个站点",
            $"{stopCount}개 정차",
            $"{stopCount} か所の立ち寄り先",
            $"{stopCount} étapes"));

    private static IReadOnlyList<MapHeatPoint> BuildHeatPoints(
        IReadOnlyList<PoiDto> pois,
        IReadOnlyList<AppUsageEventDto> usageEvents)
    {
        if (pois.Count == 0)
        {
            return Array.Empty<MapHeatPoint>();
        }

        var heatPoints = new List<MapHeatPoint>(pois.Count * 3);
        for (var index = 0; index < pois.Count; index++)
        {
            var poi = pois[index];
            var intensity = ResolveHeatIntensity(poi, usageEvents);
            heatPoints.Add(new MapHeatPoint { Latitude = poi.Lat, Longitude = poi.Lng, Intensity = intensity });

            var extraPointCount = poi.Featured ? 2 : intensity >= 0.72 ? 1 : 0;
            for (var extraIndex = 0; extraIndex < extraPointCount; extraIndex++)
            {
                var offset = HeatOffsets[(index + extraIndex) % HeatOffsets.Length];
                heatPoints.Add(new MapHeatPoint
                {
                    Latitude = poi.Lat + offset.LatitudeOffset,
                    Longitude = poi.Lng + offset.LongitudeOffset,
                    Intensity = Math.Max(0.34, intensity - (0.12 * (extraIndex + 1)))
                });
            }
        }

        return heatPoints;
    }

    private static double ResolveHeatIntensity(
        PoiDto poi,
        IReadOnlyList<AppUsageEventDto> usageEvents)
    {
        var popularityScore = Clamp(poi.PopularityScore / 100d, 0.35, 1.0);
        var poiEvents = usageEvents
            .Where(item => string.Equals(item.PoiId, poi.Id, StringComparison.OrdinalIgnoreCase))
            .ToList();
        var viewCount = poiEvents.Count(item => string.Equals(item.EventType, "poi_view", StringComparison.OrdinalIgnoreCase));
        var audioCount = poiEvents.Count(item => string.Equals(item.EventType, "audio_play", StringComparison.OrdinalIgnoreCase));
        var qrCount = poiEvents.Count(item => string.Equals(item.EventType, "qr_scan", StringComparison.OrdinalIgnoreCase));
        var activityBoost = Math.Min(0.24, (viewCount * 0.03) + (audioCount * 0.04) + (qrCount * 0.05));
        var featuredBoost = poi.Featured ? 0.08 : 0;

        return Clamp((popularityScore * 0.72) + activityBoost + featuredBoost, 0.38, 1.0);
    }

    private TourStop CreateTourStop(IReadOnlyDictionary<string, PoiLocation> poiLookup, string poiId, int order)
    {
        if (poiLookup.TryGetValue(poiId, out var localizedPoi))
        {
            return new TourStop
            {
                PoiId = poiId,
                Title = $"{order}. {localizedPoi.Title}",
                Description = GetTourStopDescription(localizedPoi.DistanceText),
                ThumbnailUrl = localizedPoi.ThumbnailUrl,
                DistanceText = localizedPoi.DistanceText
            };
        }

        return new TourStop
        {
            PoiId = poiId,
            Title = $"{order}. {GetFallbackStopTitle()}",
            Description = GetTourStopDescription("100 m"),
            ThumbnailUrl = DefaultBackdropImageUrl,
            DistanceText = "100 m"
        };
    }

    private TourCheckpoint CreateCheckpoint(
        IReadOnlyDictionary<string, PoiLocation> poiLookup,
        string poiId,
        int order,
        string distanceText,
        bool isCompleted)
    {
        var localizedTitle = poiLookup.TryGetValue(poiId, out var localizedPoi)
            ? localizedPoi.Title
            : $"{GetFallbackStopTitle()} {order}";

        return new TourCheckpoint
        {
            PoiId = poiId,
            Order = order,
            Title = localizedTitle,
            DistanceText = LocalizeDistanceText(distanceText),
            IsCompleted = isCompleted
        };
    }

    private UserProfileCard ResolveUserProfile(IReadOnlyList<CustomerUserDto> customerUsers)
        => new UserProfileCard();

    private static string ResolveBackdropImageUrl(
        IReadOnlyList<PoiLocation> pois,
        IReadOnlyDictionary<string, string> poiImages)
    {
        var featuredPoiImage = pois
            .Where(item => item.IsFeatured)
            .Select(item => item.ThumbnailUrl)
            .FirstOrDefault(item => !string.IsNullOrWhiteSpace(item));
        if (!string.IsNullOrWhiteSpace(featuredPoiImage))
        {
            return featuredPoiImage;
        }

        if (poiImages.TryGetValue("poi-bbq-night", out var streetImage) && !string.IsNullOrWhiteSpace(streetImage))
        {
            return streetImage;
        }

        return DefaultBackdropImageUrl;
    }

    private static string ResolvePoiImageUrl(
        string poiId,
        IReadOnlyDictionary<string, string> poiImages,
        IReadOnlyDictionary<string, string> foodImages)
    {
        if (poiImages.TryGetValue(poiId, out var poiImage) && !string.IsNullOrWhiteSpace(poiImage))
        {
            return poiImage;
        }

        if (foodImages.TryGetValue(poiId, out var foodImage) && !string.IsNullOrWhiteSpace(foodImage))
        {
            return foodImage;
        }

        return FallbackPoiImages.TryGetValue(poiId, out var fallbackImage) ? fallbackImage : DefaultBackdropImageUrl;
    }

    private TranslationDto? SelectTranslation(
        IReadOnlyList<TranslationDto>? translations,
        string currentLanguage,
        string? entityType = null,
        string? entityId = null)
    {
        if (translations is null || translations.Count == 0)
        {
            LogMissingTranslation(entityType, entityId, currentLanguage);
            return null;
        }

        var requestedLanguage = AppLanguage.NormalizeCode(currentLanguage);
        foreach (var candidate in GetTranslationFallbackCandidates(requestedLanguage))
        {
            var match = translations.FirstOrDefault(item =>
                HasTranslationContent(item, candidate) &&
                string.Equals(item.LanguageCode, candidate, StringComparison.OrdinalIgnoreCase));
            if (match is not null)
            {
                if (!AppLanguage.GetCandidateCodes(requestedLanguage, includeEnglishFallback: false)
                        .Contains(candidate, StringComparer.OrdinalIgnoreCase))
                {
                    _logger.LogInformation(
                        "Using fallback translation for {EntityType}:{EntityId}. Requested={RequestedLanguage}; Fallback={FallbackLanguage}.",
                        entityType ?? match.EntityType,
                        entityId ?? match.EntityId,
                        requestedLanguage,
                        match.LanguageCode);
                }

                return match;
            }
        }

        LogMissingTranslation(entityType, entityId, requestedLanguage);
        return null;
    }

    private static bool HasTranslationContent(TranslationDto translation, string languageCode)
        => LocalizationFallbackPolicy.IsUsableTextForLanguage(translation.Title, languageCode) ||
           LocalizationFallbackPolicy.IsUsableTextForLanguage(translation.ShortText, languageCode) ||
           LocalizationFallbackPolicy.IsUsableTextForLanguage(translation.FullText, languageCode);

    private bool RequiresRemoteLocalizedBootstrap(AdminBootstrapDto bootstrap, string requestedLanguageCode)
    {
        var normalizedLanguageCode = AppLanguage.NormalizeCode(requestedLanguageCode);
        if (LocalizationFallbackPolicy.IsSourceLanguage(normalizedLanguageCode))
        {
            return false;
        }

        var poisAreLocalized = bootstrap.Pois
            .Where(item => IsPublishedContent(item.Status))
            .All(item =>
                HasUsableLocalizedSourceTextSet(
                    normalizedLanguageCode,
                    item.Title,
                    item.AudioScript,
                    item.Description,
                    item.ShortDescription) ||
                HasLocalizedTranslation(bootstrap.Translations, "poi", item.Id, normalizedLanguageCode));

        var foodItemsAreLocalized = bootstrap.FoodItems.Count == 0 ||
            bootstrap.FoodItems.All(item =>
                HasUsableLocalizedSourceTextSet(
                    normalizedLanguageCode,
                    item.Name,
                    item.Description) ||
                HasLocalizedTranslation(bootstrap.Translations, "food_item", item.Id, normalizedLanguageCode));

        var promotionsAreLocalized = bootstrap.Promotions.Count == 0 ||
            bootstrap.Promotions.All(item =>
                HasUsableLocalizedSourceTextSet(
                    normalizedLanguageCode,
                    item.Title,
                    item.Description) ||
                HasLocalizedTranslation(bootstrap.Translations, "promotion", item.Id, normalizedLanguageCode));

        var needsRefresh = !poisAreLocalized || !foodItemsAreLocalized || !promotionsAreLocalized;
        if (needsRefresh)
        {
            _logger.LogInformation(
                "[BootstrapMap] Cached bootstrap content is incomplete for language {LanguageCode}. Forcing remote localized refresh. poisLocalized={PoisLocalized}; foodLocalized={FoodLocalized}; promotionsLocalized={PromotionsLocalized}",
                normalizedLanguageCode,
                poisAreLocalized,
                foodItemsAreLocalized,
                promotionsAreLocalized);
        }

        return needsRefresh;
    }

    private static bool HasLocalizedTranslation(
        IReadOnlyList<TranslationDto> translations,
        string entityType,
        string entityId,
        string requestedLanguageCode)
    {
        var normalizedLanguageCode = AppLanguage.NormalizeCode(requestedLanguageCode);
        return translations.Any(item =>
            string.Equals(item.EntityType, entityType, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(item.EntityId, entityId, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(AppLanguage.NormalizeCode(item.LanguageCode), normalizedLanguageCode, StringComparison.OrdinalIgnoreCase) &&
            HasLocalizedTranslationContent(item, entityType, normalizedLanguageCode));
    }

    private static bool HasLocalizedTranslationContent(
        TranslationDto translation,
        string entityType,
        string requestedLanguageCode)
    {
        var titleIsUsable = LocalizationFallbackPolicy.IsUsableTextForLanguage(
            translation.Title,
            requestedLanguageCode);
        var hasBodyText =
            !string.IsNullOrWhiteSpace(translation.FullText) ||
            !string.IsNullOrWhiteSpace(translation.ShortText);
        var bodyIsUsable = LocalizationFallbackPolicy.IsUsableTextForLanguage(
            FirstNonEmpty(translation.FullText, translation.ShortText),
            requestedLanguageCode);

        return NormalizeEntityTypeForLocalization(entityType) switch
        {
            "poi" => titleIsUsable && bodyIsUsable,
            "promotion" => titleIsUsable && bodyIsUsable,
            "food_item" => titleIsUsable && (!hasBodyText || bodyIsUsable),
            _ => HasTranslationContent(translation, requestedLanguageCode)
        };
    }

    private static bool HasUsableLocalizedSourceTextSet(
        string requestedLanguageCode,
        string? title,
        params string?[] bodyValues)
    {
        if (!HasUsableLocalizedSourceText(title, requestedLanguageCode))
        {
            return false;
        }

        var hasBodySource = bodyValues.Any(value => !string.IsNullOrWhiteSpace(value));
        return !hasBodySource ||
               bodyValues.Any(value => HasUsableLocalizedSourceText(value, requestedLanguageCode));
    }

    private static bool HasUsableLocalizedSourceText(string? value, string requestedLanguageCode)
        => LocalizationFallbackPolicy.IsUsableTextForLanguage(value, requestedLanguageCode);

    private static string NormalizeEntityTypeForLocalization(string? entityType)
        => string.IsNullOrWhiteSpace(entityType)
            ? string.Empty
            : entityType.Trim().Replace('-', '_').ToLowerInvariant();

    private static IReadOnlyList<string> GetTranslationFallbackCandidates(string currentLanguage)
    {
        return LocalizationFallbackPolicy.GetDisplayTextFallbackCandidates(currentLanguage);
    }

    private static void AddTranslationFallbackCandidates(ICollection<string> candidates, IEnumerable<string> values)
    {
        foreach (var value in values)
        {
            if (!string.IsNullOrWhiteSpace(value) &&
                !candidates.Contains(value, StringComparer.OrdinalIgnoreCase))
            {
                candidates.Add(value.Trim());
            }
        }
    }

    private void LogMissingTranslation(string? entityType, string? entityId, string requestedLanguage)
    {
        if (string.IsNullOrWhiteSpace(entityType) && string.IsNullOrWhiteSpace(entityId))
        {
            return;
        }

        _logger.LogInformation(
            "Missing translation for {EntityType}:{EntityId} in language {LanguageCode}. Falling back only to configured language order.",
            entityType ?? "unknown",
            entityId ?? "unknown",
            AppLanguage.NormalizeCode(requestedLanguage));
    }

    private static bool IsPlayableAudioGuide(AudioGuideDto audioGuide)
    {
        if (audioGuide is null ||
            string.IsNullOrWhiteSpace(audioGuide.AudioUrl) ||
            !string.Equals(audioGuide.Status, "ready", StringComparison.OrdinalIgnoreCase) ||
            audioGuide.IsOutdated)
        {
            return false;
        }

        var generationStatus = NormalizeGenerationStatus(audioGuide.GenerationStatus);
        if (generationStatus is "failed" or "outdated" or "pending")
        {
            return false;
        }

        return !string.Equals(audioGuide.SourceType, "generated", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(generationStatus, "success", StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeGenerationStatus(string? generationStatus)
        => string.IsNullOrWhiteSpace(generationStatus)
            ? "none"
            : generationStatus.Trim().ToLowerInvariant();

    private static string ResolveApiBaseUrl(MobileRuntimeAppSettings runtimeSettings)
        => MobileApiEndpointHelper.ResolveBaseUrl(runtimeSettings.ApiBaseUrl, runtimeSettings.PlatformApiBaseUrls);

    private static string EnsureTrailingSlash(string baseUrl)
        => MobileApiEndpointHelper.EnsureTrailingSlash(baseUrl);

    private static string GetCategoryName(IReadOnlyDictionary<string, string> categoriesById, string categoryId)
        => categoriesById.TryGetValue(categoryId, out var categoryName) && !string.IsNullOrWhiteSpace(categoryName)
            ? categoryName
            : string.Empty;

    private string CreateSafePoiTitleFallback(string value)
        => LocalizationFallbackPolicy.CanUseSourceLanguageText(CurrentLanguageCode)
            ? CreateTitleFromSlug(value)
            : "Vinh Khanh destination";

    private static string CreateTitleFromSlug(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "Vinh Khanh destination";
        }

        var normalized = value.Replace('-', ' ').Trim();
        return CultureInfo.GetCultureInfo("vi-VN").TextInfo.ToTitleCase(normalized);
    }

    private static string FirstNonEmpty(params string?[] values)
        => values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))?.Trim() ?? string.Empty;

    private static string BuildInitials(params string?[] values)
    {
        var source = values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))?.Trim();
        if (string.IsNullOrWhiteSpace(source))
        {
            return "VK";
        }

        var parts = source
            .Split([' ', '.', '-', '_'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length >= 2)
        {
            return $"{char.ToUpperInvariant(parts[0][0])}{char.ToUpperInvariant(parts[^1][0])}";
        }

        return source.Length >= 2
            ? source[..2].ToUpperInvariant()
            : source.ToUpperInvariant();
    }

    private static double Clamp(double value, double min, double max)
        => Math.Min(max, Math.Max(min, value));

    private static bool IsValidCoordinate(double value)
        => !double.IsNaN(value) && !double.IsInfinity(value);

    private static bool IsValidLatitude(double value)
        => IsValidCoordinate(value) && value is >= -90d and <= 90d;

    private static bool IsValidLongitude(double value)
        => IsValidCoordinate(value) && value is >= -180d and <= 180d;

    private static string ResolvePlatformCode()
        => "android";

    private sealed record BootstrapSnapshot(
        IReadOnlyList<PoiLocation> Pois,
        IReadOnlyList<MapHeatPoint> HeatPoints,
        string BackdropImageUrl,
        UserProfileCard UserProfile,
        IReadOnlyDictionary<string, PoiExperienceDetail> PoiDetails,
        PremiumPurchaseOffer PremiumOffer,
        IReadOnlyList<LanguageOption> SupportedLanguages,
        IReadOnlyList<RouteSnapshot> Routes);

    private sealed record RouteSnapshot(
        string Id,
        string Name,
        string Theme,
        string Description,
        int DurationMinutes,
        DateTimeOffset UpdatedAt,
        IReadOnlyList<string> StopPoiIds);

    private sealed class MobileRuntimeAppSettings
    {
        public string? ApiBaseUrl { get; set; }
        public Dictionary<string, string> PlatformApiBaseUrls { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    }

    private sealed record ApiEnvelope<T>(bool Success, T? Data, string? Message);

    private sealed class AdminBootstrapDto
    {
        public List<PoiCategoryDto> Categories { get; set; } = [];
        public List<CustomerUserDto> CustomerUsers { get; set; } = [];
        public List<PoiDto> Pois { get; set; } = [];
        public List<TranslationDto> Translations { get; set; } = [];
        public List<AudioGuideDto> AudioGuides { get; set; } = [];
        public List<MediaAssetDto> MediaAssets { get; set; } = [];
        public List<FoodItemDto> FoodItems { get; set; } = [];
        public List<RouteDto> Routes { get; set; } = [];
        public List<PromotionDto> Promotions { get; set; } = [];
        public List<AppUsageEventDto> UsageEvents { get; set; } = [];
        public List<ViewLogDto> ViewLogs { get; set; } = [];
        public List<AudioListenLogDto> AudioListenLogs { get; set; } = [];
        public SystemSettingDto? Settings { get; set; }
        public DataSyncStateDto? SyncState { get; set; }
    }

    private sealed class PoiDetailDto
    {
        public PoiDto? Poi { get; set; }
        public List<TranslationDto> Translations { get; set; } = [];
        public List<AudioGuideDto> AudioGuides { get; set; } = [];
        public List<FoodItemDto> FoodItems { get; set; } = [];
        public List<TranslationDto> FoodItemTranslations { get; set; } = [];
        public List<PromotionDto> Promotions { get; set; } = [];
        public List<TranslationDto> PromotionTranslations { get; set; } = [];
        public List<MediaAssetDto> MediaAssets { get; set; } = [];
    }

    private sealed class AppUsageEventCreateRequestDto
    {
        public string EventType { get; set; } = string.Empty;
        public string? PoiId { get; set; }
        public string LanguageCode { get; set; } = AppLanguage.DefaultLanguage;
        public string Platform { get; set; } = "android";
        public string SessionId { get; set; } = string.Empty;
        public string Source { get; set; } = "mobile_app";
        public string? Metadata { get; set; }
        public int? DurationInSeconds { get; set; }
    }

    private sealed class CustomerUserDto
    {
        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public string Phone { get; set; } = string.Empty;
        public string PreferredLanguage { get; set; } = "vi";
        public string? Username { get; set; }
        public string Country { get; set; } = string.Empty;
        public bool IsPremium { get; set; }
        public DateTimeOffset CreatedAt { get; set; }
        public DateTimeOffset? LastActiveAt { get; set; }
    }

    private sealed class PoiCategoryDto
    {
        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
    }

    private sealed class PoiDto
    {
        public string Id { get; set; } = string.Empty;
        public string Slug { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public string ShortDescription { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string AudioScript { get; set; } = string.Empty;
        public string SourceLanguageCode { get; set; } = AppLanguage.DefaultLanguage;
        public string Address { get; set; } = string.Empty;
        public double Lat { get; set; }
        public double Lng { get; set; }
        public string CategoryId { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public bool Featured { get; set; }
        public string PriceRange { get; set; } = string.Empty;
        public double TriggerRadius { get; set; }
        public int Priority { get; set; }
        public int AverageVisitDuration { get; set; }
        public int PopularityScore { get; set; }
        public List<string> Tags { get; set; } = [];
    }

    private sealed class TranslationDto
    {
        public string EntityType { get; set; } = string.Empty;
        public string EntityId { get; set; } = string.Empty;
        public string LanguageCode { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public string ShortText { get; set; } = string.Empty;
        public string FullText { get; set; } = string.Empty;
    }

    private sealed class MediaAssetDto
    {
        public string EntityType { get; set; } = string.Empty;
        public string EntityId { get; set; } = string.Empty;
        public string Type { get; set; } = string.Empty;
        public string Url { get; set; } = string.Empty;
        public string AltText { get; set; } = string.Empty;
        public DateTimeOffset CreatedAt { get; set; }
    }

    private sealed class FoodItemDto
    {
        public string Id { get; set; } = string.Empty;
        public string PoiId { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string PriceRange { get; set; } = string.Empty;
        public string ImageUrl { get; set; } = string.Empty;
        public string SpicyLevel { get; set; } = string.Empty;
    }

    private sealed class RouteDto
    {
        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string Theme { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public int DurationMinutes { get; set; }
        public string CoverImageUrl { get; set; } = string.Empty;
        public List<string> StopPoiIds { get; set; } = [];
        public bool IsActive { get; set; }
        public DateTimeOffset UpdatedAt { get; set; }
    }

    private sealed class PromotionDto
    {
        public string Id { get; set; } = string.Empty;
        public string PoiId { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public DateTimeOffset StartAt { get; set; }
        public DateTimeOffset EndAt { get; set; }
        public string Status { get; set; } = string.Empty;
    }

    private sealed class AudioGuideDto
    {
        public string Id { get; set; } = string.Empty;
        public string EntityType { get; set; } = string.Empty;
        public string EntityId { get; set; } = string.Empty;
        public string LanguageCode { get; set; } = string.Empty;
        public string AudioUrl { get; set; } = string.Empty;
        public string SourceType { get; set; } = string.Empty;
        public string ContentVersion { get; set; } = string.Empty;
        public string TextHash { get; set; } = string.Empty;
        public double? DurationInSeconds { get; set; }
        public long? FileSizeBytes { get; set; }
        public string Status { get; set; } = string.Empty;
        public string GenerationStatus { get; set; } = string.Empty;
        public bool IsOutdated { get; set; }
        public DateTimeOffset UpdatedAt { get; set; }
    }

    private sealed class AppUsageEventDto
    {
        public string Id { get; set; } = string.Empty;
        public string EventType { get; set; } = string.Empty;
        public string? PoiId { get; set; }
        public string LanguageCode { get; set; } = AppLanguage.DefaultLanguage;
        public string Platform { get; set; } = "android";
        public string SessionId { get; set; } = string.Empty;
        public string Source { get; set; } = string.Empty;
        public string? Metadata { get; set; }
        public int? DurationInSeconds { get; set; }
        public DateTimeOffset OccurredAt { get; set; }
    }

    private sealed class ViewLogDto
    {
        public string PoiId { get; set; } = string.Empty;
    }

    private sealed class AudioListenLogDto
    {
        public string PoiId { get; set; } = string.Empty;
    }

    private sealed class SystemSettingDto
    {
        public string DefaultLanguage { get; set; } = "vi";
        public string FallbackLanguage { get; set; } = "en";
        public List<string> SupportedLanguages { get; set; } = [];
        public List<string> FreeLanguages { get; set; } = [];
        public List<string> PremiumLanguages { get; set; } = [];
        public int PremiumUnlockPriceUsd { get; set; } = DefaultPremiumPriceUsd;
    }

    private sealed class DataSyncStateDto
    {
        public string Version { get; set; } = string.Empty;
        public DateTimeOffset GeneratedAt { get; set; }
        public DateTimeOffset LastChangedAt { get; set; }
    }
}
