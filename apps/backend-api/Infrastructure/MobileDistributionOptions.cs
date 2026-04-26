using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;

namespace VinhKhanh.BackendApi.Infrastructure;

public sealed class MobileDistributionOptions
{
    public const string SectionName = "MobileDistribution";

    public const string DefaultAppDisplayName = "Vinh Khanh Food Guide";
    public const string DefaultDownloadApkPath = "/downloads/vinh-khanh-food-guide/tour.apk";
    public const string LegacyGuideDownloadApkPath = "/downloads/vinh-khanh-food-guide.apk";
    public const string LegacyTourDownloadApkPath = "/downloads/vinh-khanh-food-tour.apk";
    public const string PublicDownloadAppApiPath = "/api/public/download/app";
    public const string PublicDownloadAppApiAliasPath = "/api/downloads/apk";

    public string AppDisplayName { get; set; } = DefaultAppDisplayName;
    public string? PublicBaseUrl { get; set; }
    public string PublicDownloadApkPath { get; set; } = DefaultDownloadApkPath;
    public string? MobileApiBaseUrl { get; set; }

    public static void ApplyConfiguration(MobileDistributionOptions options, IConfiguration configuration)
    {
        var section = configuration.GetSection(SectionName);

        options.AppDisplayName = FirstNonEmpty(
                section["AppDisplayName"],
                options.AppDisplayName,
                DefaultAppDisplayName)!
            .Trim();
        options.PublicBaseUrl = NormalizeBaseUrl(FirstNonEmpty(section["PublicBaseUrl"], options.PublicBaseUrl));
        options.PublicDownloadApkPath = NormalizePublicPath(
            FirstNonEmpty(section["PublicDownloadApkPath"], options.PublicDownloadApkPath),
            DefaultDownloadApkPath);
        options.MobileApiBaseUrl = NormalizeBaseUrl(
            FirstNonEmpty(section["MobileApiBaseUrl"], options.MobileApiBaseUrl));
    }

    public string GetDownloadPageUrl(HttpRequest request)
        => BuildAbsoluteUrl(request, "/app");

    public string GetDownloadApkUrl(HttpRequest request)
        => BuildAbsoluteUrl(request, PublicDownloadApkPath);

    public string GetPublicDownloadAppUrl(HttpRequest request)
        => BuildAbsoluteUrl(request, PublicDownloadAppApiPath);

    public string GetMobileApiBaseUrl(HttpRequest request)
        => !string.IsNullOrWhiteSpace(MobileApiBaseUrl)
            ? MobileApiBaseUrl!
            : GetEffectivePublicBaseUrl(request);

    public string GetApkFileName()
    {
        var fileName = Path.GetFileName(PublicDownloadApkPath);
        return string.IsNullOrWhiteSpace(fileName)
            ? "tour.apk"
            : fileName;
    }

    public string GetApkRelativeFilePath()
        => PublicDownloadApkPath.TrimStart('/').Replace('/', Path.DirectorySeparatorChar);

    public bool IsDownloadApkPath(string requestPath)
        => GetDownloadApkPaths().Any(path => string.Equals(requestPath, path, StringComparison.OrdinalIgnoreCase));

    public IEnumerable<string> GetApkRelativeFilePathCandidates()
        => GetDownloadApkPaths()
            .Select(path => path.TrimStart('/').Replace('/', Path.DirectorySeparatorChar));

    private IEnumerable<string> GetDownloadApkPaths()
    {
        yield return PublicDownloadApkPath;

        if (!string.Equals(PublicDownloadApkPath, DefaultDownloadApkPath, StringComparison.OrdinalIgnoreCase))
        {
            yield return DefaultDownloadApkPath;
        }

        if (!string.Equals(PublicDownloadApkPath, LegacyGuideDownloadApkPath, StringComparison.OrdinalIgnoreCase))
        {
            yield return LegacyGuideDownloadApkPath;
        }

        if (!string.Equals(PublicDownloadApkPath, LegacyTourDownloadApkPath, StringComparison.OrdinalIgnoreCase))
        {
            yield return LegacyTourDownloadApkPath;
        }
    }

    private string BuildAbsoluteUrl(HttpRequest request, string publicPath)
        => $"{GetEffectivePublicBaseUrl(request)}{NormalizePublicPath(publicPath, "/")}";

    private string GetEffectivePublicBaseUrl(HttpRequest request)
    {
        if (!string.IsNullOrWhiteSpace(PublicBaseUrl))
        {
            return PublicBaseUrl!;
        }

        var pathBase = request.PathBase.HasValue ? request.PathBase.Value ?? string.Empty : string.Empty;
        return $"{request.Scheme}://{request.Host}{pathBase}".TrimEnd('/');
    }

    private static string NormalizePublicPath(string? value, string fallbackValue)
    {
        var rawValue = string.IsNullOrWhiteSpace(value)
            ? fallbackValue
            : value.Trim();

        if (!rawValue.StartsWith("/", StringComparison.Ordinal))
        {
            rawValue = $"/{rawValue}";
        }

        while (rawValue.Contains("//", StringComparison.Ordinal))
        {
            rawValue = rawValue.Replace("//", "/", StringComparison.Ordinal);
        }

        return rawValue.Length > 1
            ? rawValue.TrimEnd('/')
            : rawValue;
    }

    private static string? NormalizeBaseUrl(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var trimmed = value.Trim();
        if (Uri.TryCreate(trimmed, UriKind.Absolute, out var absoluteUri))
        {
            return absoluteUri.ToString().TrimEnd('/');
        }

        if (Uri.TryCreate($"https://{trimmed}", UriKind.Absolute, out absoluteUri))
        {
            return absoluteUri.ToString().TrimEnd('/');
        }

        return trimmed.TrimEnd('/');
    }

    private static string? FirstNonEmpty(params string?[] values)
        => values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));
}
