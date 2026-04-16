using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Maui.Devices;
using Microsoft.Maui.Storage;
using VinhKhanh.MobileApp.Helpers;

namespace VinhKhanh.MobileApp.Services;

public interface IElevenLabsTtsService
{
    Task<GeneratedSpeechResult> GenerateSpeechAsync(
        string text,
        string languageCode,
        string? voiceId = null,
        string? modelId = null,
        string? outputFormat = null,
        CancellationToken cancellationToken = default);

    void CleanupGeneratedSpeech(IReadOnlyList<GeneratedSpeechSegment>? segments);
}

public sealed record GeneratedSpeechSegment(
    string FilePath,
    string ContentType,
    long ContentLength);

public sealed record GeneratedSpeechResult(
    IReadOnlyList<GeneratedSpeechSegment> Segments,
    bool? ApiKeyPresent,
    string BaseUrl,
    string LanguageCode,
    string? VoiceId,
    string ModelId,
    string? OutputFormat);

public sealed class ElevenLabsTtsException : InvalidOperationException
{
    public ElevenLabsTtsException(
        string message,
        HttpStatusCode? statusCode = null,
        bool? apiKeyPresent = null,
        string? responseBody = null)
        : base(message)
    {
        StatusCode = statusCode;
        ApiKeyPresent = apiKeyPresent;
        ResponseBody = responseBody;
    }

    public ElevenLabsTtsException(
        string message,
        Exception innerException,
        HttpStatusCode? statusCode = null,
        bool? apiKeyPresent = null,
        string? responseBody = null)
        : base(message, innerException)
    {
        StatusCode = statusCode;
        ApiKeyPresent = apiKeyPresent;
        ResponseBody = responseBody;
    }

    public HttpStatusCode? StatusCode { get; }
    public bool? ApiKeyPresent { get; }
    public string? ResponseBody { get; }
}

public sealed class ElevenLabsTtsService : IElevenLabsTtsService
{
    private const string AppSettingsFileName = "appsettings.json";
    private const int MaxCharsPerSegment = 180;
    private const string DefaultModelId = "eleven_flash_v2_5";

    private readonly JsonSerializerOptions _jsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };
    private readonly ILogger<ElevenLabsTtsService> _logger;
    private readonly HttpClient _httpClient;
    private MobileRuntimeAppSettings? _runtimeSettings;

    public ElevenLabsTtsService(ILogger<ElevenLabsTtsService> logger)
    {
        _logger = logger;
        _httpClient = new HttpClient(new HttpClientHandler
        {
            ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator
        })
        {
            Timeout = TimeSpan.FromSeconds(20)
        };
        _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Linux; Android 12; VinhKhanhMobile)");
        _httpClient.DefaultRequestHeaders.Accept.ParseAdd("audio/mpeg, audio/*;q=0.9, application/json;q=0.8, text/plain;q=0.7");
    }

    public async Task<GeneratedSpeechResult> GenerateSpeechAsync(
        string text,
        string languageCode,
        string? voiceId = null,
        string? modelId = null,
        string? outputFormat = null,
        CancellationToken cancellationToken = default)
    {
        var normalizedText = text?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(normalizedText))
        {
            throw new ElevenLabsTtsException("Khong co noi dung de gui den ElevenLabs TTS.");
        }

        var normalizedLanguageCode = AppLanguage.NormalizeCode(languageCode);
        var resolvedBaseUrl = await GetTextToSpeechBaseUrlAsync();
        if (string.IsNullOrWhiteSpace(resolvedBaseUrl))
        {
            throw new ElevenLabsTtsException("Mobile app chua co ApiBaseUrl de goi TTS proxy.");
        }

        WarnIfLoopbackBaseUrlOnPhysicalDevice(resolvedBaseUrl);

        var resolvedModelId = string.IsNullOrWhiteSpace(modelId) ? DefaultModelId : modelId.Trim();
        var urls = BuildTextToSpeechAudioUrls(
            normalizedText,
            normalizedLanguageCode,
            resolvedBaseUrl,
            voiceId,
            resolvedModelId,
            outputFormat);

        if (urls.Count == 0)
        {
            throw new ElevenLabsTtsException("Khong tao duoc request TTS hop le tu noi dung thuyet minh.");
        }

        _logger.LogInformation(
            "Preparing ElevenLabs TTS proxy request. language={LanguageCode}; voiceId={VoiceId}; modelId={ModelId}; outputFormat={OutputFormat}; segments={SegmentCount}; baseUrl={BaseUrl}; textLength={TextLength}",
            normalizedLanguageCode,
            string.IsNullOrWhiteSpace(voiceId) ? "default" : voiceId.Trim(),
            resolvedModelId,
            string.IsNullOrWhiteSpace(outputFormat) ? "default" : outputFormat.Trim(),
            urls.Count,
            resolvedBaseUrl,
            normalizedText.Length);

        var segments = new List<GeneratedSpeechSegment>();
        bool? apiKeyPresent = null;

        try
        {
            foreach (var url in urls)
            {
                var download = await DownloadSegmentAsync(url, cancellationToken);
                apiKeyPresent ??= download.ApiKeyPresent;
                segments.Add(download.Segment);
            }

            return new GeneratedSpeechResult(
                segments,
                apiKeyPresent,
                resolvedBaseUrl,
                normalizedLanguageCode,
                string.IsNullOrWhiteSpace(voiceId) ? null : voiceId.Trim(),
                resolvedModelId,
                string.IsNullOrWhiteSpace(outputFormat) ? null : outputFormat.Trim());
        }
        catch
        {
            CleanupGeneratedSpeech(segments);
            throw;
        }
    }

    public void CleanupGeneratedSpeech(IReadOnlyList<GeneratedSpeechSegment>? segments)
    {
        if (segments is null)
        {
            return;
        }

        foreach (var segment in segments)
        {
            if (string.IsNullOrWhiteSpace(segment.FilePath) || !File.Exists(segment.FilePath))
            {
                continue;
            }

            try
            {
                File.Delete(segment.FilePath);
            }
            catch (Exception exception)
            {
                _logger.LogDebug(exception, "Unable to delete temporary TTS audio file {FilePath}.", segment.FilePath);
            }
        }
    }

    private async Task<DownloadedSegment> DownloadSegmentAsync(string requestUrl, CancellationToken cancellationToken)
    {
        using var response = await _httpClient.GetAsync(requestUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        var apiKeyPresent = TryReadApiKeyPresence(response);
        if (!response.IsSuccessStatusCode)
        {
            var responseBody = await SafeReadBodyAsync(response, cancellationToken);
            _logger.LogWarning(
                "ElevenLabs TTS proxy request failed. status={StatusCode}; apiKeyPresent={ApiKeyPresent}; response={ResponseBody}",
                response.StatusCode,
                apiKeyPresent,
                responseBody);
            throw new ElevenLabsTtsException(
                BuildFailureMessage(response.StatusCode, responseBody),
                response.StatusCode,
                apiKeyPresent,
                responseBody);
        }

        var contentType = response.Content.Headers.ContentType?.MediaType ?? "audio/mpeg";
        var bytes = await response.Content.ReadAsByteArrayAsync(cancellationToken);
        if (bytes.Length == 0)
        {
            _logger.LogWarning(
                "ElevenLabs TTS proxy returned empty audio. apiKeyPresent={ApiKeyPresent}; contentType={ContentType}",
                apiKeyPresent,
                contentType);
            throw new ElevenLabsTtsException(
                "TTS proxy khong tra ve du lieu audio.",
                HttpStatusCode.BadGateway,
                apiKeyPresent);
        }

        var tempFilePath = Path.Combine(
            FileSystem.CacheDirectory,
            $"elevenlabs-tts-{Guid.NewGuid():N}{InferFileExtension(contentType)}");
        await File.WriteAllBytesAsync(tempFilePath, bytes, cancellationToken);

        _logger.LogInformation(
            "ElevenLabs TTS proxy returned audio. apiKeyPresent={ApiKeyPresent}; contentType={ContentType}; contentLength={ContentLength}; filePath={FilePath}",
            apiKeyPresent,
            contentType,
            bytes.LongLength,
            tempFilePath);

        return new DownloadedSegment(
            new GeneratedSpeechSegment(tempFilePath, contentType, bytes.LongLength),
            apiKeyPresent);
    }

    private async Task<string?> GetTextToSpeechBaseUrlAsync()
    {
        var runtimeSettings = await LoadRuntimeSettingsAsync();
        var nextBaseUrl = EnsureTrailingSlash(ResolveApiBaseUrl(runtimeSettings));
        return string.IsNullOrWhiteSpace(nextBaseUrl)
            ? null
            : nextBaseUrl;
    }

    private async Task<MobileRuntimeAppSettings> LoadRuntimeSettingsAsync()
    {
        if (_runtimeSettings is not null)
        {
            return _runtimeSettings;
        }

        try
        {
            await using var stream = await FileSystem.OpenAppPackageFileAsync(AppSettingsFileName);
            using var reader = new StreamReader(stream);
            var content = await reader.ReadToEndAsync();
            _runtimeSettings = JsonSerializer.Deserialize<MobileRuntimeAppSettings>(content, _jsonOptions) ?? new MobileRuntimeAppSettings();
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "Unable to load mobile appsettings.json for ElevenLabs TTS proxy.");
            _runtimeSettings = new MobileRuntimeAppSettings();
        }

        _logger.LogInformation(
            "Loaded ElevenLabs TTS mobile runtime settings. apiBaseUrl={ApiBaseUrl}; resolvedApiBaseUrl={ResolvedApiBaseUrl}",
            _runtimeSettings.ApiBaseUrl ?? "(empty)",
            ResolveApiBaseUrl(_runtimeSettings));

        return _runtimeSettings;
    }

    private static IReadOnlyList<string> BuildTextToSpeechAudioUrls(
        string text,
        string languageCode,
        string apiBaseUrl,
        string? voiceId,
        string modelId,
        string? outputFormat)
    {
        if (string.IsNullOrWhiteSpace(text) ||
            string.IsNullOrWhiteSpace(languageCode) ||
            string.IsNullOrWhiteSpace(apiBaseUrl))
        {
            return Array.Empty<string>();
        }

        var normalizedLanguageCode = AppLanguage.NormalizeCode(languageCode);
        var chunks = SplitNarrationIntoChunks(text.Trim(), MaxCharsPerSegment);
        if (chunks.Count == 0)
        {
            return Array.Empty<string>();
        }

        var baseUri = new Uri(apiBaseUrl, UriKind.Absolute);
        return chunks
            .Select((chunk, index) =>
            {
                var query = new Dictionary<string, string>
                {
                    ["languageCode"] = normalizedLanguageCode,
                    ["text"] = chunk,
                    ["total"] = chunks.Count.ToString(),
                    ["idx"] = index.ToString(),
                    ["model_id"] = modelId
                };

                if (!string.IsNullOrWhiteSpace(voiceId))
                {
                    query["voice_id"] = voiceId.Trim();
                }

                if (!string.IsNullOrWhiteSpace(outputFormat))
                {
                    query["output_format"] = outputFormat.Trim();
                }

                var relativePath =
                    "api/v1/tts?" +
                    string.Join("&", query.Select(item => $"{item.Key}={Uri.EscapeDataString(item.Value)}"));

                return new Uri(baseUri, relativePath).ToString();
            })
            .ToArray();
    }

    private static IReadOnlyList<string> SplitNarrationIntoChunks(string text, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return Array.Empty<string>();
        }

        var chunks = new List<string>();
        var start = 0;

        while (start < text.Length)
        {
            var end = Math.Min(start + maxLength, text.Length);
            if (end < text.Length)
            {
                var searchStart = start + (int)(maxLength * 0.6);
                for (var index = end; index > searchStart; index -= 1)
                {
                    if (char.IsWhiteSpace(text[index - 1]) || ".!?,;:".Contains(text[index - 1]))
                    {
                        end = index;
                        break;
                    }
                }
            }

            var chunk = text[start..end].Trim();
            if (!string.IsNullOrWhiteSpace(chunk))
            {
                chunks.Add(chunk);
            }

            start = end;
        }

        return chunks;
    }

    private static bool? TryReadApiKeyPresence(HttpResponseMessage response)
    {
        if (!response.Headers.TryGetValues("X-TTS-Config-Api-Key-Present", out var values))
        {
            return null;
        }

        var rawValue = values.FirstOrDefault();
        return bool.TryParse(rawValue, out var parsed)
            ? parsed
            : null;
    }

    private static async Task<string> SafeReadBodyAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        try
        {
            var content = await response.Content.ReadAsStringAsync(cancellationToken);
            if (string.IsNullOrWhiteSpace(content))
            {
                return string.Empty;
            }

            var extractedMessage = TryExtractApiMessage(content);
            return extractedMessage.Length <= 300 ? extractedMessage : extractedMessage[..300];
        }
        catch
        {
            return string.Empty;
        }
    }

    private static string TryExtractApiMessage(string body)
    {
        try
        {
            using var document = JsonDocument.Parse(body);
            if (document.RootElement.ValueKind == JsonValueKind.Object &&
                document.RootElement.TryGetProperty("message", out var messageElement) &&
                messageElement.ValueKind == JsonValueKind.String)
            {
                return messageElement.GetString()?.Trim() ?? body.Trim();
            }
        }
        catch
        {
        }

        return body.Trim();
    }

    private static string BuildFailureMessage(HttpStatusCode statusCode, string? responseBody)
    {
        var baseMessage = statusCode switch
        {
            HttpStatusCode.Unauthorized => "TTS proxy tra ve 401. API key ElevenLabs co the sai hoac chua duoc nap vao backend.",
            HttpStatusCode.Forbidden => "TTS proxy tra ve 403. API key hien tai khong duoc phep dung voice hoac model da chon.",
            HttpStatusCode.NotFound => "TTS proxy tra ve 404. VoiceId, endpoint, hoac tai nguyen TTS khong ton tai.",
            HttpStatusCode.RequestTimeout => "TTS proxy bi timeout khi goi ElevenLabs.",
            (HttpStatusCode)429 => "TTS proxy tra ve 429. Da vuot quota hoac rate limit cua ElevenLabs.",
            HttpStatusCode.ServiceUnavailable => "Backend TTS chua duoc cau hinh API key ElevenLabs.",
            _ => $"TTS proxy loi voi HTTP {(int)statusCode}."
        };

        return string.IsNullOrWhiteSpace(responseBody)
            ? baseMessage
            : $"{baseMessage} {responseBody}";
    }

    private static string InferFileExtension(string contentType) =>
        contentType switch
        {
            "audio/mpeg" => ".mp3",
            "audio/mp3" => ".mp3",
            "audio/wav" => ".wav",
            "audio/x-wav" => ".wav",
            "audio/ogg" => ".ogg",
            _ => ".mp3"
        };

    private static string ResolveApiBaseUrl(MobileRuntimeAppSettings runtimeSettings)
        => MobileApiEndpointHelper.ResolveBaseUrl(runtimeSettings.ApiBaseUrl, runtimeSettings.PlatformApiBaseUrls);

    private static string EnsureTrailingSlash(string baseUrl)
        => MobileApiEndpointHelper.EnsureTrailingSlash(baseUrl);

    private void WarnIfLoopbackBaseUrlOnPhysicalDevice(string baseUrl)
    {
        if (DeviceInfo.Current.Platform != DevicePlatform.Android ||
            DeviceInfo.Current.DeviceType == DeviceType.Virtual ||
            !Uri.TryCreate(baseUrl, UriKind.Absolute, out var parsed))
        {
            return;
        }

        if (string.Equals(parsed.Host, "localhost", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(parsed.Host, "127.0.0.1", StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogWarning(
                "ApiBaseUrl {ApiBaseUrl} is using loopback on a physical Android device. Use the computer LAN IP instead of localhost.",
                baseUrl);
        }
    }

    private sealed record DownloadedSegment(
        GeneratedSpeechSegment Segment,
        bool? ApiKeyPresent);

    private sealed class MobileRuntimeAppSettings
    {
        public string? ApiBaseUrl { get; set; }
        public Dictionary<string, string> PlatformApiBaseUrls { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    }
}
