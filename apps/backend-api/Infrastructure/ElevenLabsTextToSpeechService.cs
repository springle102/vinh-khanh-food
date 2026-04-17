using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace VinhKhanh.BackendApi.Infrastructure;

public sealed class ElevenLabsTextToSpeechService(
    HttpClient httpClient,
    IOptions<TextToSpeechOptions> optionsAccessor,
    IMemoryCache cache,
    ILogger<ElevenLabsTextToSpeechService> logger) : ITextToSpeechService
{
    private const int MaxSegmentLength = 900;
    private const int MinSegmentLength = 500;

    public async Task<TextToSpeechResult> GenerateAudioAsync(
        TextToSpeechRequest request,
        CancellationToken cancellationToken)
    {
        var normalizedText = SanitizeText(request.Text);
        if (string.IsNullOrWhiteSpace(normalizedText))
        {
            throw new InvalidOperationException("Text la bat buoc de tao audio TTS.");
        }

        var options = optionsAccessor.Value;
        var apiKey = options.ApiKey?.Trim();
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            logger.LogError(
                "ElevenLabs API key is missing. Configure {ApiKeyConfigKey} via environment variables, appsettings, or user secrets.",
                TextToSpeechOptions.ApiKeyConfigKey);
            throw new TextToSpeechConfigurationException(
                $"Thieu cau hinh {TextToSpeechOptions.ApiKeyConfigKey} cho ElevenLabs Text-to-Speech.");
        }

        var voiceId = FirstNonEmpty(
            request.VoiceId,
            options.DefaultVoiceId,
            TextToSpeechOptions.DefaultVoiceIdValue)!;
        var modelId = FirstNonEmpty(
            request.ModelId,
            options.ModelId,
            TextToSpeechOptions.DefaultModelIdValue)!;
        var outputFormat = FirstNonEmpty(
            request.OutputFormat,
            options.OutputFormat,
            TextToSpeechOptions.DefaultOutputFormatValue)!;
        var apiKeyConfigured = !string.IsNullOrWhiteSpace(apiKey);
        var normalizedLanguageCode = NormalizeLanguageCode(request.LanguageCode);
        var segments = SplitNarrationIntoSegments(normalizedText, MaxSegmentLength);
        var effectiveOutputFormat = segments.Count > 1 &&
                                    !outputFormat.StartsWith("mp3_", StringComparison.OrdinalIgnoreCase)
            ? TextToSpeechOptions.DefaultOutputFormatValue
            : outputFormat;

        if (!string.Equals(effectiveOutputFormat, outputFormat, StringComparison.OrdinalIgnoreCase))
        {
            logger.LogWarning(
                "Segmented ElevenLabs TTS requested outputFormat={RequestedOutputFormat}; overriding to safe format {EffectiveOutputFormat} so merged audio stays playable.",
                outputFormat,
                effectiveOutputFormat);
        }

        var cacheKey = BuildCacheKey(
            normalizedText,
            normalizedLanguageCode ?? request.LanguageCode?.Trim() ?? string.Empty,
            voiceId,
            modelId,
            effectiveOutputFormat);

        if (cache.TryGetValue<TextToSpeechResult>(cacheKey, out var cachedAudio) &&
            cachedAudio is not null)
        {
            logger.LogDebug(
                "ElevenLabs TTS cache hit. language={LanguageCode}; voiceId={VoiceId}; modelId={ModelId}; textHash={TextHash}",
                normalizedLanguageCode ?? request.LanguageCode?.Trim(),
                voiceId,
                modelId,
                CreateHash(normalizedText));
            return cachedAudio;
        }

        logger.LogInformation(
            "Preparing ElevenLabs TTS request. apiKeyConfigured={ApiKeyConfigured}; language={LanguageCode}; voiceId={VoiceId}; modelId={ModelId}; outputFormat={OutputFormat}; textLength={TextLength}; segmentCount={SegmentCount}; textHash={TextHash}; text={Text}",
            apiKeyConfigured,
            normalizedLanguageCode ?? request.LanguageCode?.Trim(),
            voiceId,
            modelId,
            effectiveOutputFormat,
            normalizedText.Length,
            segments.Count,
            CreateHash(normalizedText),
            normalizedText);

        if (segments.Count > 1)
        {
            logger.LogInformation(
                "Segmented ElevenLabs TTS request into {SegmentCount} chunks. segmentLengths={SegmentLengths}; textHash={TextHash}",
                segments.Count,
                string.Join(",", segments.Select(segment => segment.Length)),
                CreateHash(normalizedText));
        }

        try
        {
            var audioChunks = new List<byte[]>(segments.Count);
            string? contentType = null;
            double totalEstimatedDurationSeconds = 0;

            for (var index = 0; index < segments.Count; index += 1)
            {
                var segmentRequest = CreateSegmentRequest(request, segments, index);
                var chunk = await GenerateChunkAsync(
                    segmentRequest,
                    apiKey,
                    apiKeyConfigured,
                    voiceId,
                    modelId,
                    effectiveOutputFormat,
                    normalizedLanguageCode,
                    cancellationToken);
                audioChunks.Add(chunk.Content);
                contentType ??= chunk.ContentType;
                totalEstimatedDurationSeconds += chunk.EstimatedDurationSeconds;
            }

            var finalAudio = CombineAudioChunks(audioChunks);
            if (finalAudio.Length == 0)
            {
                throw new TextToSpeechGenerationException(
                    $"ElevenLabs Text-to-Speech tra ve 0 byte audio cho voice_id '{voiceId}' va model_id '{modelId}'.");
            }

            logger.LogInformation(
                "ElevenLabs TTS combined audio ready. apiKeyConfigured={ApiKeyConfigured}; language={LanguageCode}; voiceId={VoiceId}; modelId={ModelId}; outputFormat={OutputFormat}; segmentCount={SegmentCount}; audioBytes={AudioBytes}; estimatedDurationSeconds={EstimatedDurationSeconds}",
                apiKeyConfigured,
                normalizedLanguageCode ?? request.LanguageCode?.Trim(),
                voiceId,
                modelId,
                effectiveOutputFormat,
                segments.Count,
                finalAudio.Length,
                totalEstimatedDurationSeconds.ToString("0.00"));

            var result = new TextToSpeechResult(
                finalAudio,
                string.IsNullOrWhiteSpace(contentType) ? InferContentType(effectiveOutputFormat) : contentType,
                "elevenlabs",
                voiceId,
                modelId,
                effectiveOutputFormat);
            CacheAudio(cacheKey, result, options);
            return result;
        }
        catch (OperationCanceledException exception) when (!cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning(
                exception,
                "ElevenLabs TTS timed out. apiKeyConfigured={ApiKeyConfigured}; language={LanguageCode}; voiceId={VoiceId}; modelId={ModelId}; outputFormat={OutputFormat}; segmentCount={SegmentCount}; textHash={TextHash}",
                apiKeyConfigured,
                normalizedLanguageCode ?? request.LanguageCode?.Trim(),
                voiceId,
                modelId,
                effectiveOutputFormat,
                segments.Count,
                CreateHash(normalizedText));
            throw new TextToSpeechGenerationException("Yeu cau ElevenLabs Text-to-Speech bi timeout.", exception);
        }
        catch (HttpRequestException exception)
        {
            logger.LogWarning(
                exception,
                "Unable to reach ElevenLabs TTS. apiKeyConfigured={ApiKeyConfigured}; language={LanguageCode}; voiceId={VoiceId}; modelId={ModelId}; outputFormat={OutputFormat}; segmentCount={SegmentCount}; textHash={TextHash}",
                apiKeyConfigured,
                normalizedLanguageCode ?? request.LanguageCode?.Trim(),
                voiceId,
                modelId,
                effectiveOutputFormat,
                segments.Count,
                CreateHash(normalizedText));
            throw new TextToSpeechGenerationException("Khong the ket noi den ElevenLabs Text-to-Speech.", exception);
        }
    }

    private async Task<ChunkAudioResult> GenerateChunkAsync(
        TextToSpeechRequest request,
        string apiKey,
        bool apiKeyConfigured,
        string voiceId,
        string modelId,
        string outputFormat,
        string? normalizedLanguageCode,
        CancellationToken cancellationToken)
    {
        var payload = new Dictionary<string, object>
        {
            ["text"] = request.Text,
            ["model_id"] = modelId
        };

        if (!string.IsNullOrWhiteSpace(normalizedLanguageCode))
        {
            payload["language_code"] = normalizedLanguageCode;
        }

        if (!string.IsNullOrWhiteSpace(request.PreviousText))
        {
            payload["previous_text"] = request.PreviousText.Trim();
        }

        if (!string.IsNullOrWhiteSpace(request.NextText))
        {
            payload["next_text"] = request.NextText.Trim();
        }

        var requestUri =
            $"v1/text-to-speech/{Uri.EscapeDataString(voiceId)}" +
            $"?output_format={Uri.EscapeDataString(outputFormat)}";
        var requestEndpoint = httpClient.BaseAddress is null
            ? requestUri
            : new Uri(httpClient.BaseAddress, requestUri).ToString();
        var payloadJson = JsonSerializer.Serialize(payload);
        var segmentNumber = (request.SegmentIndex ?? 0) + 1;
        var totalSegments = request.TotalSegments ?? 1;

        logger.LogInformation(
            "Sending ElevenLabs TTS segment. apiKeyConfigured={ApiKeyConfigured}; endpoint={Endpoint}; language={LanguageCode}; voiceId={VoiceId}; modelId={ModelId}; outputFormat={OutputFormat}; segment={SegmentNumber}/{TotalSegments}; textLength={TextLength}; textHash={TextHash}; text={Text}",
            apiKeyConfigured,
            requestEndpoint,
            normalizedLanguageCode ?? request.LanguageCode?.Trim(),
            voiceId,
            modelId,
            outputFormat,
            segmentNumber,
            totalSegments,
            request.Text.Length,
            CreateHash(request.Text),
            request.Text);

        using var message = new HttpRequestMessage(HttpMethod.Post, requestUri)
        {
            Content = new StringContent(payloadJson, Encoding.UTF8)
        };
        message.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
        message.Headers.Add("xi-api-key", apiKey);

        using var response = await httpClient.SendAsync(
            message,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            var errorContentType = response.Content.Headers.ContentType?.ToString();
            var responseBody = await ReadResponseBodyAsync(response, cancellationToken);
            logger.LogWarning(
                "ElevenLabs TTS failed. apiKeyConfigured={ApiKeyConfigured}; endpoint={Endpoint}; statusCode={StatusCode}; contentType={ContentType}; language={LanguageCode}; voiceId={VoiceId}; modelId={ModelId}; outputFormat={OutputFormat}; segment={SegmentNumber}/{TotalSegments}; textLength={TextLength}; textHash={TextHash}; responseBody={ResponseBody}",
                apiKeyConfigured,
                requestEndpoint,
                (int)response.StatusCode,
                errorContentType,
                normalizedLanguageCode ?? request.LanguageCode?.Trim(),
                voiceId,
                modelId,
                outputFormat,
                segmentNumber,
                totalSegments,
                request.Text.Length,
                CreateHash(request.Text),
                responseBody);
            throw new TextToSpeechGenerationException(
                $"ElevenLabs Text-to-Speech tra ve loi HTTP {(int)response.StatusCode} ({response.StatusCode}).");
        }

        var contentType = response.Content.Headers.ContentType?.ToString();
        var content = await ReadAudioBytesAsync(response, cancellationToken);
        if (LooksLikeTextErrorPayload(contentType, content))
        {
            var errorBody = DecodeTextPayload(content);
            logger.LogWarning(
                "ElevenLabs TTS returned a non-audio payload. apiKeyConfigured={ApiKeyConfigured}; endpoint={Endpoint}; statusCode={StatusCode}; contentType={ContentType}; language={LanguageCode}; voiceId={VoiceId}; modelId={ModelId}; outputFormat={OutputFormat}; segment={SegmentNumber}/{TotalSegments}; audioBytes={AudioBytes}; responseBody={ResponseBody}",
                apiKeyConfigured,
                requestEndpoint,
                (int)response.StatusCode,
                contentType,
                normalizedLanguageCode ?? request.LanguageCode?.Trim(),
                voiceId,
                modelId,
                outputFormat,
                segmentNumber,
                totalSegments,
                content.Length,
                errorBody);
            throw new TextToSpeechGenerationException("ElevenLabs Text-to-Speech khong tra ve du lieu audio hop le.");
        }

        if (content.Length == 0)
        {
            logger.LogWarning(
                "ElevenLabs TTS returned an empty audio payload. apiKeyConfigured={ApiKeyConfigured}; endpoint={Endpoint}; statusCode={StatusCode}; contentType={ContentType}; language={LanguageCode}; voiceId={VoiceId}; modelId={ModelId}; outputFormat={OutputFormat}; segment={SegmentNumber}/{TotalSegments}; textHash={TextHash}",
                apiKeyConfigured,
                requestEndpoint,
                (int)response.StatusCode,
                contentType,
                normalizedLanguageCode ?? request.LanguageCode?.Trim(),
                voiceId,
                modelId,
                outputFormat,
                segmentNumber,
                totalSegments,
                CreateHash(request.Text));
            throw new TextToSpeechGenerationException(
                $"ElevenLabs Text-to-Speech tra ve 0 byte audio cho voice_id '{voiceId}' va model_id '{modelId}'.");
        }

        var estimatedDurationSeconds = EstimateAudioDurationSeconds(outputFormat, content.Length);
        logger.LogInformation(
            "ElevenLabs TTS segment succeeded. apiKeyConfigured={ApiKeyConfigured}; endpoint={Endpoint}; statusCode={StatusCode}; contentType={ContentType}; language={LanguageCode}; voiceId={VoiceId}; modelId={ModelId}; outputFormat={OutputFormat}; segment={SegmentNumber}/{TotalSegments}; audioBytes={AudioBytes}; estimatedDurationSeconds={EstimatedDurationSeconds}",
            apiKeyConfigured,
            requestEndpoint,
            (int)response.StatusCode,
            string.IsNullOrWhiteSpace(contentType) ? "unknown" : contentType,
            normalizedLanguageCode ?? request.LanguageCode?.Trim(),
            voiceId,
            modelId,
            outputFormat,
            segmentNumber,
            totalSegments,
            content.Length,
            estimatedDurationSeconds.ToString("0.00"));

        return new ChunkAudioResult(
            content,
            string.IsNullOrWhiteSpace(contentType) ? InferContentType(outputFormat) : contentType,
            estimatedDurationSeconds);
    }

    private static string? NormalizeLanguageCode(string? languageCode)
    {
        var normalized = languageCode?.Trim();
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return null;
        }

        return normalized switch
        {
            "zh-CN" => "zh",
            "fr-FR" => "fr",
            "en-US" => "en",
            "ja-JP" => "ja",
            "ko-KR" => "ko",
            "vi-VN" => "vi",
            _ when normalized.Contains('-', StringComparison.Ordinal) => normalized[..normalized.IndexOf('-')].ToLowerInvariant(),
            _ => normalized.ToLowerInvariant()
        };
    }

    private static string InferContentType(string outputFormat)
        => outputFormat.StartsWith("mp3_", StringComparison.OrdinalIgnoreCase)
            ? "audio/mpeg"
            : outputFormat.StartsWith("wav_", StringComparison.OrdinalIgnoreCase)
                ? "audio/wav"
                : outputFormat.StartsWith("pcm_", StringComparison.OrdinalIgnoreCase)
                    ? "audio/pcm"
                    : "application/octet-stream";

    private static async Task<byte[]> ReadAudioBytesAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        await using var buffer = new MemoryStream();
        await stream.CopyToAsync(buffer, cancellationToken);
        return buffer.ToArray();
    }

    private static async Task<string> ReadResponseBodyAsync(HttpResponseMessage response, CancellationToken cancellationToken)
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

    private static bool LooksLikeTextErrorPayload(string? contentType, byte[] content)
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

    private static string SanitizeText(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        var sanitized = text
            .Replace("\0", string.Empty, StringComparison.Ordinal)
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Trim();
        return string.IsNullOrWhiteSpace(sanitized)
            ? string.Empty
            : sanitized.Normalize();
    }

    private static IReadOnlyList<string> SplitNarrationIntoSegments(string text, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return Array.Empty<string>();
        }

        if (text.Length <= maxLength)
        {
            return new[] { text };
        }

        var segments = new List<string>();
        var start = 0;

        while (start < text.Length)
        {
            var end = Math.Min(start + maxLength, text.Length);
            if (end < text.Length)
            {
                var searchStart = Math.Min(text.Length, start + MinSegmentLength);
                for (var index = end; index > searchStart; index -= 1)
                {
                    if (char.IsWhiteSpace(text[index - 1]) || ".!?,;:".Contains(text[index - 1]))
                    {
                        end = index;
                        break;
                    }
                }
            }

            var segment = text[start..end].Trim();
            if (!string.IsNullOrWhiteSpace(segment))
            {
                segments.Add(segment);
            }

            start = end;
        }

        return segments;
    }

    private static TextToSpeechRequest CreateSegmentRequest(
        TextToSpeechRequest request,
        IReadOnlyList<string> segments,
        int index)
        => request with
        {
            Text = segments[index],
            PreviousText = FirstNonEmpty(request.PreviousText),
            NextText = FirstNonEmpty(request.NextText),
            SegmentIndex = index,
            TotalSegments = segments.Count
        };

    private static byte[] CombineAudioChunks(IReadOnlyList<byte[]> audioChunks)
    {
        if (audioChunks.Count == 0)
        {
            return Array.Empty<byte>();
        }

        var finalLength = audioChunks.Sum(chunk => chunk.Length);
        var finalAudio = new byte[finalLength];
        var offset = 0;

        foreach (var chunk in audioChunks)
        {
            Buffer.BlockCopy(chunk, 0, finalAudio, offset, chunk.Length);
            offset += chunk.Length;
        }

        return finalAudio;
    }

    private static double EstimateAudioDurationSeconds(string outputFormat, int audioBytes)
    {
        if (audioBytes <= 0)
        {
            return 0;
        }

        if (outputFormat.StartsWith("mp3_", StringComparison.OrdinalIgnoreCase))
        {
            var bitratePart = outputFormat.Split('_').LastOrDefault();
            if (int.TryParse(bitratePart, out var bitrateKbps) && bitrateKbps > 0)
            {
                return (audioBytes * 8d) / (bitrateKbps * 1000d);
            }
        }

        return 0;
    }

    private static string? FirstNonEmpty(params string?[] values)
        => values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))?.Trim();

    private sealed record ChunkAudioResult(byte[] Content, string ContentType, double EstimatedDurationSeconds);

    private void CacheAudio(string cacheKey, TextToSpeechResult result, TextToSpeechOptions options)
    {
        var cacheDurationMinutes = options.CacheDurationMinutes > 0
            ? options.CacheDurationMinutes
            : TextToSpeechOptions.DefaultCacheDurationMinutesValue;

        cache.Set(
            cacheKey,
            result,
            new MemoryCacheEntryOptions
            {
                SlidingExpiration = TimeSpan.FromMinutes(cacheDurationMinutes),
                Size = Math.Max(result.Content.Length, 1)
            });
    }

    private static string BuildCacheKey(
        string text,
        string languageCode,
        string voiceId,
        string modelId,
        string outputFormat)
    {
        var source = string.Join(
            '\u001f',
            text,
            languageCode.ToLowerInvariant(),
            voiceId.ToLowerInvariant(),
            modelId.ToLowerInvariant(),
            outputFormat.ToLowerInvariant());
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(source));
        return $"elevenlabs-tts:{Convert.ToHexString(hash)}";
    }

    private static string CreateHash(string value)
    {
        uint hash = 0;

        foreach (var character in value)
        {
            hash = (hash * 31) + character;
        }

        return hash.ToString("x8");
    }
}
