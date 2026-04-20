using System.Net;
using System.Text;
using VinhKhanh.BackendApi.Contracts;
using VinhKhanh.BackendApi.Models;

namespace VinhKhanh.BackendApi.Infrastructure;

public sealed class PoiNarrationAudioService(
    PoiNarrationService poiNarrationService,
    IWebHostEnvironment environment,
    IHttpClientFactory httpClientFactory,
    ILogger<PoiNarrationAudioService> logger)
{
    private static readonly TimeSpan PreparedAudioProxyTimeout = TimeSpan.FromSeconds(20);

    public async Task<PoiNarrationAudioResult?> GetAudioAsync(
        string poiId,
        string languageCode,
        AdminRequestContext? actor,
        CancellationToken cancellationToken)
    {
        var narration = await poiNarrationService.ResolveAsync(
            poiId,
            languageCode,
            actor,
            cancellationToken);
        if (narration is null)
        {
            return null;
        }

        if (!CanUsePreparedAudio(narration.AudioGuide))
        {
            logger.LogWarning(
                "[AudioRequest] prepared audio lookup failed. poiId={PoiId}; requestedLang={RequestedLanguage}; normalizedLang={NormalizedLanguage}; effectiveLanguage={EffectiveLanguage}; foundAudio=false; reason={Reason}; audioGuideId={AudioGuideId}; audioGuideLanguage={AudioGuideLanguage}; generationStatus={GenerationStatus}; publicStatus={PublicStatus}; isOutdated={IsOutdated}; audioUrl={AudioUrl}; audioFilePath={AudioFilePath}; cacheKey={CacheKey}",
                narration.PoiId,
                narration.RequestedLanguageCode,
                PremiumAccessCatalog.NormalizeLanguageCode(narration.RequestedLanguageCode),
                narration.EffectiveLanguageCode,
                DescribeAudioGuideAvailability(narration.AudioGuide),
                narration.AudioGuide?.Id,
                narration.AudioGuide?.LanguageCode,
                narration.AudioGuide?.GenerationStatus,
                narration.AudioGuide?.Status,
                narration.AudioGuide?.IsOutdated,
                narration.AudioGuide?.AudioUrl,
                narration.AudioGuide?.AudioFilePath,
                narration.AudioCacheKey);
            throw new PoiNarrationAudioUnavailableException("Chua co audio pre-generated cho POI/ngon ngu nay.");
        }

        return await LoadPreparedAudioAsync(narration, cancellationToken);
    }

    private async Task<PoiNarrationAudioResult> LoadPreparedAudioAsync(
        PoiNarrationResponse narration,
        CancellationToken cancellationToken)
    {
        var audioGuide = narration.AudioGuide
            ?? throw new PoiNarrationAudioUnavailableException("POI nay khong co audio guide san sang phat.");
        var audioLocation = ResolveAudioLocation(audioGuide);
        if (string.IsNullOrWhiteSpace(audioLocation))
        {
            throw new PoiNarrationAudioUnavailableException("Audio guide khong co duong dan phat hop le.");
        }

        byte[] content;
        string contentType;
        string storageReference;

        if (TryResolveLocalStoragePath(audioLocation, out var localPath))
        {
            if (!File.Exists(localPath))
            {
                throw new FileNotFoundException("Khong tim thay file audio guide tren backend.", localPath);
            }

            content = await File.ReadAllBytesAsync(localPath, cancellationToken);
            contentType = InferContentType(localPath);
            storageReference = localPath;
        }
        else
        {
            using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutSource.CancelAfter(PreparedAudioProxyTimeout);

            using var client = httpClientFactory.CreateClient();
            using var request = new HttpRequestMessage(HttpMethod.Get, audioLocation);
            using var response = await client.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                timeoutSource.Token);

            if (!response.IsSuccessStatusCode)
            {
                var responseBody = await ReadResponseBodyAsync(response, timeoutSource.Token);
                throw new InvalidOperationException(
                    $"Backend khong the tai prepared audio tu '{audioLocation}'. HTTP {(int)response.StatusCode}. Body={responseBody}");
            }

            contentType = response.Content.Headers.ContentType?.ToString() ?? InferContentType(audioLocation);
            content = await ReadAudioBytesAsync(response, timeoutSource.Token);

            if (LooksLikeTextPayload(contentType, content))
            {
                throw new InvalidOperationException(
                    $"Prepared audio tra ve payload khong phai audio. Url={audioLocation}; Body={DecodeTextPayload(content)}");
            }

            storageReference = audioLocation;
        }

        if (content.Length == 0)
        {
            throw new InvalidOperationException("Prepared audio tra ve 0 byte.");
        }

        var durationSeconds = EstimateAudioDurationSeconds(contentType, content.Length);
        logger.LogInformation(
            "Prepared POI narration audio ready. poiId={PoiId}; requestedLanguage={RequestedLanguage}; effectiveLanguage={EffectiveLanguage}; audioGuideId={AudioGuideId}; audioBytes={AudioBytes}; durationSeconds={DurationSeconds}; storage={StorageReference}; cacheKey={CacheKey}",
            narration.PoiId,
            narration.RequestedLanguageCode,
            narration.EffectiveLanguageCode,
            audioGuide.Id,
            content.Length,
            durationSeconds.ToString("0.00"),
            storageReference,
            narration.AudioCacheKey);

        return new PoiNarrationAudioResult(
            content,
            contentType,
            "prepared_audio",
            narration.UiPlaybackKey,
            narration.AudioCacheKey,
            audioGuide.Id,
            audioGuide.ContentVersion,
            audioGuide.UpdatedAt,
            narration.EffectiveLanguageCode,
            narration.TtsLocale,
            narration.TtsInputText.Length,
            1,
            durationSeconds);
    }

    private bool TryResolveLocalStoragePath(string audioUrl, out string localPath)
    {
        localPath = string.Empty;
        var webRootPath = environment.WebRootPath;
        if (string.IsNullOrWhiteSpace(webRootPath))
        {
            return false;
        }

        var relativePath = audioUrl.Trim();
        if (Uri.TryCreate(relativePath, UriKind.Absolute, out var absoluteUri))
        {
            relativePath = absoluteUri.AbsolutePath;
        }

        relativePath = Uri.UnescapeDataString(relativePath);
        if (string.IsNullOrWhiteSpace(relativePath))
        {
            return false;
        }

        relativePath = relativePath.TrimStart('/');
        if (!relativePath.StartsWith("storage/", StringComparison.OrdinalIgnoreCase) &&
            !relativePath.StartsWith("storage\\", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var normalizedRelativePath = relativePath
            .Replace('/', Path.DirectorySeparatorChar)
            .Replace('\\', Path.DirectorySeparatorChar);
        var candidatePath = Path.GetFullPath(Path.Combine(webRootPath, normalizedRelativePath));
        if (!candidatePath.StartsWith(Path.GetFullPath(webRootPath), StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        localPath = candidatePath;
        return true;
    }

    private static bool CanUsePreparedAudio(AudioGuide? audioGuide) =>
        audioGuide is not null &&
        !audioGuide.IsOutdated &&
        string.Equals(AudioGuideCatalog.NormalizeGenerationStatus(audioGuide.GenerationStatus), AudioGuideCatalog.GenerationStatusSuccess, StringComparison.OrdinalIgnoreCase) &&
        string.Equals(AudioGuideCatalog.NormalizePublicStatus(audioGuide.Status), AudioGuideCatalog.PublicStatusReady, StringComparison.OrdinalIgnoreCase) &&
        (!string.IsNullOrWhiteSpace(audioGuide.AudioUrl) || !string.IsNullOrWhiteSpace(audioGuide.AudioFilePath));

    private static string DescribeAudioGuideAvailability(AudioGuide? audioGuide)
    {
        if (audioGuide is null)
        {
            return "no_audio_guide_record";
        }

        if (audioGuide.IsOutdated)
        {
            return "audio_guide_outdated";
        }

        var generationStatus = AudioGuideCatalog.NormalizeGenerationStatus(audioGuide.GenerationStatus);
        if (!string.Equals(generationStatus, AudioGuideCatalog.GenerationStatusSuccess, StringComparison.OrdinalIgnoreCase))
        {
            return $"generation_status_{generationStatus}";
        }

        var publicStatus = AudioGuideCatalog.NormalizePublicStatus(audioGuide.Status);
        if (!string.Equals(publicStatus, AudioGuideCatalog.PublicStatusReady, StringComparison.OrdinalIgnoreCase))
        {
            return $"public_status_{publicStatus}";
        }

        if (string.IsNullOrWhiteSpace(audioGuide.AudioUrl) && string.IsNullOrWhiteSpace(audioGuide.AudioFilePath))
        {
            return "missing_audio_url_and_file_path";
        }

        return "ready";
    }

    private static string? ResolveAudioLocation(AudioGuide audioGuide)
        => !string.IsNullOrWhiteSpace(audioGuide.AudioUrl)
            ? audioGuide.AudioUrl.Trim()
            : string.IsNullOrWhiteSpace(audioGuide.AudioFilePath)
                ? null
                : audioGuide.AudioFilePath.Trim();

    private static string InferContentType(string value)
    {
        var extension = Path.GetExtension(value)?.ToLowerInvariant();
        return extension switch
        {
            ".mp3" => "audio/mpeg",
            ".wav" => "audio/wav",
            ".ogg" => "audio/ogg",
            ".opus" => "audio/opus",
            _ => "application/octet-stream"
        };
    }

    private static async Task<byte[]> ReadAudioBytesAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        await using var buffer = new MemoryStream();
        await stream.CopyToAsync(buffer, cancellationToken);
        return buffer.ToArray();
    }

    private static async Task<string> ReadResponseBodyAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        try
        {
            var content = await response.Content.ReadAsStringAsync(cancellationToken);
            return string.IsNullOrWhiteSpace(content)
                ? string.Empty
                : WebUtility.HtmlDecode(content).Trim();
        }
        catch
        {
            return string.Empty;
        }
    }

    private static bool LooksLikeTextPayload(string? contentType, byte[] content)
    {
        if (!string.IsNullOrWhiteSpace(contentType))
        {
            if (contentType.StartsWith("audio/", StringComparison.OrdinalIgnoreCase) ||
                contentType.StartsWith("application/octet-stream", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            if (contentType.Contains("json", StringComparison.OrdinalIgnoreCase) ||
                contentType.StartsWith("text/", StringComparison.OrdinalIgnoreCase) ||
                contentType.Contains("xml", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        if (content.Length == 0)
        {
            return false;
        }

        var previewLength = Math.Min(content.Length, 64);
        var preview = Encoding.UTF8.GetString(content, 0, previewLength).TrimStart('\uFEFF', ' ', '\t', '\r', '\n');
        return preview.StartsWith("{", StringComparison.Ordinal) ||
               preview.StartsWith("[", StringComparison.Ordinal) ||
               preview.StartsWith("<", StringComparison.Ordinal);
    }

    private static string DecodeTextPayload(byte[] content)
        => content.Length == 0
            ? string.Empty
            : WebUtility.HtmlDecode(Encoding.UTF8.GetString(content)).Trim();

    private static double EstimateAudioDurationSeconds(string? contentType, int audioBytes)
    {
        if (audioBytes <= 0)
        {
            return 0;
        }

        if (contentType?.Contains("audio/mpeg", StringComparison.OrdinalIgnoreCase) ?? false)
        {
            return (audioBytes * 8d) / 128000d;
        }

        if (contentType?.Contains("audio/wav", StringComparison.OrdinalIgnoreCase) ?? false)
        {
            return audioBytes / 32000d;
        }

        return 0;
    }
}

public sealed record PoiNarrationAudioResult(
    byte[] Content,
    string ContentType,
    string Source,
    string UiPlaybackKey,
    string AudioCacheKey,
    string AudioGuideId,
    string ContentVersion,
    DateTimeOffset UpdatedAt,
    string EffectiveLanguageCode,
    string TtsLocale,
    int TextLength,
    int SegmentCount,
    double EstimatedDurationSeconds);

public sealed class PoiNarrationAudioUnavailableException(string message) : InvalidOperationException(message);
