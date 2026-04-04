using System.Globalization;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Maui.Devices;
using Microsoft.Maui.Storage;
using Microsoft.Extensions.Logging;
using VinhKhanh.MobileApp.Models;

namespace VinhKhanh.MobileApp.Services;

public interface IFoodStreetDataService
{
    Task<IReadOnlyList<LanguageOption>> GetLanguagesAsync();
    Task<IReadOnlyList<PoiLocation>> GetPoisAsync();
    Task<IReadOnlyList<MapHeatPoint>> GetHeatPointsAsync();
    Task<PoiExperienceDetail?> GetPoiDetailAsync(string poiId);
    Task<TourPlan> GetTourPlanAsync();
    Task<UserProfileCard> GetUserProfileAsync();
    Task<IReadOnlyList<SettingsMenuItem>> GetSettingsMenuAsync();
    string GetBackdropImageUrl();
}

public sealed partial class FoodStreetMockDataService : IFoodStreetDataService
{
    private const string AppSettingsFileName = "appsettings.json";
    private const string BootstrapEndpoint = "api/v1/bootstrap";
    private const string SyncStateEndpoint = "api/v1/sync-state";
    private const string DefaultBackdropImageUrl = "https://images.unsplash.com/photo-1520201163981-8cc95007dd2e?auto=format&fit=crop&w=1200&q=80";
    private static readonly TimeSpan SyncCheckInterval = TimeSpan.FromSeconds(5);

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

    private static readonly IReadOnlyDictionary<string, string> FallbackPoiImages =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["poi-snail-signature"] = "https://images.unsplash.com/photo-1514933651103-005eec06c04b?auto=format&fit=crop&w=1200&q=80",
            ["poi-bbq-night"] = "https://images.unsplash.com/photo-1520201163981-8cc95007dd2e?auto=format&fit=crop&w=1200&q=80",
            ["poi-sweet-lane"] = "https://images.unsplash.com/photo-1563805042-7684c019e1cb?auto=format&fit=crop&w=1200&q=80"
        };

    private static readonly IReadOnlyList<LanguageOption> Languages =
    [
        new() { Code = "vi", Flag = "🇻🇳", DisplayName = "Tiếng Việt", IsSelected = true },
        new() { Code = "en", Flag = "🇺🇸", DisplayName = "English" },
        new() { Code = "zh-CN", Flag = "🇨🇳", DisplayName = "中文" },
        new() { Code = "ko", Flag = "🇰🇷", DisplayName = "한국어" },
        new() { Code = "ja", Flag = "🇯🇵", DisplayName = "日本語" },
        new() { Code = "fr", Flag = "🇫🇷", DisplayName = "Français" }
    ];

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
            Title = "Quảng trường Ẩm thực BBQ Night",
            ShortDescription = "Điểm tụ họp sôi động với món nướng hải sản và không khí phố đêm náo nhiệt.",
            Address = "126 Vĩnh Khánh, Phường Khánh Hội, TP.HCM",
            Category = "Hải sản nướng",
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
            Title = "Hẻm Chè Vĩnh Khánh",
            ShortDescription = "Điểm tráng miệng và món ngọt giúp cân bằng hành trình ăn uống.",
            Address = "88/4 Vĩnh Khánh, Phường Vĩnh Hội, TP.HCM",
            Category = "Món ngọt",
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
    private readonly IAppLanguageService _languageService;
    private readonly ILogger<FoodStreetMockDataService> _logger;
    private BootstrapSnapshot? _bootstrapSnapshot;
    private DataSyncStateDto? _syncState;
    private DateTimeOffset _lastSyncCheckAt = DateTimeOffset.MinValue;
    private MobileRuntimeAppSettings? _runtimeSettings;
    private HttpClient? _httpClient;
    private string? _resolvedBaseUrl;

    public FoodStreetMockDataService(
        IAppLanguageService languageService,
        ILogger<FoodStreetMockDataService> logger)
    {
        _languageService = languageService;
        _logger = logger;
        _languageService.LanguageChanged += (_, _) =>
        {
            _bootstrapSnapshot = null;
            _syncState = null;
            _lastSyncCheckAt = DateTimeOffset.MinValue;
            _detailCache.Clear();
        };
    }

    public async Task<IReadOnlyList<LanguageOption>> GetLanguagesAsync()
    {
        var snapshot = await GetBootstrapSnapshotAsync();
        var source = snapshot?.SupportedLanguages.Count > 0
            ? snapshot.SupportedLanguages
            : Languages;

        return source.Select(language => new LanguageOption
        {
            Code = language.Code,
            Flag = language.Flag,
            DisplayName = language.DisplayName,
            IsSelected = string.Equals(language.Code, _languageService.CurrentLanguage, StringComparison.OrdinalIgnoreCase)
        }).ToList();
    }

    public async Task<IReadOnlyList<PoiLocation>> GetPoisAsync()
    {
        var snapshot = await GetBootstrapSnapshotAsync();
        if (snapshot?.Pois.Count > 0)
        {
            return snapshot.Pois;
        }

        return await ShouldUseBundledFallbackAsync() ? BuildLocalizedFallbackPois() : [];
    }

    public async Task<IReadOnlyList<MapHeatPoint>> GetHeatPointsAsync()
    {
        var snapshot = await GetBootstrapSnapshotAsync();
        if (snapshot?.HeatPoints.Count > 0)
        {
            return snapshot.HeatPoints;
        }

        return await ShouldUseBundledFallbackAsync() ? FallbackHeatPoints : [];
    }

    public async Task<PoiExperienceDetail?> GetPoiDetailAsync(string poiId)
    {
        if (string.IsNullOrWhiteSpace(poiId))
        {
            return null;
        }

        if (_detailCache.TryGetValue(poiId, out var cachedDetail))
        {
            return cachedDetail;
        }

        var snapshot = await GetBootstrapSnapshotAsync();
        if (snapshot?.PoiDetails.TryGetValue(poiId, out var detail) == true)
        {
            _detailCache[poiId] = detail;
            return detail;
        }

        if (!await ShouldUseBundledFallbackAsync())
        {
            return null;
        }

        var fallbackDetail = BuildFallbackPoiDetail(poiId);
        if (fallbackDetail is not null)
        {
            _detailCache[poiId] = fallbackDetail;
        }

        return fallbackDetail;
    }

    public async Task<TourPlan> GetTourPlanAsync()
    {
        var snapshot = await GetBootstrapSnapshotAsync();
        var routePlan = TryBuildTourPlanFromSnapshot(snapshot);
        if (routePlan is not null)
        {
            return routePlan;
        }

        if (!await ShouldUseBundledFallbackAsync())
        {
            return CreateEmptyTourPlan();
        }

        var poiLookup = (await GetPoisAsync()).ToDictionary(item => item.Id, StringComparer.OrdinalIgnoreCase);
        var stops = new List<TourStop>
        {
            CreateTourStop(poiLookup, "poi-snail-signature", 1),
            CreateTourStop(poiLookup, "poi-bbq-night", 2),
            CreateTourStop(poiLookup, "poi-sweet-lane", 3)
        };
        var checkpoints = new List<TourCheckpoint>
        {
            CreateCheckpoint(poiLookup, "poi-snail-signature", 1, "150 m", true),
            CreateCheckpoint(poiLookup, "poi-bbq-night", 2, "2,50 km", true),
            CreateCheckpoint(poiLookup, "poi-sweet-lane", 3, "2,70 km", false)
        };

        return new TourPlan
        {
            Title = GetTourThemeText(),
            Theme = GetTourThemeText(),
            Description = GetTourDescriptionText(),
            ProgressValue = 0.66,
            ProgressText = FormatTourProgressText(checkpoints.Count(item => item.IsCompleted), checkpoints.Count),
            SummaryText = GetTourSummaryText(),
            Stops = stops,
            Checkpoints = checkpoints
        };
    }

    private TourPlan CreateEmptyTourPlan()
        => new()
        {
            Title = GetTourThemeText(),
            Theme = GetTourThemeText(),
            Description = GetTourDescriptionText(),
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
    {
        var snapshot = await GetBootstrapSnapshotAsync();
        if (snapshot?.UserProfile is not null)
        {
            return snapshot.UserProfile;
        }

        return new UserProfileCard
        {
            FullName = "Nguyễn Bảo Vy",
            Email = "baovy@gmail.com",
            Phone = "0911 000 111",
            AvatarInitials = "BV",
            MetaLine = $"ID customer-1 • android • {_languageService.CurrentLanguage}"
        };
    }

    public Task<IReadOnlyList<SettingsMenuItem>> GetSettingsMenuAsync()
        => Task.FromResult<IReadOnlyList<SettingsMenuItem>>(
        [
            new SettingsMenuItem { Icon = "🔔", Title = _languageService.GetText("settings_notifications") },
            new SettingsMenuItem { Icon = "💳", Title = _languageService.GetText("settings_cards") },
            new SettingsMenuItem { Icon = "🔒", Title = _languageService.GetText("settings_privacy") },
            new SettingsMenuItem { Icon = "❓", Title = _languageService.GetText("settings_support") }
        ]);

    public string GetBackdropImageUrl()
        => _bootstrapSnapshot?.BackdropImageUrl ?? DefaultBackdropImageUrl;
}

public sealed partial class FoodStreetMockDataService
{
    private static readonly (double LatitudeOffset, double LongitudeOffset)[] HeatOffsets =
    [
        (0.00018, 0.00006),
        (-0.00014, 0.00005),
        (0.00006, -0.00011),
        (-0.00008, -0.00007)
    ];

    private async Task<BootstrapSnapshot?> GetBootstrapSnapshotAsync()
    {
        await _bootstrapLock.WaitAsync();
        try
        {
            var client = await GetClientAsync();
            if (client is null)
            {
                return _bootstrapSnapshot;
            }

            if (_bootstrapSnapshot is null)
            {
                return await RefreshBootstrapSnapshotAsync(client, null, "initial");
            }

            if (!ShouldCheckSyncState())
            {
                return _bootstrapSnapshot;
            }

            var remoteSyncState = await FetchSyncStateAsync(client);
            _lastSyncCheckAt = DateTimeOffset.UtcNow;

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

            return await RefreshBootstrapSnapshotAsync(client, remoteSyncState, "version-changed");
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

    private bool ShouldCheckSyncState()
        => _bootstrapSnapshot is null || DateTimeOffset.UtcNow - _lastSyncCheckAt >= SyncCheckInterval;

    private async Task<bool> ShouldUseBundledFallbackAsync()
        => !await HasRemoteApiConfiguredAsync();

    private async Task<bool> HasRemoteApiConfiguredAsync()
    {
        var runtimeSettings = await LoadRuntimeSettingsAsync();
        return !string.IsNullOrWhiteSpace(EnsureTrailingSlash(ResolveApiBaseUrl(runtimeSettings)));
    }

    private async Task<DataSyncStateDto?> FetchSyncStateAsync(HttpClient client)
    {
        using var response = await client.GetAsync(SyncStateEndpoint);
        if (!response.IsSuccessStatusCode)
        {
            return null;
        }

        var envelope = await response.Content.ReadFromJsonAsync<ApiEnvelope<DataSyncStateDto>>(JsonOptions);
        return envelope?.Success == true ? envelope.Data : null;
    }

    private async Task<BootstrapSnapshot?> RefreshBootstrapSnapshotAsync(
        HttpClient client,
        DataSyncStateDto? remoteSyncState,
        string reason)
    {
        using var response = await client.GetAsync(BootstrapEndpoint);
        if (!response.IsSuccessStatusCode)
        {
            return _bootstrapSnapshot;
        }

        var envelope = await response.Content.ReadFromJsonAsync<ApiEnvelope<AdminBootstrapDto>>(JsonOptions);
        if (envelope?.Success != true || envelope.Data is null)
        {
            return _bootstrapSnapshot;
        }

        var snapshot = CreateSnapshot(envelope.Data);
        _bootstrapSnapshot = snapshot;
        _syncState = envelope.Data.SyncState ?? remoteSyncState;
        _lastSyncCheckAt = DateTimeOffset.UtcNow;
        _detailCache.Clear();

        _logger.LogInformation(
            "Bootstrap snapshot refreshed ({Reason}). Version={Version}; pois={PoiCount}; routes={RouteCount}",
            reason,
            _syncState?.Version ?? "none",
            snapshot.Pois.Count,
            snapshot.Routes.Count);

        return _bootstrapSnapshot;
    }

    private async Task<HttpClient?> GetClientAsync()
    {
        var runtimeSettings = await LoadRuntimeSettingsAsync();
        var nextBaseUrl = EnsureTrailingSlash(ResolveApiBaseUrl(runtimeSettings));
        if (string.IsNullOrWhiteSpace(nextBaseUrl))
        {
            _logger.LogWarning("Mobile app has no API base URL configured. Using bundled fallback content.");
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
        var supportedLanguages = BuildSupportedLanguages(bootstrap.Settings);

        var translationsByPoiId = bootstrap.Translations
            .Where(item => string.Equals(item.EntityType, "poi", StringComparison.OrdinalIgnoreCase))
            .GroupBy(item => item.EntityId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.ToList(), StringComparer.OrdinalIgnoreCase);

        var poiImages = bootstrap.MediaAssets
            .Where(item =>
                string.Equals(item.EntityType, "poi", StringComparison.OrdinalIgnoreCase) &&
                string.Equals(item.Type, "image", StringComparison.OrdinalIgnoreCase) &&
                !string.IsNullOrWhiteSpace(item.Url))
            .GroupBy(item => item.EntityId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.OrderByDescending(item => item.CreatedAt).First().Url, StringComparer.OrdinalIgnoreCase);

        var foodImages = bootstrap.FoodItems
            .Where(item => !string.IsNullOrWhiteSpace(item.PoiId) && !string.IsNullOrWhiteSpace(item.ImageUrl))
            .GroupBy(item => item.PoiId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First().ImageUrl, StringComparer.OrdinalIgnoreCase);

        var orderedPois = bootstrap.Pois
            .Where(item => IsPublishedContent(item.Status) && IsValidCoordinate(item.Lat) && IsValidCoordinate(item.Lng))
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
            var translation = SelectTranslation(poi, poiTranslations, _languageService.CurrentLanguage);
            var title = FirstNonEmpty(translation?.Title, CreateTitleFromSlug(poi.Slug), poi.Id);
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
                HeatIntensity = ResolveHeatIntensity(poi, bootstrap.ViewLogs, bootstrap.AudioListenLogs),
                DistanceText = FormatVisitDuration(Math.Max(10, poi.AverageVisitDuration))
            };
        }).ToList();

        return new BootstrapSnapshot(
            poiLocations,
            BuildHeatPoints(orderedPois, bootstrap.ViewLogs, bootstrap.AudioListenLogs),
            ResolveBackdropImageUrl(poiLocations, poiImages),
            ResolveUserProfile(bootstrap.CustomerUsers),
            new Dictionary<string, PoiExperienceDetail>(StringComparer.OrdinalIgnoreCase),
            supportedLanguages,
            BuildRouteSnapshots(bootstrap.Routes, bootstrap.Translations, publishedPoiIds));
    }

    private BootstrapSnapshot CreateSnapshot(AdminBootstrapDto bootstrap)
    {
        var categoriesById = bootstrap.Categories
            .Where(item => !string.IsNullOrWhiteSpace(item.Id))
            .ToDictionary(item => item.Id, item => item.Name, StringComparer.OrdinalIgnoreCase);
        var supportedLanguages = BuildSupportedLanguages(bootstrap.Settings);

        var translationsByPoiId = bootstrap.Translations
            .Where(item => string.Equals(item.EntityType, "poi", StringComparison.OrdinalIgnoreCase))
            .GroupBy(item => item.EntityId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.ToList(), StringComparer.OrdinalIgnoreCase);

        var poiImages = bootstrap.MediaAssets
            .Where(item =>
                string.Equals(item.EntityType, "poi", StringComparison.OrdinalIgnoreCase) &&
                string.Equals(item.Type, "image", StringComparison.OrdinalIgnoreCase) &&
                !string.IsNullOrWhiteSpace(item.Url))
            .GroupBy(item => item.EntityId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => (IReadOnlyList<string>)group
                    .OrderByDescending(item => item.CreatedAt)
                    .Select(item => item.Url)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList(),
                StringComparer.OrdinalIgnoreCase);

        var foodImages = bootstrap.FoodItems
            .Where(item => !string.IsNullOrWhiteSpace(item.PoiId) && !string.IsNullOrWhiteSpace(item.ImageUrl))
            .GroupBy(item => item.PoiId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => (IReadOnlyList<string>)group
                    .Select(item => item.ImageUrl)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList(),
                StringComparer.OrdinalIgnoreCase);

        var audioGuidesByPoiId = bootstrap.AudioGuides
            .Where(item =>
                string.Equals(item.EntityType, "poi", StringComparison.OrdinalIgnoreCase) &&
                string.Equals(item.Status, "ready", StringComparison.OrdinalIgnoreCase) &&
                !string.IsNullOrWhiteSpace(item.AudioUrl))
            .GroupBy(item => item.EntityId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.ToList(), StringComparer.OrdinalIgnoreCase);

        var reviewsByPoiId = bootstrap.Reviews
            .Where(item =>
                string.Equals(item.Status, "approved", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(item.Status, "published", StringComparison.OrdinalIgnoreCase))
            .GroupBy(item => item.PoiId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.ToList(), StringComparer.OrdinalIgnoreCase);

        var orderedPois = bootstrap.Pois
            .Where(item => IsPublishedContent(item.Status) && IsValidCoordinate(item.Lat) && IsValidCoordinate(item.Lng))
            .OrderByDescending(item => item.Featured)
            .ThenByDescending(item => item.PopularityScore)
            .ThenBy(item => item.Slug, StringComparer.OrdinalIgnoreCase)
            .ToList();
        var publishedPoiIds = orderedPois
            .Select(item => item.Id)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var poiDetails = orderedPois.ToDictionary(poi => poi.Id, poi =>
        {
            translationsByPoiId.TryGetValue(poi.Id, out var poiTranslations);
            audioGuidesByPoiId.TryGetValue(poi.Id, out var poiAudioGuides);
            reviewsByPoiId.TryGetValue(poi.Id, out var poiReviews);

            return BuildPoiDetail(
                poi,
                GetCategoryName(categoriesById, poi.CategoryId),
                poiTranslations,
                poiAudioGuides,
                poiReviews,
                poiImages,
                foodImages);
        }, StringComparer.OrdinalIgnoreCase);

        var poiLocations = orderedPois
            .Select(poi => CreatePoiLocation(poi, poiDetails[poi.Id]))
            .ToList();

        return new BootstrapSnapshot(
            poiLocations,
            BuildHeatPoints(orderedPois, bootstrap.ViewLogs, bootstrap.AudioListenLogs),
            ResolveBackdropImageUrl(poiDetails),
            ResolveUserProfile(bootstrap.CustomerUsers),
            poiDetails,
            supportedLanguages,
            BuildRouteSnapshots(bootstrap.Routes, bootstrap.Translations, publishedPoiIds));
    }

    private TourPlan? TryBuildTourPlanFromSnapshot(BootstrapSnapshot? snapshot)
    {
        if (snapshot is null || snapshot.Routes.Count == 0)
        {
            return null;
        }

        var poiLookup = snapshot.Pois.ToDictionary(item => item.Id, StringComparer.OrdinalIgnoreCase);
        var route = snapshot.Routes
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

        var stops = stopPoiIds
            .Select((poiId, index) => CreateTourStop(poiLookup, poiId, index + 1))
            .ToList();
        var checkpoints = stopPoiIds
            .Select((poiId, index) =>
            {
                var distanceText = poiLookup.TryGetValue(poiId, out var poi)
                    ? poi.DistanceText
                    : FormatVisitDuration(15);
                return CreateCheckpoint(poiLookup, poiId, index + 1, distanceText, false);
            })
            .ToList();

        return new TourPlan
        {
            Title = FirstNonEmpty(route.Name, route.Theme, GetTourThemeText()),
            Theme = FirstNonEmpty(route.Theme, route.Name, GetTourThemeText()),
            Description = FirstNonEmpty(route.Description, GetTourDescriptionText()),
            ProgressValue = 0,
            ProgressText = FormatTourProgressText(0, checkpoints.Count),
            SummaryText = BuildRouteSummaryText(route, checkpoints.Count),
            Stops = stops,
            Checkpoints = checkpoints
        };
    }

    private IReadOnlyList<LanguageOption> BuildSupportedLanguages(SystemSettingDto? settings)
    {
        var orderedCodes = new List<string>();

        void AddCode(string? code)
        {
            if (!string.IsNullOrWhiteSpace(code) &&
                !orderedCodes.Contains(code.Trim(), StringComparer.OrdinalIgnoreCase))
            {
                orderedCodes.Add(code.Trim());
            }
        }

        AddCode(settings?.DefaultLanguage);
        AddCode(settings?.FallbackLanguage);

        foreach (var code in settings?.FreeLanguages ?? [])
        {
            AddCode(code);
        }

        foreach (var code in settings?.PremiumLanguages ?? [])
        {
            AddCode(code);
        }

        if (orderedCodes.Count == 0)
        {
            orderedCodes.AddRange(Languages.Select(item => item.Code));
        }

        return orderedCodes
            .Select(CreateLanguageOption)
            .ToList();
    }

    private LanguageOption CreateLanguageOption(string code)
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
                var translation = SelectRouteTranslation(routeTranslations);
                var stopPoiIds = route.StopPoiIds
                    .Where(publishedPoiIds.Contains)
                    .ToList();

                return new RouteSnapshot(
                    route.Id,
                    FirstNonEmpty(translation?.Title, route.Name),
                    route.Theme,
                    FirstNonEmpty(translation?.FullText, translation?.ShortText, route.Description),
                    route.UpdatedAt,
                    stopPoiIds);
            })
            .Where(route => route.StopPoiIds.Count > 0)
            .ToList();
    }

    private TranslationDto? SelectRouteTranslation(IReadOnlyList<TranslationDto>? translations)
    {
        if (translations is null || translations.Count == 0)
        {
            return null;
        }

        return translations.FirstOrDefault(item =>
                   !string.IsNullOrWhiteSpace(item.Title) &&
                   string.Equals(item.LanguageCode, _languageService.CurrentLanguage, StringComparison.OrdinalIgnoreCase))
               ?? translations.FirstOrDefault(item => !string.IsNullOrWhiteSpace(item.Title))
               ?? translations[0];
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

    private static IReadOnlyList<MapHeatPoint> BuildHeatPoints(
        IReadOnlyList<PoiDto> pois,
        IReadOnlyList<ViewLogDto> viewLogs,
        IReadOnlyList<AudioListenLogDto> audioListenLogs)
    {
        if (pois.Count == 0)
        {
            return FallbackHeatPoints;
        }

        var heatPoints = new List<MapHeatPoint>(pois.Count * 3);
        for (var index = 0; index < pois.Count; index++)
        {
            var poi = pois[index];
            var intensity = ResolveHeatIntensity(poi, viewLogs, audioListenLogs);
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
        IReadOnlyList<ViewLogDto> viewLogs,
        IReadOnlyList<AudioListenLogDto> audioListenLogs)
    {
        var popularityScore = Clamp(poi.PopularityScore / 100d, 0.35, 1.0);
        var viewCount = viewLogs.Count(item => string.Equals(item.PoiId, poi.Id, StringComparison.OrdinalIgnoreCase));
        var audioCount = audioListenLogs.Count(item => string.Equals(item.PoiId, poi.Id, StringComparison.OrdinalIgnoreCase));
        var activityBoost = Math.Min(0.22, (viewCount * 0.04) + (audioCount * 0.05));
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
            Order = order,
            Title = localizedTitle,
            DistanceText = LocalizeDistanceText(distanceText),
            IsCompleted = isCompleted
        };
    }

    private UserProfileCard ResolveUserProfile(IReadOnlyList<CustomerUserDto> customerUsers)
    {
        var customer = customerUsers
            .Where(item => item.IsActive && !item.IsBanned)
            .OrderByDescending(item => item.LastActiveAt ?? item.CreatedAt)
            .ThenByDescending(item => item.TotalScans)
            .FirstOrDefault()
            ?? customerUsers
                .Where(item => !item.IsBanned)
                .OrderByDescending(item => item.LastActiveAt ?? item.CreatedAt)
                .FirstOrDefault()
            ?? customerUsers.FirstOrDefault();

        if (customer is null)
        {
            return new UserProfileCard
            {
                FullName = "Nguyễn Bảo Vy",
                Email = "baovy@gmail.com",
                Phone = "0911 000 111",
                AvatarInitials = "BV",
                MetaLine = $"ID customer-1 • android • {_languageService.CurrentLanguage}"
            };
        }

        return new UserProfileCard
        {
            FullName = FirstNonEmpty(customer.Name, customer.Username, customer.Email, customer.Id),
            Email = customer.Email,
            Phone = customer.Phone,
            AvatarInitials = BuildInitials(customer.Name, customer.Username, customer.Email),
            MetaLine = $"ID {customer.Id} • {customer.DeviceType} • {_languageService.CurrentLanguage}"
        };
    }

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

    private static TranslationDto? SelectTranslation(
        PoiDto poi,
        IReadOnlyList<TranslationDto>? translations,
        string currentLanguage)
    {
        if (translations is null || translations.Count == 0)
        {
            return null;
        }

        return translations.FirstOrDefault(item =>
                   !string.IsNullOrWhiteSpace(item.Title) &&
                   string.Equals(item.LanguageCode, currentLanguage, StringComparison.OrdinalIgnoreCase))
               ?? translations.FirstOrDefault(item =>
                   !string.IsNullOrWhiteSpace(item.Title) &&
                   string.Equals(item.LanguageCode, poi.DefaultLanguageCode, StringComparison.OrdinalIgnoreCase))
               ?? translations.FirstOrDefault(item =>
                   !string.IsNullOrWhiteSpace(item.Title) &&
                   string.Equals(item.LanguageCode, "vi", StringComparison.OrdinalIgnoreCase))
               ?? translations.FirstOrDefault(item => !string.IsNullOrWhiteSpace(item.Title))
               ?? translations[0];
    }

    private static string ResolveApiBaseUrl(MobileRuntimeAppSettings runtimeSettings)
    {
        var platformKey = DeviceInfo.Current.Platform.ToString();
        if (runtimeSettings.PlatformApiBaseUrls.TryGetValue(platformKey, out var platformApiBaseUrl) &&
            !string.IsNullOrWhiteSpace(platformApiBaseUrl))
        {
            return platformApiBaseUrl;
        }

        return runtimeSettings.ApiBaseUrl ?? string.Empty;
    }

    private static string EnsureTrailingSlash(string baseUrl)
        => string.IsNullOrWhiteSpace(baseUrl)
            ? string.Empty
            : baseUrl.EndsWith("/", StringComparison.Ordinal) ? baseUrl : $"{baseUrl}/";

    private static string GetCategoryName(IReadOnlyDictionary<string, string> categoriesById, string categoryId)
        => categoriesById.TryGetValue(categoryId, out var categoryName) && !string.IsNullOrWhiteSpace(categoryName)
            ? categoryName
            : "Địa điểm ẩm thực";

    private static string CreateTitleFromSlug(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "Điểm đến Vĩnh Khánh";
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

    private sealed record BootstrapSnapshot(
        IReadOnlyList<PoiLocation> Pois,
        IReadOnlyList<MapHeatPoint> HeatPoints,
        string BackdropImageUrl,
        UserProfileCard UserProfile,
        IReadOnlyDictionary<string, PoiExperienceDetail> PoiDetails,
        IReadOnlyList<LanguageOption> SupportedLanguages,
        IReadOnlyList<RouteSnapshot> Routes);

    private sealed record RouteSnapshot(
        string Id,
        string Name,
        string Theme,
        string Description,
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
        public List<ReviewDto> Reviews { get; set; } = [];
        public List<ViewLogDto> ViewLogs { get; set; } = [];
        public List<AudioListenLogDto> AudioListenLogs { get; set; } = [];
        public SystemSettingDto? Settings { get; set; }
        public DataSyncStateDto? SyncState { get; set; }
    }

    private sealed class CustomerUserDto
    {
        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public string Phone { get; set; } = string.Empty;
        public string PreferredLanguage { get; set; } = "vi";
        public string? Username { get; set; }
        public string DeviceType { get; set; } = "android";
        public bool IsActive { get; set; }
        public bool IsBanned { get; set; }
        public int TotalScans { get; set; }
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
        public string Address { get; set; } = string.Empty;
        public double Lat { get; set; }
        public double Lng { get; set; }
        public string CategoryId { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public bool Featured { get; set; }
        public string DefaultLanguageCode { get; set; } = "vi";
        public string PriceRange { get; set; } = string.Empty;
        public int AverageVisitDuration { get; set; }
        public int PopularityScore { get; set; }
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
    }

    private sealed class RouteDto
    {
        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string Theme { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public int DurationMinutes { get; set; }
        public List<string> StopPoiIds { get; set; } = [];
        public bool IsActive { get; set; }
        public DateTimeOffset UpdatedAt { get; set; }
    }

    private sealed class AudioGuideDto
    {
        public string EntityType { get; set; } = string.Empty;
        public string EntityId { get; set; } = string.Empty;
        public string LanguageCode { get; set; } = string.Empty;
        public string AudioUrl { get; set; } = string.Empty;
        public string VoiceType { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public DateTimeOffset UpdatedAt { get; set; }
    }

    private sealed class ReviewDto
    {
        public string PoiId { get; set; } = string.Empty;
        public int Rating { get; set; }
        public string Comment { get; set; } = string.Empty;
        public string LanguageCode { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public DateTimeOffset CreatedAt { get; set; }
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
        public List<string> FreeLanguages { get; set; } = [];
        public List<string> PremiumLanguages { get; set; } = [];
    }

    private sealed class DataSyncStateDto
    {
        public string Version { get; set; } = string.Empty;
        public DateTimeOffset GeneratedAt { get; set; }
        public DateTimeOffset LastChangedAt { get; set; }
    }
}
