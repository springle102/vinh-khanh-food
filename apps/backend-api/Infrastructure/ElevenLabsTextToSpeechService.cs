using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
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
    public async Task<TextToSpeechResult> GenerateAudioAsync(
        TextToSpeechRequest request,
        CancellationToken cancellationToken)
    {
        var normalizedText = request.Text?.Trim() ?? string.Empty;
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
        var normalizedLanguageCode = NormalizeLanguageCode(request.LanguageCode);
        var cacheKey = BuildCacheKey(
            normalizedText,
            normalizedLanguageCode ?? request.LanguageCode?.Trim() ?? string.Empty,
            voiceId,
            modelId,
            outputFormat);

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

        var payload = new Dictionary<string, object>
        {
            ["text"] = normalizedText,
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

        using var message = new HttpRequestMessage(HttpMethod.Post, requestUri)
        {
            Content = JsonContent.Create(payload)
        };
        message.Headers.TryAddWithoutValidation("xi-api-key", apiKey);

        try
        {
            using var response = await httpClient.SendAsync(
                message,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                var responseSnippet = await SafeReadBodyAsync(response, cancellationToken);
                logger.LogWarning(
                    "ElevenLabs TTS failed. Status={StatusCode}; language={LanguageCode}; voiceId={VoiceId}; modelId={ModelId}; segment={SegmentIndex}/{TotalSegments}; textLength={TextLength}; textHash={TextHash}; response={ResponseSnippet}",
                    response.StatusCode,
                    normalizedLanguageCode ?? request.LanguageCode?.Trim(),
                    voiceId,
                    modelId,
                    request.SegmentIndex,
                    request.TotalSegments,
                    normalizedText.Length,
                    CreateHash(normalizedText),
                    responseSnippet);
                throw new TextToSpeechGenerationException("Khong the tao audio tu ElevenLabs Text-to-Speech.");
            }

            var contentType = response.Content.Headers.ContentType?.ToString();
            var content = await response.Content.ReadAsByteArrayAsync(cancellationToken);
            if (content.Length == 0)
            {
                logger.LogWarning(
                    "ElevenLabs TTS returned an empty response. language={LanguageCode}; voiceId={VoiceId}; modelId={ModelId}; segment={SegmentIndex}/{TotalSegments}; textHash={TextHash}",
                    normalizedLanguageCode ?? request.LanguageCode?.Trim(),
                    voiceId,
                    modelId,
                    request.SegmentIndex,
                    request.TotalSegments,
                    CreateHash(normalizedText));
                throw new TextToSpeechGenerationException("ElevenLabs Text-to-Speech khong tra ve du lieu audio.");
            }

            var result = new TextToSpeechResult(
                content,
                string.IsNullOrWhiteSpace(contentType) ? InferContentType(outputFormat) : contentType,
                "elevenlabs",
                voiceId,
                modelId,
                outputFormat);
            CacheAudio(cacheKey, result, options);
            return result;
        }
        catch (OperationCanceledException exception) when (!cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning(
                exception,
                "ElevenLabs TTS timed out. language={LanguageCode}; voiceId={VoiceId}; modelId={ModelId}; segment={SegmentIndex}/{TotalSegments}; textHash={TextHash}",
                normalizedLanguageCode ?? request.LanguageCode?.Trim(),
                voiceId,
                modelId,
                request.SegmentIndex,
                request.TotalSegments,
                CreateHash(normalizedText));
            throw new TextToSpeechGenerationException("Yeu cau ElevenLabs Text-to-Speech bi timeout.", exception);
        }
        catch (HttpRequestException exception)
        {
            logger.LogWarning(
                exception,
                "Unable to reach ElevenLabs TTS. language={LanguageCode}; voiceId={VoiceId}; modelId={ModelId}; segment={SegmentIndex}/{TotalSegments}; textHash={TextHash}",
                normalizedLanguageCode ?? request.LanguageCode?.Trim(),
                voiceId,
                modelId,
                request.SegmentIndex,
                request.TotalSegments,
                CreateHash(normalizedText));
            throw new TextToSpeechGenerationException("Khong the ket noi den ElevenLabs Text-to-Speech.", exception);
        }
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

    private static async Task<string> SafeReadBodyAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        try
        {
            var content = await response.Content.ReadAsStringAsync(cancellationToken);
            if (string.IsNullOrWhiteSpace(content))
            {
                return string.Empty;
            }

            var decoded = WebUtility.HtmlDecode(content).Trim();
            return decoded.Length <= 300 ? decoded : decoded[..300];
        }
        catch
        {
            return string.Empty;
        }
    }

    private static string? FirstNonEmpty(params string?[] values)
        => values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))?.Trim();

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
