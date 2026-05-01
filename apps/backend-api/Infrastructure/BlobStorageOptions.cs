using Microsoft.Extensions.Configuration;

namespace VinhKhanh.BackendApi.Infrastructure;

public sealed class BlobStorageOptions
{
    public const string SectionName = "BlobStorage";

    public string ConnectionString { get; set; } = string.Empty;
    public string ContainerName { get; set; } = "public-assets";
    public string PublicBaseUrl { get; set; } = string.Empty;
    public string ApkFolder { get; set; } = "downloads";
    public string AudioFolder { get; set; } = "audio";
    public string MediaFolder { get; set; } = "media";

    public bool HasConnectionString => !string.IsNullOrWhiteSpace(ConnectionString);

    public static void ApplyConfiguration(BlobStorageOptions options, IConfiguration configuration)
    {
        var section = configuration.GetSection(SectionName);

        options.ConnectionString = section["ConnectionString"]?.Trim() ?? options.ConnectionString;
        options.ContainerName = NormalizeContainerName(
            FirstNonEmpty(section["ContainerName"], options.ContainerName, "public-assets")!);
        options.PublicBaseUrl = NormalizeBaseUrl(FirstNonEmpty(section["PublicBaseUrl"], options.PublicBaseUrl)) ?? string.Empty;
        options.ApkFolder = NormalizeBlobFolder(FirstNonEmpty(section["ApkFolder"], options.ApkFolder, "downloads")!);
        options.AudioFolder = NormalizeBlobFolder(FirstNonEmpty(section["AudioFolder"], options.AudioFolder, "audio")!);
        options.MediaFolder = NormalizeBlobFolder(FirstNonEmpty(section["MediaFolder"], options.MediaFolder, "media")!);
    }

    public string GetApkBlobPath(string? fileName)
        => CombineBlobPath(ApkFolder, string.IsNullOrWhiteSpace(fileName) ? "tour.apk" : Path.GetFileName(fileName.Trim()));

    public static string CombineBlobPath(params string?[] segments)
        => string.Join(
            "/",
            segments
                .Where(segment => !string.IsNullOrWhiteSpace(segment))
                .SelectMany(segment => segment!
                    .Replace('\\', '/')
                    .Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                .Select(SanitizeBlobSegment)
                .Where(segment => !string.IsNullOrWhiteSpace(segment)));

    public static string NormalizeBlobFolder(string value)
    {
        var normalized = CombineBlobPath(value);
        return string.IsNullOrWhiteSpace(normalized) ? "assets" : normalized;
    }

    private static string NormalizeContainerName(string value)
    {
        var normalized = value.Trim().ToLowerInvariant();
        return string.IsNullOrWhiteSpace(normalized) ? "public-assets" : normalized;
    }

    private static string SanitizeBlobSegment(string value)
    {
        var builder = new System.Text.StringBuilder(value.Length);
        foreach (var character in value.Trim())
        {
            if (char.IsAsciiLetterOrDigit(character) || character is '-' or '_' or '.')
            {
                builder.Append(character);
            }
            else if (builder.Length == 0 || builder[^1] != '-')
            {
                builder.Append('-');
            }
        }

        return builder.ToString().Trim('-', '.');
    }

    private static string? NormalizeBaseUrl(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var trimmed = value.Trim().TrimEnd('/');
        if (Uri.TryCreate(trimmed, UriKind.Absolute, out var absoluteUri))
        {
            return absoluteUri.ToString().TrimEnd('/');
        }

        return trimmed;
    }

    private static string? FirstNonEmpty(params string?[] values)
        => values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));
}
