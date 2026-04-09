using Microsoft.Maui.Devices;

namespace VinhKhanh.MobileApp.Helpers;

public static class MobileApiEndpointHelper
{
    private const string AndroidEmulatorHost = "10.0.2.2";

    public static string ResolveBaseUrl(string? apiBaseUrl, IReadOnlyDictionary<string, string>? platformApiBaseUrls)
    {
        var platformKey = DeviceInfo.Current.Platform.ToString();
        var virtualPlatformKey = GetVirtualPlatformKey(platformKey);

        if (platformApiBaseUrls is not null &&
            !string.IsNullOrWhiteSpace(virtualPlatformKey) &&
            platformApiBaseUrls.TryGetValue(virtualPlatformKey, out var virtualPlatformApiBaseUrl) &&
            !string.IsNullOrWhiteSpace(virtualPlatformApiBaseUrl))
        {
            return NormalizeForCurrentPlatform(virtualPlatformApiBaseUrl);
        }

        if (platformApiBaseUrls is not null &&
            platformApiBaseUrls.TryGetValue(platformKey, out var platformApiBaseUrl) &&
            !string.IsNullOrWhiteSpace(platformApiBaseUrl))
        {
            return NormalizeForCurrentPlatform(platformApiBaseUrl);
        }

        return NormalizeForCurrentPlatform(apiBaseUrl);
    }

    public static string EnsureTrailingSlash(string? baseUrl)
    {
        var normalized = NormalizeForCurrentPlatform(baseUrl);
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return string.Empty;
        }

        return normalized.EndsWith("/", StringComparison.Ordinal) ? normalized : $"{normalized}/";
    }

    private static string NormalizeForCurrentPlatform(string? baseUrl)
    {
        var trimmed = baseUrl?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(trimmed) ||
            DeviceInfo.Current.Platform != DevicePlatform.Android ||
            DeviceInfo.Current.DeviceType != DeviceType.Virtual ||
            !Uri.TryCreate(trimmed, UriKind.Absolute, out var uri))
        {
            return trimmed;
        }

        if (!string.Equals(uri.Host, "localhost", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(uri.Host, "127.0.0.1", StringComparison.OrdinalIgnoreCase))
        {
            return trimmed.TrimEnd('/');
        }

        var builder = new UriBuilder(uri)
        {
            Host = AndroidEmulatorHost
        };

        return builder.Uri.ToString().TrimEnd('/');
    }

    private static string? GetVirtualPlatformKey(string platformKey)
    {
        return DeviceInfo.Current.Platform == DevicePlatform.Android &&
               DeviceInfo.Current.DeviceType == DeviceType.Virtual
            ? $"{platformKey}Virtual"
            : null;
    }
}
