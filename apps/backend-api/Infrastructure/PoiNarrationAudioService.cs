using System.Net;
using System.Text;
using Microsoft.Extensions.Caching.Memory;
using VinhKhanh.BackendApi.Contracts;
using VinhKhanh.BackendApi.Models;

namespace VinhKhanh.BackendApi.Infrastructure;

public sealed class PoiNarrationAudioService(
    PoiNarrationService poiNarrationService,
    ITextToSpeechService textToSpeechService,
    IMemoryCache cache,
    IWebHostEnvironment environment,
    IHttpClientFactory httpClientFactory,
    ILogger<PoiNarrationAudioService> logger)
{
    private static readonly TimeSpan PreparedAudioProxyTimeout = TimeSpan.FromSeconds(20);
    private static readonly TimeSpan CachedAudioDuration = TimeSpan.FromHours(2);

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

        if (cache.TryGetValue<PoiNarrationAudioResult>(narration.AudioCacheKey, out var cachedAudio) &&
            cachedAudio is not null)
        {
            logger.LogInformation(
                "POI narration audio cache hit. PoiId={PoiId}; requestedLanguage={RequestedLanguage}; effectiveLanguage={EffectiveLanguage}; source={Source}; cacheKey={CacheKey}; audioBytes={AudioBytes}; durationSeconds={DurationSeconds}",
                narration.PoiId,
                narration.RequestedLanguageCode,
                cachedAudio.EffectiveLanguageCode,
                cachedAudio.Source,
                narration.AudioCacheKey,
                cachedAudio.Content.Length,
                cachedAudio.EstimatedDurationSeconds.ToString("0.00"));
            return cachedAudio;
        }

        PoiNarrationAudioResult resolvedAudio;
        if (CanUsePreparedAudio(narration.AudioGuide))
        {
            try
            {
                resolvedAudio = await LoadPreparedAudioAsync(narration, cancellationToken);
            }
            catch (Exception exception) when (
                !cancellationToken.IsCancellationRequested &&
                HasNarrationText(narration.TtsInputText))
            {
                logger.LogWarning(
                    exception,
                    "Prepared narration audio was unavailable. Falling back to TTS. PoiId={PoiId}; requestedLanguage={RequestedLanguage}; effectiveLanguage={EffectiveLanguage}; cacheKey={CacheKey}",
                    narration.PoiId,
                    narration.RequestedLanguageCode,
                    narration.EffectiveLanguageCode,
                    narration.AudioCacheKey);
                resolvedAudio = await GenerateTtsAudioAsync(narration, cancellationToken);
            }
        }
        else if (HasNarrationText(narration.TtsInputText))
        {
            resolvedAudio = await GenerateTtsAudioAsync(narration, cancellationToken);
        }
        else
        {
            throw new PoiNarrationAudioUnavailableException("Chưa có audio hoặc nội dung thuyết minh khả dụng cho POI này.");
        }

        cache.Set(
            narration.AudioCacheKey,
            resolvedAudio,
            new MemoryCacheEntryOptions
            {
                SlidingExpiration = CachedAudioDuration,
                Size = Math.Max(resolvedAudio.Content.Length, 1)
            });

        return resolvedAudio;
    }

    private async Task<PoiNarrationAudioResult> LoadPreparedAudioAsync(
        PoiNarrationResponse narration,
        CancellationToken cancellationToken)
    {
        var audioGuide = narration.AudioGuide
            ?? throw new PoiNarrationAudioUnavailableException("POI này không có audio guide sẵn sàng phát.");
        var audioUrl = audioGuide.AudioUrl?.Trim();
        if (string.IsNullOrWhiteSpace(audioUrl))
        {
            throw new PoiNarrationAudioUnavailableException("Audio guide không có đường dẫn phát hợp lệ.");
        }

        byte[] content;
        string contentType;
        string storageReference;

        if (TryResolveLocalStoragePath(audioUrl, out var localPath))
        {
            if (!File.Exists(localPath))
            {
                throw new FileNotFoundException("Không tìm thấy file audio guide trên backend.", localPath);
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
            using var request = new HttpRequestMessage(HttpMethod.Get, audioUrl);
            using var response = await client.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                timeoutSource.Token);

            if (!response.IsSuccessStatusCode)
            {
                var responseBody = await ReadResponseBodyAsync(response, timeoutSource.Token);
                throw new InvalidOperationException(
                    $"Backend không thể tải prepared audio từ '{audioUrl}'. HTTP {(int)response.StatusCode}. Body={responseBody}");
            }

            contentType = response.Content.Headers.ContentType?.ToString() ?? InferContentType(audioUrl);
            content = await ReadAudioBytesAsync(response, timeoutSource.Token);

            if (LooksLikeTextPayload(contentType, content))
            {
                throw new InvalidOperationException(
                    $"Prepared audio trả về payload không phải audio. Url={audioUrl}; Body={DecodeTextPayload(content)}");
            }

            storageReference = audioUrl;
        }

        if (content.Length == 0)
        {
            throw new InvalidOperationException("Prepared audio trả về 0 byte.");
        }

        var durationSeconds = EstimateAudioDurationSeconds(contentType, content.Length);
        logger.LogInformation(
            "Prepared POI narration audio ready. PoiId={PoiId}; requestedLanguage={RequestedLanguage}; effectiveLanguage={EffectiveLanguage}; audioGuideId={AudioGuideId}; audioBytes={AudioBytes}; durationSeconds={DurationSeconds}; storage={StorageReference}; cacheKey={CacheKey}",
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
            narration.EffectiveLanguageCode,
            narration.TtsLocale,
            narration.TtsInputText.Length,
            1,
            durationSeconds);
    }

    private async Task<PoiNarrationAudioResult> GenerateTtsAudioAsync(
        PoiNarrationResponse narration,
        CancellationToken cancellationToken)
    {
        var audio = await textToSpeechService.GenerateAudioAsync(
            new TextToSpeechRequest(
                narration.TtsInputText,
                narration.EffectiveLanguageCode),
            cancellationToken);

        logger.LogInformation(
            "Generated POI narration TTS audio. PoiId={PoiId}; requestedLanguage={RequestedLanguage}; effectiveLanguage={EffectiveLanguage}; textLength={TextLength}; segmentCount={SegmentCount}; audioBytes={AudioBytes}; durationSeconds={DurationSeconds}; cacheKey={CacheKey}",
            narration.PoiId,
            narration.RequestedLanguageCode,
            narration.EffectiveLanguageCode,
            audio.TextLength,
            audio.SegmentCount,
            audio.Content.Length,
            audio.EstimatedDurationSeconds.ToString("0.00"),
            narration.AudioCacheKey);

        return new PoiNarrationAudioResult(
            audio.Content,
            audio.ContentType,
            "generated_tts",
            narration.UiPlaybackKey,
            narration.AudioCacheKey,
            narration.EffectiveLanguageCode,
            narration.TtsLocale,
            audio.TextLength,
            audio.SegmentCount,
            audio.EstimatedDurationSeconds);
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
        string.Equals(audioGuide.SourceType, "uploaded", StringComparison.OrdinalIgnoreCase) &&
        string.Equals(audioGuide.Status, "ready", StringComparison.OrdinalIgnoreCase) &&
        !string.IsNullOrWhiteSpace(audioGuide.AudioUrl);

    private static bool HasNarrationText(string? value)
        => !string.IsNullOrWhiteSpace(value);

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
    string EffectiveLanguageCode,
    string TtsLocale,
    int TextLength,
    int SegmentCount,
    double EstimatedDurationSeconds);

public sealed class PoiNarrationAudioUnavailableException(string message) : InvalidOperationException(message);
