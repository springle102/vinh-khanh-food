using System.Text.Json;
using Microsoft.Extensions.Logging;
using VinhKhanh.MobileApp.Helpers;

namespace VinhKhanh.MobileApp.Services;

public interface IMobileApiBaseUrlService
{
    Task<string?> GetBaseUrlAsync(CancellationToken cancellationToken = default);
}

public sealed class MobileApiBaseUrlService(ILogger<MobileApiBaseUrlService> logger) : IMobileApiBaseUrlService
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
            ? LogMissingBaseUrl()
            : LogResolvedBaseUrl(resolvedBaseUrl.Trim());
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
        catch (Exception exception)
        {
            logger.LogWarning(exception, "[MobileApiBaseUrl] Unable to load packaged appsettings.json. API calls will be disabled until a valid ApiBaseUrl is configured.");
            _runtimeSettings = new RuntimeAppSettings();
        }

        return _runtimeSettings;
    }

    private string? LogMissingBaseUrl()
    {
        logger.LogWarning("[MobileApiBaseUrl] ApiBaseUrl resolved to empty. Check Resources/Raw/appsettings.json or generated .android-settings/appsettings.json.");
        return null;
    }

    private string LogResolvedBaseUrl(string baseUrl)
    {
        logger.LogInformation("[MobileApiBaseUrl] ApiBaseUrl resolved. baseUrl={BaseUrl}", baseUrl);
        return baseUrl;
    }

    private sealed class RuntimeAppSettings
    {
        public string? ApiBaseUrl { get; set; }
        public Dictionary<string, string> PlatformApiBaseUrls { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    }
}
