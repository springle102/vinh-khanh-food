using System.Text.Json;
using Microsoft.Maui.Storage;
using VinhKhanh.MobileApp.Helpers;

namespace VinhKhanh.MobileApp.Services;

public interface IMobileApiBaseUrlService
{
    Task<string?> GetBaseUrlAsync(CancellationToken cancellationToken = default);
}

public sealed class MobileApiBaseUrlService : IMobileApiBaseUrlService
{
    private const string AppSettingsFileName = "appsettings.json";
    private const string UserSettingsPreferenceKey = "user_settings";

    private readonly JsonSerializerOptions _jsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

    private RuntimeAppSettings? _runtimeSettings;

    public async Task<string?> GetBaseUrlAsync(CancellationToken cancellationToken = default)
    {
        var runtimeSettings = await LoadRuntimeSettingsAsync(cancellationToken);
        var runtimeBaseUrl = MobileApiEndpointHelper.ResolveBaseUrl(
            runtimeSettings.ApiBaseUrl,
            runtimeSettings.PlatformApiBaseUrls);

        var persistedBaseUrl = LoadPersistedBaseUrl();
        var preferredBaseUrl =
            string.IsNullOrWhiteSpace(persistedBaseUrl) || IsLegacyDefaultApiBaseUrl(persistedBaseUrl)
                ? runtimeBaseUrl
                : MobileApiEndpointHelper.ResolveBaseUrl(persistedBaseUrl, null);

        return string.IsNullOrWhiteSpace(preferredBaseUrl)
            ? null
            : preferredBaseUrl.Trim();
    }

    private string? LoadPersistedBaseUrl()
    {
        try
        {
            var raw = Preferences.Default.Get(UserSettingsPreferenceKey, string.Empty);
            if (string.IsNullOrWhiteSpace(raw))
            {
                return null;
            }

            var settings = JsonSerializer.Deserialize<PersistedUserSettings>(raw, _jsonOptions);
            return string.IsNullOrWhiteSpace(settings?.ApiBaseUrl)
                ? null
                : settings.ApiBaseUrl.Trim();
        }
        catch
        {
            return null;
        }
    }

    private async Task<RuntimeAppSettings> LoadRuntimeSettingsAsync(CancellationToken cancellationToken)
    {
        if (_runtimeSettings is not null)
        {
            return _runtimeSettings;
        }

        try
        {
            await using var stream = await FileSystem.OpenAppPackageFileAsync(AppSettingsFileName);
            using var reader = new StreamReader(stream);
            var content = await reader.ReadToEndAsync(cancellationToken);
            _runtimeSettings = JsonSerializer.Deserialize<RuntimeAppSettings>(content, _jsonOptions)
                               ?? new RuntimeAppSettings();
        }
        catch
        {
            _runtimeSettings = new RuntimeAppSettings();
        }

        return _runtimeSettings;
    }

    private static bool IsLegacyDefaultApiBaseUrl(string apiBaseUrl)
    {
        if (string.IsNullOrWhiteSpace(apiBaseUrl))
        {
            return true;
        }

        return apiBaseUrl.Contains("localhost:7055", StringComparison.OrdinalIgnoreCase)
               || apiBaseUrl.Contains("127.0.0.1:7055", StringComparison.OrdinalIgnoreCase)
               || apiBaseUrl.Contains("localhost:5080", StringComparison.OrdinalIgnoreCase)
               || apiBaseUrl.Contains("127.0.0.1:5080", StringComparison.OrdinalIgnoreCase)
               || apiBaseUrl.Contains("10.0.2.2:7055", StringComparison.OrdinalIgnoreCase)
               || apiBaseUrl.Contains("10.0.2.2:5080", StringComparison.OrdinalIgnoreCase);
    }

    private sealed class RuntimeAppSettings
    {
        public string? ApiBaseUrl { get; set; }
        public Dictionary<string, string> PlatformApiBaseUrls { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    }

    private sealed class PersistedUserSettings
    {
        public string? ApiBaseUrl { get; set; }
    }
}
