using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.Media;
using Microsoft.Maui.Storage;
using Plugin.Maui.Audio;
using VinhKhanh.MobileApp.Helpers;
using VinhKhanh.MobileApp.Models;

namespace VinhKhanh.MobileApp.Services;

public interface IPoiNarrationService
{
    bool IsPlaying { get; }
    Task PlayAsync(PoiExperienceDetail detail, string languageCode, CancellationToken cancellationToken = default);
    Task StopAsync();
}

public interface IPoiTourStoreService
{
    Task<bool> IsSavedAsync(string poiId);
    Task<bool> ToggleSavedAsync(string poiId);
}

public sealed class PoiNarrationService : IPoiNarrationService, IAudioPlayerService
{
    private const string AppSettingsFileName = "appsettings.json";
    private static readonly TimeSpan RemoteAudioRequestTimeout = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan TextToSpeechProxyRequestTimeout = TimeSpan.FromSeconds(60);

    private readonly SemaphoreSlim _stateLock = new(1, 1);
    private readonly JsonSerializerOptions _jsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };
    private readonly IAudioManager _audioManager;
    private readonly IFoodStreetDataService _dataService;
    private readonly ILogger<PoiNarrationService> _logger;
    private readonly HttpClient _mediaClient;
    private HttpClient? _apiClient;
    private string? _resolvedBaseUrl;
    private MobileRuntimeAppSettings? _runtimeSettings;
    private CancellationTokenSource? _playbackCancellationSource;
    private AsyncAudioPlayer? _audioPlayer;
    private MemoryStream? _audioBuffer;
    private bool _isPlaybackActive;
    private long _playbackId;

    public PoiNarrationService(
        IAudioManager audioManager,
        IFoodStreetDataService dataService,
        ILogger<PoiNarrationService> logger)
    {
        _audioManager = audioManager;
        _dataService = dataService;
        _logger = logger;
        _mediaClient = new HttpClient(new HttpClientHandler
        {
            ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator
        })
        {
            Timeout = Timeout.InfiniteTimeSpan
        };
        _mediaClient.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Linux; Android 12; VinhKhanhMobile)");
    }

    public bool IsPlaying => _isPlaybackActive || (_audioPlayer?.IsPlaying ?? false);

    public Task PlayPoiNarrationAsync(PoiExperienceDetail detail, string languageCode, CancellationToken cancellationToken = default)
        => PlayAsync(detail, languageCode, cancellationToken);

    public async Task PlayAsync(PoiExperienceDetail detail, string languageCode, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(detail);

        var requestedLanguageCode = AppLanguage.NormalizeCode(languageCode);
        var session = await BeginPlaybackSessionAsync(cancellationToken);
        try
        {
            var resolvedNarration = await TryResolveNarrationAsync(
                detail.Id,
                requestedLanguageCode,
                session.Token);
            var effectiveLanguageCode = AppLanguage.NormalizeCode(resolvedNarration?.EffectiveLanguageCode ?? requestedLanguageCode);
            var narrationText = FirstNonEmpty(
                resolvedNarration?.TranslatedText,
                resolvedNarration?.TtsInputText,
                GetRequestedLocalizedText(detail.Description, requestedLanguageCode),
                GetRequestedLocalizedText(detail.Summary, requestedLanguageCode));
            var sanitizedNarrationText = SanitizeNarrationText(narrationText);

            var audioUrl = FirstNonEmpty(
                CanUseResolvedAudioGuide(resolvedNarration)
                    ? resolvedNarration?.AudioGuide?.AudioUrl
                    : null,
                GetRequestedLocalizedText(detail.AudioUrls, requestedLanguageCode));
            if (IsPlaceholderAudioUrl(audioUrl))
            {
                audioUrl = string.Empty;
            }

            if (!string.IsNullOrWhiteSpace(audioUrl))
            {
                var remotePlayback = await TryPlayRemoteAudioAsync(audioUrl, session.PlaybackId, session.Token);
                if (remotePlayback.Success)
                {
                    await _dataService.TrackAudioPlayAsync(
                        detail.Id,
                        effectiveLanguageCode,
                        "remote_audio",
                        ToTelemetryDurationSeconds(remotePlayback.DurationSeconds));
                    return;
                }
            }

            if (!string.IsNullOrWhiteSpace(sanitizedNarrationText))
            {
                var backendTtsPlayback = await TryPlayBackendTextToSpeechAsync(
                    sanitizedNarrationText,
                    effectiveLanguageCode,
                    session.PlaybackId,
                    session.Token);
                if (backendTtsPlayback.Success)
                {
                    await _dataService.TrackAudioPlayAsync(
                        detail.Id,
                        effectiveLanguageCode,
                        "backend_tts",
                        ToTelemetryDurationSeconds(backendTtsPlayback.DurationSeconds));
                    return;
                }
            }

            if (string.IsNullOrWhiteSpace(sanitizedNarrationText))
            {
                return;
            }

            await SpeakWithDeviceTextToSpeechAsync(
                sanitizedNarrationText,
                resolvedNarration?.TtsLocale,
                session.Token);
            await _dataService.TrackAudioPlayAsync(detail.Id, effectiveLanguageCode, "device_tts");
        }
        catch (OperationCanceledException) when (session.Token.IsCancellationRequested)
        {
        }
        finally
        {
            await _stateLock.WaitAsync();
            try
            {
                _isPlaybackActive = false;
            }
            finally
            {
                _stateLock.Release();
            }
        }
    }

    public async Task StopAsync()
    {
        await _stateLock.WaitAsync();
        try
        {
            _playbackId++;
            ReleasePlaybackStateLocked();
        }
        finally
        {
            _stateLock.Release();
        }
    }

    private static bool CanUseResolvedAudioGuide(
        PoiNarrationResponseDto? resolvedNarration) =>
        resolvedNarration?.AudioGuide is not null &&
        !string.Equals(resolvedNarration.TranslationStatus, "auto_translated", StringComparison.OrdinalIgnoreCase) &&
        string.Equals(resolvedNarration.AudioGuide.SourceType, "uploaded", StringComparison.OrdinalIgnoreCase) &&
        string.Equals(resolvedNarration.AudioGuide.Status, "ready", StringComparison.OrdinalIgnoreCase) &&
        HasPlayableRemoteAudioUrl(resolvedNarration.AudioGuide.AudioUrl);

    private static string GetRequestedLocalizedText(LocalizedTextSet source, string languageCode)
    {
        foreach (var candidate in GetRequestedLanguageCandidates(languageCode))
        {
            if (source.Values.TryGetValue(candidate, out var value) &&
                LocalizationFallbackPolicy.IsUsableTextForLanguage(value, candidate))
            {
                return value.Trim();
            }
        }

        return string.Empty;
    }

    private static IEnumerable<string> GetRequestedLanguageCandidates(string languageCode)
    {
        return AppLanguage.GetCandidateCodes(languageCode);
    }

    private async Task<PlaybackSession> BeginPlaybackSessionAsync(CancellationToken cancellationToken)
    {
        await _stateLock.WaitAsync(cancellationToken);
        try
        {
            ReleasePlaybackStateLocked();
            _playbackCancellationSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            _isPlaybackActive = true;
            _playbackId++;
            return new PlaybackSession(_playbackId, _playbackCancellationSource.Token);
        }
        finally
        {
            _stateLock.Release();
        }
    }

    private async Task<AudioPlaybackResult> TryPlayRemoteAudioAsync(
        string audioUrl,
        long playbackId,
        CancellationToken cancellationToken)
    {
        if (!Uri.TryCreate(audioUrl, UriKind.Absolute, out var audioUri))
        {
            return new AudioPlaybackResult(false, null);
        }

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, audioUri);
            var audioPayload = await DownloadAudioPayloadAsync(
                request,
                audioUri.ToString(),
                RemoteAudioRequestTimeout,
                cancellationToken);
            if (audioPayload is null)
            {
                return new AudioPlaybackResult(false, null);
            }

            var durationSeconds = await PlayAudioBufferAsync(
                audioPayload.Content,
                playbackId,
                "remote_audio",
                cancellationToken);
            return new AudioPlaybackResult(true, durationSeconds);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return new AudioPlaybackResult(false, null);
        }
        catch (Exception exception)
        {
            _logger.LogWarning(
                exception,
                "Unable to play remote narration audio. audioUrl={AudioUrl}",
                audioUrl);
            return new AudioPlaybackResult(false, null);
        }
    }

    private async Task<AudioPlaybackResult> TryPlayBackendTextToSpeechAsync(
        string narrationText,
        string languageCode,
        long playbackId,
        CancellationToken cancellationToken)
    {
        var apiBaseUrl = await GetTextToSpeechBaseUrlAsync();
        if (string.IsNullOrWhiteSpace(apiBaseUrl) ||
            string.IsNullOrWhiteSpace(narrationText) ||
            string.IsNullOrWhiteSpace(languageCode))
        {
            return new AudioPlaybackResult(false, null);
        }

        try
        {
            var requestUri = new Uri(new Uri(apiBaseUrl, UriKind.Absolute), "api/v1/tts");
            using var request = new HttpRequestMessage(HttpMethod.Post, requestUri)
            {
                Content = JsonContent.Create(new TextToSpeechProxyRequestDto
                {
                    Text = narrationText,
                    LanguageCode = AppLanguage.NormalizeCode(languageCode)
                })
            };

            _logger.LogInformation(
                "Requesting backend TTS audio. endpoint={Endpoint}; languageCode={LanguageCode}; textLength={TextLength}",
                requestUri,
                languageCode,
                narrationText.Length);

            var audioPayload = await DownloadAudioPayloadAsync(
                request,
                requestUri.ToString(),
                TextToSpeechProxyRequestTimeout,
                cancellationToken);
            if (audioPayload is null)
            {
                return new AudioPlaybackResult(false, null);
            }

            var durationSeconds = await PlayAudioBufferAsync(
                audioPayload.Content,
                playbackId,
                "backend_tts",
                cancellationToken);
            return new AudioPlaybackResult(true, durationSeconds);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return new AudioPlaybackResult(false, null);
        }
        catch (Exception exception)
        {
            _logger.LogWarning(
                exception,
                "Unable to play backend TTS narration. languageCode={LanguageCode}; textLength={TextLength}",
                languageCode,
                narrationText.Length);
            return new AudioPlaybackResult(false, null);
        }
    }

    private async Task<PoiNarrationResponseDto?> TryResolveNarrationAsync(
        string poiId,
        string languageCode,
        CancellationToken cancellationToken)
    {
        try
        {
            var client = await GetApiClientAsync();
            if (client is null)
            {
                return null;
            }

            var query = new Dictionary<string, string>
            {
                ["languageCode"] = languageCode
            };

            var relativeUrl =
                $"api/v1/pois/{Uri.EscapeDataString(poiId)}/narration?" +
                string.Join("&", query.Select(item => $"{item.Key}={Uri.EscapeDataString(item.Value)}"));
            var envelope = await client.GetFromJsonAsync<ApiEnvelope<PoiNarrationResponseDto>>(relativeUrl, _jsonOptions, cancellationToken);
            if (envelope?.Success != true || envelope.Data is null)
            {
                return null;
            }

            if (envelope.Data.AudioGuide is not null)
            {
                envelope.Data.AudioGuide.AudioUrl = NormalizeAudioUrl(envelope.Data.AudioGuide.AudioUrl, client.BaseAddress);
            }

            return envelope.Data;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            return null;
        }
    }

    private async Task<HttpClient?> GetApiClientAsync()
    {
        var runtimeSettings = await LoadRuntimeSettingsAsync();
        var nextBaseUrl = EnsureTrailingSlash(ResolveApiBaseUrl(runtimeSettings));
        if (string.IsNullOrWhiteSpace(nextBaseUrl))
        {
            return null;
        }

        if (_apiClient is not null &&
            string.Equals(_resolvedBaseUrl, nextBaseUrl, StringComparison.OrdinalIgnoreCase))
        {
            return _apiClient;
        }

        _apiClient?.Dispose();
        _apiClient = new HttpClient(new HttpClientHandler
        {
            ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator
        })
        {
            BaseAddress = new Uri(nextBaseUrl, UriKind.Absolute),
            Timeout = TimeSpan.FromSeconds(6)
        };
        _resolvedBaseUrl = nextBaseUrl;
        return _apiClient;
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
        catch
        {
            _runtimeSettings = new MobileRuntimeAppSettings();
        }

        return _runtimeSettings;
    }

    private static string ResolveApiBaseUrl(MobileRuntimeAppSettings runtimeSettings)
        => MobileApiEndpointHelper.ResolveBaseUrl(runtimeSettings.ApiBaseUrl, runtimeSettings.PlatformApiBaseUrls);

    private static string EnsureTrailingSlash(string baseUrl)
        => MobileApiEndpointHelper.EnsureTrailingSlash(baseUrl);

    private void ReleasePlaybackStateLocked()
    {
        _playbackCancellationSource?.Cancel();
        _playbackCancellationSource?.Dispose();
        _playbackCancellationSource = null;
        _isPlaybackActive = false;
        DisposeCurrentAudioResourcesLocked();
    }

    private void DisposeCurrentAudioResourcesLocked()
    {
        _audioPlayer?.Stop();
        _audioPlayer?.Dispose();
        _audioPlayer = null;

        _audioBuffer?.Dispose();
        _audioBuffer = null;
    }

    private static string FirstNonEmpty(params string?[] values)
        => values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))?.Trim() ?? string.Empty;

    private static bool HasPlayableRemoteAudioUrl(string? value) =>
        !string.IsNullOrWhiteSpace(value) && !IsPlaceholderAudioUrl(value);

    private static string NormalizeAudioUrl(string? value, Uri? baseAddress)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var normalized = value.Trim();
        if (Uri.TryCreate(normalized, UriKind.Absolute, out var absolute))
        {
            return absolute.ToString();
        }

        return baseAddress is null
            ? normalized
            : new Uri(baseAddress, normalized).ToString();
    }

    private static bool IsPlaceholderAudioUrl(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        return Uri.TryCreate(value, UriKind.Absolute, out var parsed) &&
               parsed.Host.Contains("example.com", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsTextToSpeechProxyUrl(string audioUrl)
    {
        if (!Uri.TryCreate(audioUrl, UriKind.Absolute, out var parsed))
        {
            return false;
        }

        return parsed.AbsolutePath.EndsWith("/api/v1/tts", StringComparison.OrdinalIgnoreCase);
    }

    private async Task<DownloadedAudioPayload?> DownloadAudioPayloadAsync(
        HttpRequestMessage request,
        string requestDescription,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutSource.CancelAfter(timeout);
        var requestCancellationToken = timeoutSource.Token;

        using var response = await _mediaClient.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            requestCancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            var responseBody = await SafeReadBodyAsync(response, requestCancellationToken);
            _logger.LogWarning(
                "Narration audio request failed. request={Request}; statusCode={StatusCode}; contentType={ContentType}; responseBody={ResponseBody}",
                requestDescription,
                (int)response.StatusCode,
                response.Content.Headers.ContentType?.ToString(),
                responseBody);
            return null;
        }

        var contentType = response.Content.Headers.ContentType?.ToString();
        await using var networkStream = await response.Content.ReadAsStreamAsync(requestCancellationToken);
        await using var buffer = new MemoryStream();
        await networkStream.CopyToAsync(buffer, requestCancellationToken);
        var content = buffer.ToArray();

        if (LooksLikeTextPayload(contentType, content))
        {
            var responseBody = DecodeTextPayload(content);
            _logger.LogWarning(
                "Narration audio request returned a non-audio payload. request={Request}; statusCode={StatusCode}; contentType={ContentType}; responseBody={ResponseBody}",
                requestDescription,
                (int)response.StatusCode,
                contentType,
                responseBody);
            return null;
        }

        if (content.Length == 0)
        {
            _logger.LogWarning(
                "Narration audio request returned 0 bytes. request={Request}; statusCode={StatusCode}; contentType={ContentType}",
                requestDescription,
                (int)response.StatusCode,
                contentType);
            return null;
        }

        _logger.LogInformation(
            "Narration audio downloaded. request={Request}; statusCode={StatusCode}; contentType={ContentType}; audioBytes={AudioBytes}",
            requestDescription,
            (int)response.StatusCode,
            string.IsNullOrWhiteSpace(contentType) ? "unknown" : contentType,
            content.Length);

        return new DownloadedAudioPayload(content, contentType);
    }

    private async Task<double?> PlayAudioBufferAsync(
        byte[] audioBytes,
        long playbackId,
        string source,
        CancellationToken cancellationToken)
    {
        var buffer = new MemoryStream(audioBytes, writable: false);
        AsyncAudioPlayer player;
        double durationSeconds;

        await _stateLock.WaitAsync(cancellationToken);
        try
        {
            if (playbackId != _playbackId || cancellationToken.IsCancellationRequested)
            {
                buffer.Dispose();
                throw new OperationCanceledException(cancellationToken);
            }

            DisposeCurrentAudioResourcesLocked();
            _audioBuffer = buffer;
            _audioPlayer = _audioManager.CreateAsyncPlayer(_audioBuffer);
            player = _audioPlayer;
            durationSeconds = player.Duration;
        }
        finally
        {
            _stateLock.Release();
        }

        _logger.LogInformation(
            "Starting narration playback. source={Source}; playbackId={PlaybackId}; audioBytes={AudioBytes}; durationSeconds={DurationSeconds}",
            source,
            playbackId,
            audioBytes.Length,
            durationSeconds.ToString("0.00"));

        try
        {
            await player.PlayAsync(cancellationToken);
            _logger.LogInformation(
                "Narration playback completed. source={Source}; playbackId={PlaybackId}; durationSeconds={DurationSeconds}",
                source,
                playbackId,
                durationSeconds.ToString("0.00"));
            return durationSeconds > 0 ? durationSeconds : null;
        }
        finally
        {
            await _stateLock.WaitAsync();
            try
            {
                if (ReferenceEquals(_audioPlayer, player))
                {
                    DisposeCurrentAudioResourcesLocked();
                }
            }
            finally
            {
                _stateLock.Release();
            }
        }
    }

    private static async Task SpeakWithDeviceTextToSpeechAsync(
        string narrationText,
        string? preferredLocale,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(narrationText))
        {
            return;
        }

        var speechOptions = await BuildSpeechOptionsAsync(preferredLocale);
        if (speechOptions is null)
        {
            await TextToSpeech.Default.SpeakAsync(narrationText, null, cancellationToken);
            return;
        }

        await TextToSpeech.Default.SpeakAsync(narrationText, speechOptions, cancellationToken);
    }

    private static async Task<SpeechOptions?> BuildSpeechOptionsAsync(string? preferredLocale)
    {
        if (string.IsNullOrWhiteSpace(preferredLocale))
        {
            return null;
        }

        var normalizedPreferredLocale = preferredLocale.Trim();
        var locales = await TextToSpeech.Default.GetLocalesAsync();
        var matchedLocale = locales?.FirstOrDefault(locale =>
            string.Equals(locale.Language, normalizedPreferredLocale, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(locale.Name, normalizedPreferredLocale, StringComparison.OrdinalIgnoreCase) ||
            string.Equals($"{locale.Language}-{locale.Country}", normalizedPreferredLocale, StringComparison.OrdinalIgnoreCase));

        return matchedLocale is null
            ? null
            : new SpeechOptions
            {
                Locale = matchedLocale
            };
    }

    private static async Task<string> SafeReadBodyAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        try
        {
            var content = await response.Content.ReadAsStringAsync(cancellationToken);
            return string.IsNullOrWhiteSpace(content)
                ? string.Empty
                : content.Trim();
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
        var preview = System.Text.Encoding.UTF8.GetString(content, 0, previewLength).TrimStart('\uFEFF', ' ', '\t', '\r', '\n');
        return preview.StartsWith("{", StringComparison.Ordinal) ||
               preview.StartsWith("[", StringComparison.Ordinal) ||
               preview.StartsWith("<", StringComparison.Ordinal);
    }

    private static string DecodeTextPayload(byte[] content)
        => content.Length == 0
            ? string.Empty
            : System.Text.Encoding.UTF8.GetString(content).Trim();

    private static string SanitizeNarrationText(string? text)
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

    private static int? ToTelemetryDurationSeconds(double? durationSeconds)
    {
        if (!durationSeconds.HasValue || durationSeconds.Value <= 0)
        {
            return null;
        }

        return (int)Math.Ceiling(durationSeconds.Value);
    }

    private sealed record PlaybackSession(long PlaybackId, CancellationToken Token);
    private sealed record AudioPlaybackResult(bool Success, double? DurationSeconds);
    private sealed record DownloadedAudioPayload(byte[] Content, string? ContentType);

    private sealed class MobileRuntimeAppSettings
    {
        public string? ApiBaseUrl { get; set; }
        public Dictionary<string, string> PlatformApiBaseUrls { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    }

    private sealed class ApiEnvelope<T>
    {
        public bool Success { get; set; }
        public T? Data { get; set; }
        public string? Message { get; set; }
    }

    private sealed class TextToSpeechProxyRequestDto
    {
        public string Text { get; set; } = string.Empty;
        public string LanguageCode { get; set; } = AppLanguage.FallbackLanguage;
    }

    private sealed class PoiNarrationResponseDto
    {
        public string PoiId { get; set; } = string.Empty;
        public string RequestedLanguageCode { get; set; } = AppLanguage.FallbackLanguage;
        public string EffectiveLanguageCode { get; set; } = AppLanguage.FallbackLanguage;
        public string TtsInputText { get; set; } = string.Empty;
        public string? TranslatedText { get; set; }
        public string TranslationStatus { get; set; } = "stored";
        public string TtsLocale { get; set; } = "en-US";
        public string? FallbackMessage { get; set; }
        public NarrationAudioGuideDto? AudioGuide { get; set; }
    }

    private sealed class NarrationAudioGuideDto
    {
        public string Id { get; set; } = string.Empty;
        public string AudioUrl { get; set; } = string.Empty;
        public string SourceType { get; set; } = "uploaded";
        public string Status { get; set; } = "ready";
    }
}

public sealed class PoiTourStoreService : IPoiTourStoreService
{
    private const string SavedTourFileName = "vkfood.saved-pois.json";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = false
    };

    private readonly SemaphoreSlim _lock = new(1, 1);
    private HashSet<string>? _savedPoiIds;

    public async Task<bool> IsSavedAsync(string poiId)
    {
        if (string.IsNullOrWhiteSpace(poiId))
        {
            return false;
        }

        var savedPoiIds = await GetSavedPoiIdsAsync();
        return savedPoiIds.Contains(poiId);
    }

    public async Task<bool> ToggleSavedAsync(string poiId)
    {
        if (string.IsNullOrWhiteSpace(poiId))
        {
            return false;
        }

        await _lock.WaitAsync();
        try
        {
            _savedPoiIds ??= await LoadSavedPoiIdsAsync();
            if (!_savedPoiIds.Add(poiId))
            {
                _savedPoiIds.Remove(poiId);
            }

            await SaveAsync(_savedPoiIds);
            return _savedPoiIds.Contains(poiId);
        }
        finally
        {
            _lock.Release();
        }
    }

    private async Task<HashSet<string>> GetSavedPoiIdsAsync()
    {
        await _lock.WaitAsync();
        try
        {
            _savedPoiIds ??= await LoadSavedPoiIdsAsync();
            return new HashSet<string>(_savedPoiIds, StringComparer.OrdinalIgnoreCase);
        }
        finally
        {
            _lock.Release();
        }
    }

    private static async Task<HashSet<string>> LoadSavedPoiIdsAsync()
    {
        try
        {
            var path = GetStoragePath();
            if (!File.Exists(path))
            {
                return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            }

            var content = await File.ReadAllTextAsync(path);
            var poiIds = JsonSerializer.Deserialize<List<string>>(content, JsonOptions) ?? [];
            return new HashSet<string>(poiIds.Where(item => !string.IsNullOrWhiteSpace(item)), StringComparer.OrdinalIgnoreCase);
        }
        catch
        {
            return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        }
    }

    private static async Task SaveAsync(IEnumerable<string> poiIds)
    {
        try
        {
            var path = GetStoragePath();
            var directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var payload = JsonSerializer.Serialize(poiIds.OrderBy(item => item, StringComparer.OrdinalIgnoreCase).ToArray(), JsonOptions);
            await File.WriteAllTextAsync(path, payload);
        }
        catch
        {
            // Best effort persistence only.
        }
    }

    private static string GetStoragePath()
        => Path.Combine(FileSystem.Current.AppDataDirectory, SavedTourFileName);
}
