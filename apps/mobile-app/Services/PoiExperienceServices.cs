using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.Devices;
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

    private readonly SemaphoreSlim _stateLock = new(1, 1);
    private readonly JsonSerializerOptions _jsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };
    private readonly IAudioManager _audioManager;
    private readonly IFoodStreetDataService _dataService;
    private readonly IElevenLabsTtsService _elevenLabsTtsService;
    private readonly ILogger<PoiNarrationService> _logger;
    private readonly HttpClient _mediaClient;
    private HttpClient? _apiClient;
    private string? _resolvedBaseUrl;
    private MobileRuntimeAppSettings? _runtimeSettings;
    private CancellationTokenSource? _playbackCancellationSource;
    private AsyncAudioPlayer? _audioPlayer;
    private Stream? _audioStream;
    private string? _tempAudioFilePath;
    private bool _isPlaybackActive;
    private long _playbackId;

    public PoiNarrationService(
        IAudioManager audioManager,
        IFoodStreetDataService dataService,
        IElevenLabsTtsService elevenLabsTtsService,
        ILogger<PoiNarrationService> logger)
    {
        _audioManager = audioManager;
        _dataService = dataService;
        _elevenLabsTtsService = elevenLabsTtsService;
        _logger = logger;
        _mediaClient = new HttpClient(new HttpClientHandler
        {
            ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator
        })
        {
            Timeout = TimeSpan.FromSeconds(20)
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
        var poiTitle = FirstNonEmpty(
            GetRequestedLocalizedText(detail.Name, requestedLanguageCode),
            GetRequestedLocalizedText(detail.Name, AppLanguage.DefaultLanguage),
            detail.Id);
        try
        {
            _logger.LogInformation(
                "POI narration requested. poiId={PoiId}; poiTitle={PoiTitle}; requestedLanguage={RequestedLanguageCode}",
                detail.Id,
                poiTitle,
                requestedLanguageCode);

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

            var audioUrl = FirstNonEmpty(
                CanUseResolvedAudioGuide(resolvedNarration)
                    ? resolvedNarration?.AudioGuide?.AudioUrl
                    : null,
                GetRequestedLocalizedText(detail.AudioUrls, requestedLanguageCode));
            if (IsPlaceholderAudioUrl(audioUrl))
            {
                audioUrl = string.Empty;
            }

            _logger.LogInformation(
                "Narration payload prepared. poiId={PoiId}; requestedLanguage={RequestedLanguage}; effectiveLanguage={EffectiveLanguage}; translationStatus={TranslationStatus}; hasPreparedAudio={HasPreparedAudio}; narrationLength={NarrationLength}; narrationPreview={NarrationPreview}",
                detail.Id,
                requestedLanguageCode,
                effectiveLanguageCode,
                resolvedNarration?.TranslationStatus ?? "local_fallback",
                !string.IsNullOrWhiteSpace(audioUrl),
                narrationText.Length,
                ToLogPreview(narrationText));

            if (!string.IsNullOrWhiteSpace(audioUrl))
            {
                _logger.LogInformation(
                    "Attempting prepared remote audio playback. poiId={PoiId}; effectiveLanguage={EffectiveLanguage}; audioUrl={AudioUrl}",
                    detail.Id,
                    effectiveLanguageCode,
                    GetSafeUrlForLog(audioUrl));

                var playedAudio = await TryPlayRemoteAudioSequenceAsync([audioUrl], session.PlaybackId, session.Token);
                if (playedAudio)
                {
                    _logger.LogInformation(
                        "Prepared remote audio playback started successfully. poiId={PoiId}; effectiveLanguage={EffectiveLanguage}",
                        detail.Id,
                        effectiveLanguageCode);
                    await _dataService.TrackAudioPlayAsync(detail.Id, effectiveLanguageCode, "remote_audio");
                    return;
                }

                _logger.LogWarning(
                    "Prepared remote audio playback failed. poiId={PoiId}; effectiveLanguage={EffectiveLanguage}; audioUrl={AudioUrl}",
                    detail.Id,
                    effectiveLanguageCode,
                    GetSafeUrlForLog(audioUrl));
            }

            if (string.IsNullOrWhiteSpace(narrationText))
            {
                _logger.LogWarning(
                    "Narration text is empty after all fallbacks. poiId={PoiId}; requestedLanguage={RequestedLanguage}; effectiveLanguage={EffectiveLanguage}",
                    detail.Id,
                    requestedLanguageCode,
                    effectiveLanguageCode);
                return;
            }

            try
            {
                var generatedSpeech = await _elevenLabsTtsService.GenerateSpeechAsync(
                    narrationText,
                    effectiveLanguageCode,
                    cancellationToken: session.Token);
                _logger.LogInformation(
                    "ElevenLabs TTS proxy request succeeded. poiId={PoiId}; apiKeyPresent={ApiKeyPresent}; segmentCount={SegmentCount}; modelId={ModelId}; voiceId={VoiceId}",
                    detail.Id,
                    generatedSpeech.ApiKeyPresent,
                    generatedSpeech.Segments.Count,
                    generatedSpeech.ModelId,
                    generatedSpeech.VoiceId ?? "default");

                var playedGeneratedAudio = await TryPlayGeneratedSpeechAsync(generatedSpeech, session.PlaybackId, session.Token);
                if (playedGeneratedAudio)
                {
                    _logger.LogInformation(
                        "Generated ElevenLabs audio playback started successfully. poiId={PoiId}; segmentCount={SegmentCount}",
                        detail.Id,
                        generatedSpeech.Segments.Count);
                    await _dataService.TrackAudioPlayAsync(detail.Id, effectiveLanguageCode, "elevenlabs_tts");
                    return;
                }

                _logger.LogWarning(
                    "Generated ElevenLabs audio was received but could not be played. poiId={PoiId}; segmentCount={SegmentCount}",
                    detail.Id,
                    generatedSpeech.Segments.Count);
            }
            catch (ElevenLabsTtsException exception)
            {
                _logger.LogWarning(
                    exception,
                    "ElevenLabs TTS generation failed. poiId={PoiId}; requestedLanguage={RequestedLanguage}; effectiveLanguage={EffectiveLanguage}; apiKeyPresent={ApiKeyPresent}; status={StatusCode}; response={ResponseBody}",
                    detail.Id,
                    requestedLanguageCode,
                    effectiveLanguageCode,
                    exception.ApiKeyPresent,
                    exception.StatusCode,
                    exception.ResponseBody);
            }

            await SpeakWithDeviceTextToSpeechAsync(
                narrationText,
                resolvedNarration?.TtsLocale,
                session.Token);
            _logger.LogInformation(
                "Fell back to device text-to-speech. poiId={PoiId}; effectiveLanguage={EffectiveLanguage}; narrationLength={NarrationLength}",
                detail.Id,
                effectiveLanguageCode,
                narrationText.Length);
            await _dataService.TrackAudioPlayAsync(detail.Id, effectiveLanguageCode, "device_tts");
        }
        catch (OperationCanceledException) when (session.Token.IsCancellationRequested)
        {
            _logger.LogInformation(
                "POI narration playback was cancelled. poiId={PoiId}; requestedLanguage={RequestedLanguageCode}",
                detail.Id,
                requestedLanguageCode);
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

        foreach (var candidate in GetRequestedLanguageCandidates(AppLanguage.DefaultLanguage))
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

    private async Task<bool> TryPlayRemoteAudioSequenceAsync(
        IReadOnlyList<string> audioUrls,
        long playbackId,
        CancellationToken cancellationToken)
    {
        if (audioUrls.Count == 0)
        {
            return false;
        }

        try
        {
            foreach (var audioUrl in audioUrls)
            {
                if (!Uri.TryCreate(audioUrl, UriKind.Absolute, out _))
                {
                    _logger.LogWarning("Remote audio URL is invalid and cannot be played. audioUrl={AudioUrl}", GetSafeUrlForLog(audioUrl));
                    return false;
                }

                await PlayRemoteAudioSegmentAsync(audioUrl, playbackId, cancellationToken);
            }

            return true;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return false;
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "Remote audio playback failed for POI narration.");
            return false;
        }
    }

    private async Task PlayRemoteAudioSegmentAsync(string audioUrl, long playbackId, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Sending remote audio request. audioUrl={AudioUrl}", GetSafeUrlForLog(audioUrl));

        using var response = await _mediaClient.GetAsync(audioUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var errorBody = await SafeReadBodyAsync(response, cancellationToken);
            _logger.LogWarning(
                "Remote audio request failed. audioUrl={AudioUrl}; status={StatusCode}; response={ResponseBody}",
                GetSafeUrlForLog(audioUrl),
                response.StatusCode,
                errorBody);
            response.EnsureSuccessStatusCode();
        }

        await using var networkStream = await response.Content.ReadAsStreamAsync(cancellationToken);
        var buffer = new MemoryStream();
        await networkStream.CopyToAsync(buffer, cancellationToken);
        buffer.Position = 0;

        _logger.LogInformation(
            "Remote audio response received. audioUrl={AudioUrl}; contentType={ContentType}; contentLength={ContentLength}",
            GetSafeUrlForLog(audioUrl),
            response.Content.Headers.ContentType?.ToString() ?? "unknown",
            response.Content.Headers.ContentLength ?? buffer.Length);

        AsyncAudioPlayer player;
        await _stateLock.WaitAsync(cancellationToken);
        try
        {
            if (playbackId != _playbackId || cancellationToken.IsCancellationRequested)
            {
                buffer.Dispose();
                throw new OperationCanceledException(cancellationToken);
            }

            DisposeCurrentAudioResourcesLocked();
            _audioStream = buffer;
            _audioPlayer = _audioManager.CreateAsyncPlayer(_audioStream);
            player = _audioPlayer;
        }
        finally
        {
            _stateLock.Release();
        }

        try
        {
            await player.PlayAsync(cancellationToken);
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

    private async Task<bool> TryPlayGeneratedSpeechAsync(
        GeneratedSpeechResult generatedSpeech,
        long playbackId,
        CancellationToken cancellationToken)
    {
        if (generatedSpeech.Segments.Count == 0)
        {
            _logger.LogWarning("Generated ElevenLabs speech result has no audio segments to play.");
            return false;
        }

        try
        {
            foreach (var segment in generatedSpeech.Segments)
            {
                await PlayGeneratedAudioSegmentAsync(segment, playbackId, cancellationToken);
            }

            return true;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return false;
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "Unable to play generated ElevenLabs audio for POI narration.");
            return false;
        }
        finally
        {
            _elevenLabsTtsService.CleanupGeneratedSpeech(generatedSpeech.Segments);
        }
    }

    private async Task PlayGeneratedAudioSegmentAsync(
        GeneratedSpeechSegment segment,
        long playbackId,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(segment.FilePath) || !File.Exists(segment.FilePath))
        {
            throw new FileNotFoundException("Generated speech segment file was not found.", segment.FilePath);
        }

        _logger.LogInformation(
            "Starting generated ElevenLabs audio playback. filePath={FilePath}; contentType={ContentType}; contentLength={ContentLength}",
            segment.FilePath,
            segment.ContentType,
            segment.ContentLength);

        var fileStream = File.OpenRead(segment.FilePath);

        AsyncAudioPlayer player;
        await _stateLock.WaitAsync(cancellationToken);
        try
        {
            if (playbackId != _playbackId || cancellationToken.IsCancellationRequested)
            {
                fileStream.Dispose();
                throw new OperationCanceledException(cancellationToken);
            }

            DisposeCurrentAudioResourcesLocked();
            _audioStream = fileStream;
            _tempAudioFilePath = segment.FilePath;
            _audioPlayer = _audioManager.CreateAsyncPlayer(_audioStream);
            player = _audioPlayer;
        }
        finally
        {
            _stateLock.Release();
        }

        try
        {
            await player.PlayAsync(cancellationToken);
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
            _logger.LogInformation(
                "Resolving POI narration from backend. poiId={PoiId}; languageCode={LanguageCode}; relativeUrl={RelativeUrl}",
                poiId,
                languageCode,
                relativeUrl);
            var envelope = await client.GetFromJsonAsync<ApiEnvelope<PoiNarrationResponseDto>>(relativeUrl, _jsonOptions, cancellationToken);
            if (envelope?.Success != true || envelope.Data is null)
            {
                _logger.LogWarning(
                    "POI narration response was empty or unsuccessful. poiId={PoiId}; languageCode={LanguageCode}; success={Success}; message={Message}",
                    poiId,
                    languageCode,
                    envelope?.Success,
                    envelope?.Message);
                return null;
            }

            if (envelope.Data.AudioGuide is not null)
            {
                envelope.Data.AudioGuide.AudioUrl = NormalizeAudioUrl(envelope.Data.AudioGuide.AudioUrl, client.BaseAddress);
            }

            _logger.LogInformation(
                "POI narration resolved. poiId={PoiId}; requestedLanguage={RequestedLanguage}; effectiveLanguage={EffectiveLanguage}; translationStatus={TranslationStatus}; hasAudioGuide={HasAudioGuide}; ttsTextLength={TtsTextLength}",
                poiId,
                envelope.Data.RequestedLanguageCode,
                envelope.Data.EffectiveLanguageCode,
                envelope.Data.TranslationStatus,
                envelope.Data.AudioGuide is not null && !string.IsNullOrWhiteSpace(envelope.Data.AudioGuide.AudioUrl),
                envelope.Data.TtsInputText?.Length ?? 0);
            return envelope.Data;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            _logger.LogWarning(
                exception,
                "Unable to resolve POI narration from backend. poiId={PoiId}; languageCode={LanguageCode}",
                poiId,
                languageCode);
            return null;
        }
    }

    private async Task<HttpClient?> GetApiClientAsync()
    {
        var runtimeSettings = await LoadRuntimeSettingsAsync();
        var nextBaseUrl = EnsureTrailingSlash(ResolveApiBaseUrl(runtimeSettings));
        if (string.IsNullOrWhiteSpace(nextBaseUrl))
        {
            _logger.LogWarning("POI narration has no API base URL configured for backend narration requests.");
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
        WarnIfLoopbackBaseUrlOnPhysicalDevice(nextBaseUrl);
        _logger.LogInformation("Configured POI narration backend client. baseUrl={BaseUrl}", nextBaseUrl);
        return _apiClient;
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
            _logger.LogWarning(exception, "Unable to load mobile appsettings.json for POI narration.");
            _runtimeSettings = new MobileRuntimeAppSettings();
        }

        _logger.LogInformation(
            "Loaded POI narration runtime settings. apiBaseUrl={ApiBaseUrl}; resolvedApiBaseUrl={ResolvedApiBaseUrl}",
            _runtimeSettings.ApiBaseUrl ?? "(empty)",
            ResolveApiBaseUrl(_runtimeSettings));

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

        _audioStream?.Dispose();
        _audioStream = null;

        if (!string.IsNullOrWhiteSpace(_tempAudioFilePath) && File.Exists(_tempAudioFilePath))
        {
            try
            {
                File.Delete(_tempAudioFilePath);
            }
            catch (Exception exception)
            {
                _logger.LogDebug(exception, "Unable to delete temporary narration audio file {FilePath}.", _tempAudioFilePath);
            }
        }

        _tempAudioFilePath = null;
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

    private async Task<string> SafeReadBodyAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        try
        {
            var content = await response.Content.ReadAsStringAsync(cancellationToken);
            if (string.IsNullOrWhiteSpace(content))
            {
                return string.Empty;
            }

            return content.Length <= 300 ? content : content[..300];
        }
        catch (Exception exception)
        {
            _logger.LogDebug(exception, "Unable to read remote audio error response body.");
            return string.Empty;
        }
    }

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

    private static string ToLogPreview(string value)
    {
        var normalized = value?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return "(empty)";
        }

        return normalized.Length <= 180
            ? normalized
            : $"{normalized[..180]}...";
    }

    private static string GetSafeUrlForLog(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "(empty)";
        }

        if (!Uri.TryCreate(value, UriKind.Absolute, out var parsed))
        {
            return value.Trim();
        }

        return $"{parsed.Scheme}://{parsed.Host}{parsed.AbsolutePath}";
    }

    private sealed record PlaybackSession(long PlaybackId, CancellationToken Token);

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
