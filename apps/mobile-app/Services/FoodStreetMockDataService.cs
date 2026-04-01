using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Maui.Devices;
using Microsoft.Maui.Storage;
using VinhKhanh.MobileApp.Models;

namespace VinhKhanh.MobileApp.Services;

public interface IFoodStreetDataService
{
    Task<IReadOnlyList<LanguageOption>> GetLanguagesAsync();
    Task<IReadOnlyList<PoiLocation>> GetPoisAsync();
    Task<IReadOnlyList<MapHeatPoint>> GetHeatPointsAsync();
    Task<TourPlan> GetTourPlanAsync();
    Task<UserProfileCard> GetUserProfileAsync();
    Task<IReadOnlyList<SettingsMenuItem>> GetSettingsMenuAsync();
    string GetBackdropImageUrl();
}

public sealed partial class FoodStreetMockDataService : IFoodStreetDataService
{
    private const string AppSettingsFileName = "appsettings.json";
    private const string BootstrapEndpoint = "api/v1/bootstrap";
    private const string DefaultBackdropImageUrl = "https://images.unsplash.com/photo-1520201163981-8cc95007dd2e?auto=format&fit=crop&w=1200&q=80";

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
    private BootstrapSnapshot? _bootstrapSnapshot;
    private MobileRuntimeAppSettings? _runtimeSettings;
    private HttpClient? _httpClient;
    private string? _resolvedBaseUrl;

    public FoodStreetMockDataService(IAppLanguageService languageService)
    {
        _languageService = languageService;
        _languageService.LanguageChanged += (_, _) => _bootstrapSnapshot = null;
    }

    public Task<IReadOnlyList<LanguageOption>> GetLanguagesAsync()
        => Task.FromResult<IReadOnlyList<LanguageOption>>(Languages.Select(language => new LanguageOption
        {
            Code = language.Code,
            Flag = language.Flag,
            DisplayName = language.DisplayName,
            IsSelected = string.Equals(language.Code, _languageService.CurrentLanguage, StringComparison.OrdinalIgnoreCase)
        }).ToList());

    public async Task<IReadOnlyList<PoiLocation>> GetPoisAsync()
    {
        var snapshot = await GetBootstrapSnapshotAsync();
        return snapshot?.Pois.Count > 0 ? snapshot.Pois : FallbackPois;
    }

    public async Task<IReadOnlyList<MapHeatPoint>> GetHeatPointsAsync()
    {
        var snapshot = await GetBootstrapSnapshotAsync();
        return snapshot?.HeatPoints.Count > 0 ? snapshot.HeatPoints : FallbackHeatPoints;
    }

    public async Task<TourPlan> GetTourPlanAsync()
    {
        var poiLookup = (await GetPoisAsync()).ToDictionary(item => item.Id, StringComparer.OrdinalIgnoreCase);
        return new TourPlan
        {
            Title = "Hành Trình Ăn Vặt",
            Theme = "Hành Trình Ăn Vặt",
            Description = "Tour ngắn ưu tiên các POI đang được quản lý trong admin và fallback mock khi backend chưa bật.",
            ProgressValue = 0.66,
            ProgressText = "2 / 3 điểm đã đi",
            SummaryText = "Lộ trình nhẹ, nhiều món signature và kết thúc bằng món ngọt.",
            Stops =
            [
                CreateTourStop(poiLookup, "poi-snail-signature", 1),
                CreateTourStop(poiLookup, "poi-bbq-night", 2),
                CreateTourStop(poiLookup, "poi-sweet-lane", 3)
            ],
            Checkpoints =
            [
                CreateCheckpoint(poiLookup, "poi-snail-signature", 1, "150 m", true),
                CreateCheckpoint(poiLookup, "poi-bbq-night", 2, "2,50 km", true),
                CreateCheckpoint(poiLookup, "poi-sweet-lane", 3, "2,70 km", false)
            ]
        };
    }

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
            MetaLine = "ID customer-1 • android • vi"
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
        if (_bootstrapSnapshot is not null)
        {
            return _bootstrapSnapshot;
        }

        await _bootstrapLock.WaitAsync();
        try
        {
            if (_bootstrapSnapshot is not null)
            {
                return _bootstrapSnapshot;
            }

            var client = await GetClientAsync();
            if (client is null)
            {
                return null;
            }

            var response = await client.GetAsync(BootstrapEndpoint);
            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            var envelope = await response.Content.ReadFromJsonAsync<ApiEnvelope<AdminBootstrapDto>>(JsonOptions);
            if (envelope?.Success != true || envelope.Data is null)
            {
                return null;
            }

            var snapshot = CreateSnapshot(envelope.Data);
            _bootstrapSnapshot = snapshot.Pois.Count > 0 ? snapshot : null;
            return _bootstrapSnapshot;
        }
        catch
        {
            return null;
        }
        finally
        {
            _bootstrapLock.Release();
        }
    }

    private async Task<HttpClient?> GetClientAsync()
    {
        var runtimeSettings = await LoadRuntimeSettingsAsync();
        var nextBaseUrl = EnsureTrailingSlash(ResolveApiBaseUrl(runtimeSettings));
        if (string.IsNullOrWhiteSpace(nextBaseUrl))
        {
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

    private BootstrapSnapshot CreateSnapshot(AdminBootstrapDto bootstrap)
    {
        var categoriesById = bootstrap.Categories
            .Where(item => !string.IsNullOrWhiteSpace(item.Id))
            .ToDictionary(item => item.Id, item => item.Name, StringComparer.OrdinalIgnoreCase);

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
            .Where(item => IsValidCoordinate(item.Lat) && IsValidCoordinate(item.Lng))
            .OrderByDescending(item => item.Featured)
            .ThenByDescending(item => item.PopularityScore)
            .ThenBy(item => item.Slug, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var poiLocations = orderedPois.Select(poi =>
        {
            translationsByPoiId.TryGetValue(poi.Id, out var poiTranslations);
            var translation = SelectTranslation(poi, poiTranslations, _languageService.CurrentLanguage);
            var title = FirstNonEmpty(translation?.Title, CreateTitleFromSlug(poi.Slug), poi.Id);

            return new PoiLocation
            {
                Id = poi.Id,
                Title = title,
                ShortDescription = FirstNonEmpty(translation?.ShortText, translation?.FullText, $"{title} • {GetCategoryName(categoriesById, poi.CategoryId)}"),
                Address = poi.Address,
                Category = GetCategoryName(categoriesById, poi.CategoryId),
                PriceRange = poi.PriceRange,
                ThumbnailUrl = ResolvePoiImageUrl(poi.Id, poiImages, foodImages),
                Latitude = poi.Lat,
                Longitude = poi.Lng,
                IsFeatured = poi.Featured,
                HeatIntensity = ResolveHeatIntensity(poi, bootstrap.ViewLogs, bootstrap.AudioListenLogs),
                DistanceText = $"{Math.Max(10, poi.AverageVisitDuration)} phút"
            };
        }).ToList();

        return new BootstrapSnapshot(
            poiLocations,
            BuildHeatPoints(orderedPois, bootstrap.ViewLogs, bootstrap.AudioListenLogs),
            ResolveBackdropImageUrl(poiLocations, poiImages),
            ResolveUserProfile(bootstrap.CustomerUsers));
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

    private static TourStop CreateTourStop(IReadOnlyDictionary<string, PoiLocation> poiLookup, string poiId, int order)
    {
        if (poiLookup.TryGetValue(poiId, out var poi))
        {
            return new TourStop
            {
                PoiId = poiId,
                Title = $"{order}. {poi.Title}",
                Description = $"Tạo Tour - {poi.DistanceText}",
                ThumbnailUrl = poi.ThumbnailUrl,
                DistanceText = poi.DistanceText
            };
        }

        return new TourStop
        {
            PoiId = poiId,
            Title = $"{order}. Điểm dừng",
            Description = "Tạo Tour - 100 m",
            ThumbnailUrl = DefaultBackdropImageUrl,
            DistanceText = "100 m"
        };
    }

    private static TourCheckpoint CreateCheckpoint(
        IReadOnlyDictionary<string, PoiLocation> poiLookup,
        string poiId,
        int order,
        string distanceText,
        bool isCompleted)
    {
        var title = poiLookup.TryGetValue(poiId, out var poi) ? poi.Title : $"Điểm dừng {order}";
        return new TourCheckpoint
        {
            Order = order,
            Title = title,
            DistanceText = distanceText,
            IsCompleted = isCompleted
        };
    }

    private static UserProfileCard ResolveUserProfile(IReadOnlyList<CustomerUserDto> customerUsers)
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
                MetaLine = "ID customer-1 • android • vi"
            };
        }

        return new UserProfileCard
        {
            FullName = FirstNonEmpty(customer.Name, customer.Username, customer.Email, customer.Id),
            Email = customer.Email,
            Phone = customer.Phone,
            AvatarInitials = BuildInitials(customer.Name, customer.Username, customer.Email),
            MetaLine = $"ID {customer.Id} • {customer.DeviceType} • {customer.PreferredLanguage}"
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
        UserProfileCard UserProfile);

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
        public List<MediaAssetDto> MediaAssets { get; set; } = [];
        public List<FoodItemDto> FoodItems { get; set; } = [];
        public List<ViewLogDto> ViewLogs { get; set; } = [];
        public List<AudioListenLogDto> AudioListenLogs { get; set; } = [];
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
        public DateTimeOffset CreatedAt { get; set; }
    }

    private sealed class FoodItemDto
    {
        public string PoiId { get; set; } = string.Empty;
        public string ImageUrl { get; set; } = string.Empty;
    }

    private sealed class ViewLogDto
    {
        public string PoiId { get; set; } = string.Empty;
    }

    private sealed class AudioListenLogDto
    {
        public string PoiId { get; set; } = string.Empty;
    }
}
