using System.Text.RegularExpressions;

namespace VinhKhanh.BackendApi.Infrastructure;

public sealed class GeneratedAudioStorageService(
    IWebHostEnvironment environment,
    ILogger<GeneratedAudioStorageService> logger,
    Microsoft.Extensions.Options.IOptions<TextToSpeechOptions> optionsAccessor)
{
    private static readonly Regex InvalidFileSegmentPattern = new("[^a-zA-Z0-9_-]+", RegexOptions.Compiled);

    public async Task<StoredGeneratedAudioFile> SavePoiAudioAsync(
        string poiId,
        string languageCode,
        string contentVersion,
        string outputFormat,
        string contentType,
        byte[] content,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(poiId);
        ArgumentException.ThrowIfNullOrWhiteSpace(languageCode);
        ArgumentException.ThrowIfNullOrWhiteSpace(contentVersion);

        if (content.Length == 0)
        {
            throw new InvalidOperationException("Generated audio content is empty.");
        }

        var options = optionsAccessor.Value;
        var relativeRoot = NormalizeRelativeRoot(options.AudioStorageRoot);
        var safePoiId = SanitizeSegment(poiId);
        var storageLanguageCode = LanguageRegistry.GetStorageCode(languageCode);
        var safeLanguageCode = SanitizeSegment(storageLanguageCode);
        var contentVersionSuffix = SanitizeSegment(contentVersion)[..Math.Min(12, SanitizeSegment(contentVersion).Length)];
        var fileExtension = ResolveFileExtension(outputFormat, contentType);
        var relativeDirectory = $"{relativeRoot}/pois/{safePoiId}/{safeLanguageCode}";
        var fileName = $"{safePoiId}-{safeLanguageCode}-{contentVersionSuffix}-{DateTimeOffset.UtcNow:yyyyMMddHHmmssfff}{fileExtension}";
        var absoluteDirectory = ResolveAbsoluteDirectory(relativeDirectory);
        var absolutePath = Path.Combine(absoluteDirectory, fileName);

        Directory.CreateDirectory(absoluteDirectory);

        var tempPath = Path.Combine(absoluteDirectory, $"{Guid.NewGuid():N}.tmp");
        await File.WriteAllBytesAsync(tempPath, content, cancellationToken);

        if (File.Exists(absolutePath))
        {
            File.Delete(absolutePath);
        }

        File.Move(tempPath, absolutePath);

        var relativePath = $"{relativeDirectory}/{fileName}";
        var publicUrl = BuildPublicUrl(relativePath);
        logger.LogInformation(
            "[AudioGenerate] Generated audio file saved. poiId={PoiId}; requestedLanguage={RequestedLanguage}; storageLanguage={StorageLanguage}; relativePath={RelativePath}; sizeBytes={SizeBytes}",
            poiId,
            languageCode,
            storageLanguageCode,
            relativePath,
            content.LongLength);

        return new StoredGeneratedAudioFile(
            relativePath,
            fileName,
            absolutePath,
            publicUrl,
            string.IsNullOrWhiteSpace(contentType) ? "audio/mpeg" : contentType,
            content.LongLength);
    }

    public bool Exists(string? relativePathOrUrl)
    {
        var absolutePath = TryResolveAbsolutePath(relativePathOrUrl);
        return !string.IsNullOrWhiteSpace(absolutePath) && File.Exists(absolutePath);
    }

    public string? TryResolveAbsolutePath(string? relativePathOrUrl)
    {
        if (string.IsNullOrWhiteSpace(relativePathOrUrl))
        {
            return null;
        }

        var normalized = relativePathOrUrl.Trim();
        if (Path.IsPathRooted(normalized))
        {
            return normalized;
        }

        if (Uri.TryCreate(normalized, UriKind.Absolute, out var absoluteUri))
        {
            normalized = absoluteUri.AbsolutePath;
        }

        normalized = Uri.UnescapeDataString(normalized).TrimStart('/');
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return null;
        }

        var options = optionsAccessor.Value;
        var relativeRoot = NormalizeRelativeRoot(options.AudioStorageRoot);
        var publicBasePath = NormalizePublicBasePath(options.AudioPublicBasePath).TrimStart('/');
        if (normalized.StartsWith(publicBasePath, StringComparison.OrdinalIgnoreCase))
        {
            normalized = $"{relativeRoot}{normalized[publicBasePath.Length..]}";
        }

        if (!normalized.StartsWith(relativeRoot, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var absolutePath = Path.GetFullPath(Path.Combine(GetWebRootPath(), normalized.Replace('/', Path.DirectorySeparatorChar)));
        var webRootPath = Path.GetFullPath(GetWebRootPath());
        return absolutePath.StartsWith(webRootPath, StringComparison.OrdinalIgnoreCase)
            ? absolutePath
            : null;
    }

    public bool DeleteIfExists(string? relativePathOrUrl)
    {
        var absolutePath = TryResolveAbsolutePath(relativePathOrUrl);
        if (string.IsNullOrWhiteSpace(absolutePath) || !File.Exists(absolutePath))
        {
            return false;
        }

        File.Delete(absolutePath);
        return true;
    }

    public string BuildPublicUrl(string relativePath)
    {
        var normalizedRelativePath = relativePath.Replace('\\', '/').TrimStart('/');
        var options = optionsAccessor.Value;
        var relativeRoot = NormalizeRelativeRoot(options.AudioStorageRoot);
        var suffix = normalizedRelativePath.StartsWith(relativeRoot, StringComparison.OrdinalIgnoreCase)
            ? normalizedRelativePath[relativeRoot.Length..]
            : $"/{normalizedRelativePath}";
        var normalizedSuffix = suffix.StartsWith("/", StringComparison.Ordinal) ? suffix : $"/{suffix}";
        var publicBasePath = NormalizePublicBasePath(options.AudioPublicBasePath);

        if (!string.IsNullOrWhiteSpace(options.AudioPublicBaseUrl))
        {
            return $"{options.AudioPublicBaseUrl.TrimEnd('/')}{normalizedSuffix}";
        }

        return $"{publicBasePath}{normalizedSuffix}";
    }

    private string ResolveAbsoluteDirectory(string relativeDirectory)
    {
        var webRootPath = GetWebRootPath();
        var absolutePath = Path.GetFullPath(Path.Combine(webRootPath, relativeDirectory.Replace('/', Path.DirectorySeparatorChar)));
        var normalizedWebRootPath = Path.GetFullPath(webRootPath);
        if (!absolutePath.StartsWith(normalizedWebRootPath, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Resolved generated audio path is outside the web root.");
        }

        return absolutePath;
    }

    private string GetWebRootPath()
        => environment.WebRootPath ?? Path.Combine(environment.ContentRootPath, "wwwroot");

    private static string NormalizeRelativeRoot(string? value)
    {
        var normalized = string.IsNullOrWhiteSpace(value)
            ? TextToSpeechOptions.DefaultAudioStorageRootValue
            : value.Trim().TrimStart('/').Replace('\\', '/');
        return string.Join(
            "/",
            normalized
                .Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(SanitizeSegment)
                .Where(segment => !string.IsNullOrWhiteSpace(segment)));
    }

    private static string NormalizePublicBasePath(string? value)
    {
        var normalized = string.IsNullOrWhiteSpace(value)
            ? TextToSpeechOptions.DefaultAudioPublicBasePathValue
            : value.Trim().Replace('\\', '/');
        if (!normalized.StartsWith("/", StringComparison.Ordinal))
        {
            normalized = $"/{normalized}";
        }

        return normalized.TrimEnd('/');
    }

    private static string ResolveFileExtension(string outputFormat, string? contentType)
    {
        if (!string.IsNullOrWhiteSpace(outputFormat))
        {
            if (outputFormat.StartsWith("wav_", StringComparison.OrdinalIgnoreCase))
            {
                return ".wav";
            }

            if (outputFormat.StartsWith("ogg_", StringComparison.OrdinalIgnoreCase))
            {
                return ".ogg";
            }
        }

        if (!string.IsNullOrWhiteSpace(contentType))
        {
            if (contentType.Contains("wav", StringComparison.OrdinalIgnoreCase))
            {
                return ".wav";
            }

            if (contentType.Contains("ogg", StringComparison.OrdinalIgnoreCase))
            {
                return ".ogg";
            }
        }

        return ".mp3";
    }

    private static string SanitizeSegment(string value)
    {
        var sanitized = InvalidFileSegmentPattern.Replace(value.Trim(), "-").Trim('-');
        return string.IsNullOrWhiteSpace(sanitized)
            ? "audio"
            : sanitized;
    }
}

public sealed record StoredGeneratedAudioFile(
    string RelativePath,
    string FileName,
    string AbsolutePath,
    string PublicUrl,
    string ContentType,
    long SizeBytes);
