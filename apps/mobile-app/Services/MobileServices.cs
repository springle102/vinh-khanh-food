using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Plugin.Maui.Audio;
using VinhKhanh.MobileApp.Helpers;
using VinhKhanh.MobileApp.Models;

namespace VinhKhanh.MobileApp.Interfaces
{
    public interface IAppSettingsService
    {
        Task<UserSettings> GetAsync();
        Task SaveAsync(UserSettings settings);
        Task SetLanguageAsync(string languageCode);
        Task SetVoiceAsync(VoiceOption voiceOption);
    }

    public interface ILocalizationService
    {
        string CurrentLanguage { get; }
        event EventHandler? LanguageChanged;
        Task InitializeAsync();
        Task SetLanguageAsync(string languageCode);
        string GetText(string key);
    }

    public interface IOfflineCacheService
    {
        Task SaveAsync<T>(string key, T data);
        Task<T?> LoadAsync<T>(string key);
    }

    public interface IGuideApiService
    {
        Task<MobileSettingsModel> GetMobileSettingsAsync();
        Task<IReadOnlyList<PoiSummaryModel>> GetPoisAsync(string languageCode, string? search = null);
        Task<IReadOnlyList<PoiSummaryModel>> GetNearbyAsync(double latitude, double longitude, double radiusMeters, string languageCode);
        Task<IReadOnlyList<TourRouteModel>> GetRoutesAsync(string languageCode);
        Task<PoiDetailModel?> GetPoiByIdAsync(string poiId, string languageCode);
        Task<PoiDetailModel?> GetPoiBySlugAsync(string slug, string languageCode);
        Task TrackViewAsync(string poiId, TrackViewRequest request);
        Task TrackAudioAsync(string poiId, TrackAudioRequest request);
    }

    public interface INarrationService
    {
        bool IsPlaying { get; }
        Task PlayAsync(PoiDetailModel detail, string languageCode);
        Task PauseAsync();
        Task StopAsync();
    }

    public interface ILocationTrackerService
    {
        event EventHandler<LocationChangedMessage>? LocationChanged;
        Task<bool> EnsurePermissionAsync();
        Task StartAsync(IEnumerable<PoiSummaryModel> pois, UserSettings settings, Func<PoiSummaryModel, Task> onPoiReached);
        Task StopAsync();
        Task<Location?> GetCurrentLocationAsync();
    }
}

namespace VinhKhanh.MobileApp.Services
{
    using VinhKhanh.MobileApp.Interfaces;

    internal sealed class MobileRuntimeAppSettings
    {
        public string? ApiBaseUrl { get; set; }
        public Dictionary<string, string> PlatformApiBaseUrls { get; set; } = new(StringComparer.OrdinalIgnoreCase);
        public string? RoutingBaseUrl { get; set; }
        public string? RoutingProfile { get; set; }
    }

    public sealed class AppSettingsService : IAppSettingsService
    {
        private const string SettingsKey = "user_settings";
        private const string AppSettingsFileName = "appsettings.json";
        private readonly JsonSerializerOptions _serializerOptions = new() { PropertyNameCaseInsensitive = true };
        private MobileRuntimeAppSettings? _runtimeSettings;

        public async Task<UserSettings> GetAsync()
        {
            var runtimeSettings = await LoadRuntimeSettingsAsync();
            var resolvedApiBaseUrl = ResolveApiBaseUrl(runtimeSettings);
            var json = Preferences.Default.Get(SettingsKey, string.Empty);
            var settings = string.IsNullOrWhiteSpace(json)
                ? new UserSettings()
                : JsonSerializer.Deserialize<UserSettings>(json, _serializerOptions) ?? new UserSettings();

            if (!string.IsNullOrWhiteSpace(resolvedApiBaseUrl) &&
                string.IsNullOrWhiteSpace(settings.ApiBaseUrl))
            {
                settings.ApiBaseUrl = resolvedApiBaseUrl;
            }

            return settings;
        }

        public Task SaveAsync(UserSettings settings)
        {
            Preferences.Default.Set(SettingsKey, JsonSerializer.Serialize(settings, _serializerOptions));
            return Task.CompletedTask;
        }

        public async Task SetLanguageAsync(string languageCode)
        {
            var settings = await GetAsync();
            settings.SelectedLanguage = languageCode;
            await SaveAsync(settings);
        }

        public async Task SetVoiceAsync(VoiceOption voiceOption)
        {
            var settings = await GetAsync();
            settings.SelectedVoiceId = voiceOption.Id;
            settings.SelectedVoiceLocale = voiceOption.Locale;
            await SaveAsync(settings);
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
                _runtimeSettings = JsonSerializer.Deserialize<MobileRuntimeAppSettings>(content, _serializerOptions) ?? new MobileRuntimeAppSettings();
            }
            catch
            {
                _runtimeSettings = new MobileRuntimeAppSettings();
            }

            return _runtimeSettings;
        }

        private static string ResolveApiBaseUrl(MobileRuntimeAppSettings runtimeSettings)
            => MobileApiEndpointHelper.ResolveBaseUrl(runtimeSettings.ApiBaseUrl, runtimeSettings.PlatformApiBaseUrls);

    }

    public sealed class LocalizationService(IAppSettingsService settingsService, ILogger<LocalizationService> logger) : ILocalizationService
    {
        private readonly JsonSerializerOptions _serializerOptions = new() { PropertyNameCaseInsensitive = true };
        private Dictionary<string, string> _dictionary = new(StringComparer.OrdinalIgnoreCase);

        public string CurrentLanguage { get; private set; } = "vi";

        public event EventHandler? LanguageChanged;

        public async Task InitializeAsync()
        {
            var settings = await settingsService.GetAsync();
            await SetLanguageAsync(settings.SelectedLanguage);
        }

        public async Task SetLanguageAsync(string languageCode)
        {
            var fileName = $"Localization/{languageCode}.json";
            try
            {
                await using var stream = await FileSystem.OpenAppPackageFileAsync(fileName);
                using var reader = new StreamReader(stream);
                var content = await reader.ReadToEndAsync();
                _dictionary = JsonSerializer.Deserialize<Dictionary<string, string>>(content, _serializerOptions)
                    ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                CurrentLanguage = languageCode;
                LanguageChanged?.Invoke(this, EventArgs.Empty);
            }
            catch (Exception exception)
            {
                logger.LogWarning(exception, "Không tải được gói localization {LanguageCode}", languageCode);
            }
        }

        public string GetText(string key) => _dictionary.TryGetValue(key, out var value) ? value : key;
    }

    // ✅ FIX: Adapter to make IAppLanguageService compatible with ILocalizationService
    // This allows existing code using ILocalizationService to work with the unified AppLanguageService
    public sealed class LocalizationServiceAdapter(IAppLanguageService appLanguageService) : ILocalizationService
    {
        public string CurrentLanguage => appLanguageService.CurrentLanguage;

        public event EventHandler? LanguageChanged
        {
            add => appLanguageService.LanguageChanged += value;
            remove => appLanguageService.LanguageChanged -= value;
        }

        public async Task InitializeAsync()
        {
            await appLanguageService.InitializeAsync();
        }

        public async Task SetLanguageAsync(string languageCode)
        {
            await appLanguageService.SetLanguageAsync(languageCode);
        }

        public string GetText(string key) => appLanguageService.GetText(key);
    }

    public sealed class OfflineCacheService : IOfflineCacheService
    {
        private readonly JsonSerializerOptions _serializerOptions = new() { PropertyNameCaseInsensitive = true };

        public async Task SaveAsync<T>(string key, T data)
        {
            var filePath = BuildPath(key);
            Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);
            await File.WriteAllTextAsync(filePath, JsonSerializer.Serialize(data, _serializerOptions));
        }

        public async Task<T?> LoadAsync<T>(string key)
        {
            var filePath = BuildPath(key);
            if (!File.Exists(filePath))
            {
                return default;
            }

            var content = await File.ReadAllTextAsync(filePath);
            return JsonSerializer.Deserialize<T>(content, _serializerOptions);
        }

        private static string BuildPath(string key) => Path.Combine(FileSystem.AppDataDirectory, "cache", $"{key}.json");
    }

    public sealed class GuideApiService(
        IAppSettingsService settingsService,
        IOfflineCacheService cacheService,
        ILogger<GuideApiService> logger) : IGuideApiService
    {
        private readonly JsonSerializerOptions _serializerOptions = new() { PropertyNameCaseInsensitive = true };
        private HttpClient? _httpClient;
        private string? _resolvedBaseUrl;

        public async Task<MobileSettingsModel> GetMobileSettingsAsync()
        {
            return await ExecuteWithFallbackAsync(
                "mobile-settings",
                async () =>
                {
                    var result = await SendAsync<MobileSettingsModel>("api/guide/v1/settings/mobile");
                    await cacheService.SaveAsync("mobile-settings", result);
                    return result;
                },
                () => cacheService.LoadAsync<MobileSettingsModel>("mobile-settings"),
                new MobileSettingsModel());
        }

        public async Task<IReadOnlyList<PoiSummaryModel>> GetPoisAsync(string languageCode, string? search = null)
        {
            var cacheKey = $"pois-{languageCode}-{search}";
            return await ExecuteWithFallbackAsync<IReadOnlyList<PoiSummaryModel>>(
                cacheKey,
                async () =>
                {
                    var result = await SendAsync<PagedResultModel<PoiSummaryModel>>(
                        $"api/guide/v1/pois?language={Uri.EscapeDataString(languageCode)}&search={Uri.EscapeDataString(search ?? string.Empty)}");
                    await cacheService.SaveAsync(cacheKey, result.Items);
                    return (IReadOnlyList<PoiSummaryModel>)result.Items;
                },
                async () => (IReadOnlyList<PoiSummaryModel>?)await cacheService.LoadAsync<List<PoiSummaryModel>>(cacheKey),
                new List<PoiSummaryModel>());
        }

        public async Task<IReadOnlyList<PoiSummaryModel>> GetNearbyAsync(double latitude, double longitude, double radiusMeters, string languageCode)
        {
            return await SendAsync<List<PoiSummaryModel>>(
                $"api/guide/v1/pois/nearby?lat={latitude}&lng={longitude}&radiusMeters={radiusMeters}&language={Uri.EscapeDataString(languageCode)}");
        }

        public async Task<IReadOnlyList<TourRouteModel>> GetRoutesAsync(string languageCode)
        {
            return await ExecuteWithFallbackAsync<IReadOnlyList<TourRouteModel>>(
                $"routes-{languageCode}",
                async () =>
                {
                    var result = await SendAsync<List<TourRouteModel>>($"api/guide/v1/pois/routes?language={Uri.EscapeDataString(languageCode)}");
                    await cacheService.SaveAsync($"routes-{languageCode}", result);
                    return (IReadOnlyList<TourRouteModel>)result;
                },
                async () => (IReadOnlyList<TourRouteModel>?)await cacheService.LoadAsync<List<TourRouteModel>>($"routes-{languageCode}"),
                new List<TourRouteModel>());
        }

        public Task<PoiDetailModel?> GetPoiByIdAsync(string poiId, string languageCode)
            => GetPoiWithFallbackAsync($"poi-id-{poiId}-{languageCode}", $"api/guide/v1/pois/{poiId}?language={Uri.EscapeDataString(languageCode)}");

        public Task<PoiDetailModel?> GetPoiBySlugAsync(string slug, string languageCode)
            => GetPoiWithFallbackAsync($"poi-slug-{slug}-{languageCode}", $"api/guide/v1/pois/slug/{slug}?language={Uri.EscapeDataString(languageCode)}");

        public Task TrackViewAsync(string poiId, TrackViewRequest request)
            => PostAsync($"api/guide/v1/pois/{poiId}/events/view", request);

        public Task TrackAudioAsync(string poiId, TrackAudioRequest request)
            => PostAsync($"api/guide/v1/pois/{poiId}/events/audio", request);

        private async Task<PoiDetailModel?> GetPoiWithFallbackAsync(string cacheKey, string url)
        {
            return await ExecuteWithFallbackAsync(
                cacheKey,
                async () =>
                {
                    var result = await SendAsync<PoiDetailModel>(url);
                    await cacheService.SaveAsync(cacheKey, result);
                    return result;
                },
                () => cacheService.LoadAsync<PoiDetailModel>(cacheKey),
                null);
        }

        private async Task<T> SendAsync<T>(string relativeUrl)
        {
            var client = await GetClientAsync();
            var envelope = await client.GetFromJsonAsync<ApiEnvelope<T>>(relativeUrl, _serializerOptions);
            if (envelope?.Success != true || envelope.Data is null)
            {
                throw new InvalidOperationException(envelope?.Message ?? "API không trả về dữ liệu hợp lệ.");
            }

            return envelope.Data;
        }

        private async Task PostAsync<T>(string relativeUrl, T payload)
        {
            var client = await GetClientAsync();
            var response = await client.PostAsJsonAsync(relativeUrl, payload, _serializerOptions);
            response.EnsureSuccessStatusCode();
        }

        private async Task<HttpClient> GetClientAsync()
        {
            var settings = await settingsService.GetAsync();
            var nextBaseUrl = EnsureTrailingSlash(settings.ApiBaseUrl);

            if (_httpClient is not null && string.Equals(_resolvedBaseUrl, nextBaseUrl, StringComparison.OrdinalIgnoreCase))
            {
                return _httpClient;
            }

            _httpClient?.Dispose();
            _httpClient = new HttpClient()
            {
                BaseAddress = new Uri(nextBaseUrl)
            };
            _resolvedBaseUrl = nextBaseUrl;

            return _httpClient;
        }

        private static string EnsureTrailingSlash(string baseUrl) => baseUrl.EndsWith('/') ? baseUrl : $"{baseUrl}/";

        private async Task<T> ExecuteWithFallbackAsync<T>(string key, Func<Task<T>> onlineAction, Func<Task<T?>> offlineAction, T defaultValue)
        {
            try
            {
                return await RetryAsync(onlineAction);
            }
            catch (Exception exception)
            {
                logger.LogWarning(exception, "Không gọi được API {CacheKey}, sử dụng cache offline nếu có.", key);
                return await offlineAction() ?? defaultValue;
            }
        }

        private static async Task<T> RetryAsync<T>(Func<Task<T>> action)
        {
            Exception? lastException = null;
            foreach (var delay in new[] { 0, 500, 1200 })
            {
                if (delay > 0)
                {
                    await Task.Delay(delay);
                }

                try
                {
                    return await action();
                }
                catch (Exception exception)
                {
                    lastException = exception;
                }
            }

            throw lastException ?? new InvalidOperationException("API retry failed.");
        }
    }

    public sealed class NarrationService(
        IAudioManager audioManager,
        IAppSettingsService settingsService,
        IGuideApiService guideApiService,
        ILogger<NarrationService> logger) : INarrationService
    {
        private IAudioPlayer? _player;
        private Stream? _audioStream;
        private readonly HttpClient _audioClient = new();

        public bool IsPlaying => _player?.IsPlaying ?? false;

        public async Task PlayAsync(PoiDetailModel detail, string languageCode)
        {
            var narration = detail.Narrations.FirstOrDefault(item => item.LanguageCode == languageCode)
                ?? detail.Narrations.FirstOrDefault(item => item.LanguageCode == "en")
                ?? detail.Narrations.FirstOrDefault();

            if (narration is null)
            {
                return;
            }

            if (string.IsNullOrWhiteSpace(narration.AudioUrl))
            {
                logger.LogWarning(
                    "No pre-generated narration audio is available for POI {PoiId} and language {LanguageCode}. Runtime TTS is disabled.",
                    detail.Id,
                    languageCode);
                return;
            }

            await StopAsync();
            _audioStream = await _audioClient.GetStreamAsync(narration.AudioUrl);
            _player = audioManager.CreatePlayer(_audioStream);
            _player.Play();

            await guideApiService.TrackAudioAsync(detail.Id, new TrackAudioRequest
            {
                LanguageCode = languageCode,
                DurationInSeconds = 30
            });

            logger.LogInformation("Đang phát narration cho POI {PoiId} bằng ngôn ngữ {LanguageCode}", detail.Id, languageCode);
        }

        public Task PauseAsync()
        {
            _player?.Pause();
            return Task.CompletedTask;
        }

        public Task StopAsync()
        {
            _player?.Stop();
            _player?.Dispose();
            _player = null;
            _audioStream?.Dispose();
            _audioStream = null;
            return Task.CompletedTask;
        }
    }

    public sealed class LocationTrackerService(ILogger<LocationTrackerService> logger) : ILocationTrackerService
    {
        private CancellationTokenSource? _loopCancellation;
        private readonly ConcurrentDictionary<string, DateTimeOffset> _cooldown = new();

        public event EventHandler<LocationChangedMessage>? LocationChanged;

        public async Task<bool> EnsurePermissionAsync()
        {
            var status = await Permissions.CheckStatusAsync<Permissions.LocationWhenInUse>();
            if (status != PermissionStatus.Granted)
            {
                status = await Permissions.RequestAsync<Permissions.LocationWhenInUse>();
            }

            return status == PermissionStatus.Granted;
        }

        public async Task<Location?> GetCurrentLocationAsync()
        {
            if (!await EnsurePermissionAsync())
            {
                return null;
            }

            return await Geolocation.Default.GetLocationAsync(new GeolocationRequest(GeolocationAccuracy.Best, TimeSpan.FromSeconds(10)));
        }

        public async Task StartAsync(IEnumerable<PoiSummaryModel> pois, UserSettings settings, Func<PoiSummaryModel, Task> onPoiReached)
        {
            await StopAsync();
            if (!await EnsurePermissionAsync())
            {
                return;
            }

            _loopCancellation = new CancellationTokenSource();
            _ = Task.Run(async () =>
            {
                using var timer = new PeriodicTimer(TimeSpan.FromSeconds(8));
                while (await timer.WaitForNextTickAsync(_loopCancellation.Token))
                {
                    var location = await GetCurrentLocationAsync();
                    if (location is null)
                    {
                        continue;
                    }

                    LocationChanged?.Invoke(this, new LocationChangedMessage
                    {
                        Latitude = location.Latitude,
                        Longitude = location.Longitude
                    });

                    foreach (var poi in pois)
                    {
                        var distance = GeoFenceHelper.CalculateDistanceMeters(
                            location.Latitude,
                            location.Longitude,
                            poi.Latitude,
                            poi.Longitude);

                        if (distance > settings.GeofenceRadiusMeters)
                        {
                            continue;
                        }

                        if (_cooldown.TryGetValue(poi.Id, out var lastTriggered) &&
                            DateTimeOffset.UtcNow - lastTriggered < TimeSpan.FromMinutes(15))
                        {
                            continue;
                        }

                        _cooldown[poi.Id] = DateTimeOffset.UtcNow;
                        await onPoiReached(poi);
                    }
                }
            }, _loopCancellation.Token);

            logger.LogInformation("Da bat location tracking cho {PoiCount} POI.", pois.Count());
        }

        public Task StopAsync()
        {
            _loopCancellation?.Cancel();
            _loopCancellation = null;
            return Task.CompletedTask;
        }
    }
}





