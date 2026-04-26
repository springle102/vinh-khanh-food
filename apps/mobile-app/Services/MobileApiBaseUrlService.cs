using System.Text.Json;
using VinhKhanh.MobileApp.Helpers;

namespace VinhKhanh.MobileApp.Services;

public interface IMobileApiBaseUrlService
{
    Task<string?> GetBaseUrlAsync(CancellationToken cancellationToken = default);
}

public sealed class MobileApiBaseUrlService : IMobileApiBaseUrlService
{
    private const string AppSettingsFileName = "appsettings.json";

    private readonly JsonSerializerOptions _jsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

    private RuntimeAppSettings? _runtimeSettings;

    public async Task<string?> GetBaseUrlAsync(CancellationToken cancellationToken = default)
    {
        var runtimeSettings = await LoadRuntimeSettingsAsync(cancellationToken);
        var resolvedBaseUrl = MobileApiEndpointHelper.ResolveBaseUrl(
            runtimeSettings.ApiBaseUrl,
            runtimeSettings.PlatformApiBaseUrls);

        return string.IsNullOrWhiteSpace(resolvedBaseUrl)
            ? null
            : resolvedBaseUrl.Trim();
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
    private sealed class RuntimeAppSettings
    {
        public string? ApiBaseUrl { get; set; }
        public Dictionary<string, string> PlatformApiBaseUrls { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    }
}
