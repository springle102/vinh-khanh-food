using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Maui.Media;
using Microsoft.Maui.Storage;
using Plugin.Maui.Audio;
using VinhKhanh.MobileApp.Helpers;
using VinhKhanh.MobileApp.Models;

namespace VinhKhanh.MobileApp.Services;

public enum PoiAudioPlaybackStatus
{
    Idle,
    Loading,
    Playing,
    Stopped,
    Error
}

public sealed record PoiAudioPlaybackSnapshot(
    PoiAudioPlaybackStatus Status,
    string? PoiId,
    string? RequestedLanguageCode,
    string? EffectiveLanguageCode,
    string? UiPlaybackKey,
    string? AudioCacheKey,
    string? Source,
    string? ErrorMessage)
{
    public static PoiAudioPlaybackSnapshot Idle { get; } = new(
        PoiAudioPlaybackStatus.Idle,
        null,
        null,
        null,
        null,
        null,
        null,
        null);

    public bool IsBusy => Status is PoiAudioPlaybackStatus.Loading or PoiAudioPlaybackStatus.Playing;

    public bool Matches(string? poiId, string? languageCode) =>
        !string.IsNullOrWhiteSpace(PoiId) &&
        !string.IsNullOrWhiteSpace(poiId) &&
        string.Equals(PoiId, poiId, StringComparison.OrdinalIgnoreCase) &&
        string.Equals(
            AppLanguage.NormalizeCode(RequestedLanguageCode ?? string.Empty),
            AppLanguage.NormalizeCode(languageCode ?? string.Empty),
            StringComparison.OrdinalIgnoreCase);
}

public interface IPoiAudioPlaybackService
{
    PoiAudioPlaybackSnapshot Snapshot { get; }
    bool IsPlaying { get; }
    bool IsBusy { get; }
    event EventHandler<PoiAudioPlaybackSnapshot>? PlaybackStateChanged;

    Task PlayAsync(PoiExperienceDetail detail, string languageCode, CancellationToken cancellationToken = default);
    Task ToggleAsync(PoiExperienceDetail detail, string languageCode, CancellationToken cancellationToken = default);
    Task PreloadAsync(PoiExperienceDetail detail, string languageCode, CancellationToken cancellationToken = default);
    Task StopAsync();
}

public sealed class PoiAudioPlaybackService : IPoiAudioPlaybackService
{
    private const string AppSettingsFileName = "appsettings.json";
    private const string CacheIndexFileName = "poi-audio-cache-index.json";
    private static readonly TimeSpan AudioRequestTimeout = TimeSpan.FromSeconds(45);

    private readonly SemaphoreSlim _stateLock = new(1, 1);
    private readonly SemaphoreSlim _cacheLock = new(1, 1);
    private readonly JsonSerializerOptions _jsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = false
    };
    private readonly IAudioManager _audioManager;
    private readonly IFoodStreetDataService _dataService;
    private readonly ILogger<PoiAudioPlaybackService> _logger;
    private readonly HttpClient _httpClient;
    private readonly ConcurrentDictionary<string, Task<CachedAudioAsset?>> _inflightLoads = new(StringComparer.OrdinalIgnoreCase);

    private MobileRuntimeAppSettings? _runtimeSettings;
    private PoiAudioPlaybackSnapshot _snapshot = PoiAudioPlaybackSnapshot.Idle;
    private CancellationTokenSource? _activeSessionSource;
    private AsyncAudioPlayer? _audioPlayer;
    private Stream? _audioStream;
    private long _sessionId;
    private Dictionary<string, AudioCacheIndexEntry>? _cacheIndex;

    public PoiAudioPlaybackService(
        IAudioManager audioManager,
        IFoodStreetDataService dataService,
        ILogger<PoiAudioPlaybackService> logger)
    {
        _audioManager = audioManager;
        _dataService = dataService;
        _logger = logger;
        _httpClient = new HttpClient(new HttpClientHandler
        {
            ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator
        })
        {
            Timeout = Timeout.InfiniteTimeSpan
        };
        _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Linux; Android 12; VinhKhanhMobile)");
    }

    public PoiAudioPlaybackSnapshot Snapshot => _snapshot;

    public bool IsPlaying => Snapshot.Status == PoiAudioPlaybackStatus.Playing;

    public bool IsBusy => Snapshot.IsBusy;

    public event EventHandler<PoiAudioPlaybackSnapshot>? PlaybackStateChanged;

    public async Task ToggleAsync(
        PoiExperienceDetail detail,
        string languageCode,
        CancellationToken cancellationToken = default)
    {
        var normalizedLanguageCode = AppLanguage.NormalizeCode(languageCode);
        var currentSnapshot = Snapshot;
        if (currentSnapshot.Status == PoiAudioPlaybackStatus.Loading &&
            currentSnapshot.Matches(detail.Id, normalizedLanguageCode))
        {
            return;
        }

        if (currentSnapshot.Status == PoiAudioPlaybackStatus.Playing &&
            currentSnapshot.Matches(detail.Id, normalizedLanguageCode))
        {
            await StopAsync();
            return;
        }

        await PlayAsync(detail, normalizedLanguageCode, cancellationToken);
    }

    public async Task PlayAsync(
        PoiExperienceDetail detail,
        string languageCode,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(detail);

        var normalizedLanguageCode = AppLanguage.NormalizeCode(languageCode);
        var uiPlaybackKey = BuildUiPlaybackKey(detail.Id, normalizedLanguageCode);
        var session = await BeginPlaybackSessionAsync(detail.Id, normalizedLanguageCode, uiPlaybackKey, cancellationToken);

        try
        {
            var cachedAudio = await EnsureAudioAssetAsync(detail.Id, normalizedLanguageCode, cancellationToken);
            if (cachedAudio is not null)
            {
                await PlayCachedAudioAsync(cachedAudio, session);
                return;
            }

            var narrationText = ResolveFallbackNarrationText(detail, normalizedLanguageCode);
            if (string.IsNullOrWhiteSpace(narrationText))
            {
                await PublishErrorAsync(
                    detail.Id,
                    normalizedLanguageCode,
                    "Chưa có nội dung thuyết minh cho địa điểm này.");
                return;
            }

            await PlayDeviceTextToSpeechAsync(
                detail.Id,
                normalizedLanguageCode,
                uiPlaybackKey,
                narrationText,
                session);
        }
        catch (OperationCanceledException) when (session.Token.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            _logger.LogWarning(
                exception,
                "Unable to play POI narration. poiId={PoiId}; languageCode={LanguageCode}",
                detail.Id,
                normalizedLanguageCode);
            await PublishErrorAsync(
                detail.Id,
                normalizedLanguageCode,
                "Không thể phát thuyết minh lúc này.");
        }
    }

    public async Task PreloadAsync(
        PoiExperienceDetail detail,
        string languageCode,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(detail);

        try
        {
            await EnsureAudioAssetAsync(detail.Id, AppLanguage.NormalizeCode(languageCode), cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            _logger.LogDebug(
                exception,
                "POI audio preload failed. poiId={PoiId}; languageCode={LanguageCode}",
                detail.Id,
                languageCode);
        }
    }

    public async Task StopAsync()
    {
        PoiAudioPlaybackSnapshot snapshotToPublish;

        await _stateLock.WaitAsync();
        try
        {
            Interlocked.Increment(ref _sessionId);
            CancelActiveSessionLocked();
            DisposeActiveAudioResourcesLocked();
            snapshotToPublish = string.IsNullOrWhiteSpace(_snapshot.PoiId)
                ? PoiAudioPlaybackSnapshot.Idle
                : _snapshot with
                {
                    Status = PoiAudioPlaybackStatus.Stopped,
                    Source = null,
                    ErrorMessage = null
                };
            _snapshot = snapshotToPublish;
        }
        finally
        {
            _stateLock.Release();
        }

        PublishSnapshotChanged(snapshotToPublish);
    }

    private async Task<CachedAudioAsset?> EnsureAudioAssetAsync(
        string poiId,
        string languageCode,
        CancellationToken cancellationToken)
    {
        var uiPlaybackKey = BuildUiPlaybackKey(poiId, languageCode);
        var cached = await TryGetCachedAudioAsync(uiPlaybackKey);
        if (cached is not null)
        {
            return cached with { PlaybackSource = "local_cache" };
        }

        var loadTask = _inflightLoads.GetOrAdd(
            uiPlaybackKey,
            _ => LoadAndCacheAudioAsync(poiId, languageCode));

        try
        {
            return await loadTask.WaitAsync(cancellationToken);
        }
        finally
        {
            if (loadTask.IsCompleted)
            {
                _inflightLoads.TryRemove(uiPlaybackKey, out _);
            }
        }
    }

    private async Task<CachedAudioAsset?> LoadAndCacheAudioAsync(string poiId, string languageCode)
    {
        var downloadedAudio = await DownloadPoiAudioAsync(poiId, languageCode);
        if (downloadedAudio is null || downloadedAudio.Content.Length == 0)
        {
            return null;
        }

        var cacheKey = string.IsNullOrWhiteSpace(downloadedAudio.AudioCacheKey)
            ? CreateCacheHash($"{downloadedAudio.UiPlaybackKey}|{downloadedAudio.ContentType}|{downloadedAudio.Content.Length}")
            : downloadedAudio.AudioCacheKey;
        var extension = GetFileExtension(downloadedAudio.ContentType);
        var fileName = $"{CreateCacheHash(cacheKey)}{extension}";
        var directoryPath = GetCacheDirectoryPath();
        var targetPath = Path.Combine(directoryPath, fileName);

        Directory.CreateDirectory(directoryPath);
        if (!File.Exists(targetPath))
        {
            var tempPath = Path.Combine(directoryPath, $"{Guid.NewGuid():N}.tmp");
            await File.WriteAllBytesAsync(tempPath, downloadedAudio.Content);

            try
            {
                File.Move(tempPath, targetPath, overwrite: false);
            }
            catch (IOException)
            {
                if (File.Exists(targetPath))
                {
                    File.Delete(tempPath);
                }
                else
                {
                    throw;
                }
            }
        }

        var indexEntry = new AudioCacheIndexEntry
        {
            UiPlaybackKey = downloadedAudio.UiPlaybackKey,
            AudioCacheKey = cacheKey,
            FileName = fileName,
            ContentType = downloadedAudio.ContentType,
            EffectiveLanguageCode = downloadedAudio.EffectiveLanguageCode,
            Source = downloadedAudio.Source,
            TtsLocale = downloadedAudio.TtsLocale,
            EstimatedDurationSeconds = downloadedAudio.EstimatedDurationSeconds,
            CachedAtUtc = DateTimeOffset.UtcNow
        };
        await SaveCacheIndexEntryAsync(indexEntry);

        return new CachedAudioAsset(
            poiId,
            languageCode,
            downloadedAudio.EffectiveLanguageCode,
            downloadedAudio.UiPlaybackKey,
            cacheKey,
            targetPath,
            downloadedAudio.ContentType,
            downloadedAudio.Source,
            downloadedAudio.TtsLocale,
            downloadedAudio.EstimatedDurationSeconds,
            downloadedAudio.Source);
    }

    private async Task<DownloadedAudioPayload?> DownloadPoiAudioAsync(string poiId, string languageCode)
    {
        var apiBaseUrl = await GetApiBaseUrlAsync();
        if (string.IsNullOrWhiteSpace(apiBaseUrl))
        {
            return null;
        }

        var requestUri = new Uri(
            new Uri(apiBaseUrl, UriKind.Absolute),
            $"api/v1/pois/{Uri.EscapeDataString(poiId)}/audio?languageCode={Uri.EscapeDataString(languageCode)}");

        using var request = new HttpRequestMessage(HttpMethod.Get, requestUri);
        using var timeoutSource = new CancellationTokenSource(AudioRequestTimeout);
        using var response = await _httpClient.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            timeoutSource.Token);

        if (!response.IsSuccessStatusCode)
        {
            var responseBody = await SafeReadBodyAsync(response, timeoutSource.Token);
            _logger.LogWarning(
                "POI narration audio request failed. request={Request}; statusCode={StatusCode}; responseBody={ResponseBody}",
                requestUri,
                (int)response.StatusCode,
                responseBody);
            return null;
        }

        var contentType = response.Content.Headers.ContentType?.ToString() ?? "audio/mpeg";
        await using var networkStream = await response.Content.ReadAsStreamAsync(timeoutSource.Token);
        await using var buffer = new MemoryStream();
        await networkStream.CopyToAsync(buffer, timeoutSource.Token);
        var content = buffer.ToArray();

        if (content.Length == 0)
        {
            _logger.LogWarning(
                "POI narration audio request returned 0 bytes. request={Request}; contentType={ContentType}",
                requestUri,
                contentType);
            return null;
        }

        if (LooksLikeTextPayload(contentType, content))
        {
            _logger.LogWarning(
                "POI narration audio request returned a non-audio payload. request={Request}; contentType={ContentType}; responseBody={ResponseBody}",
                requestUri,
                contentType,
                DecodeTextPayload(content));
            return null;
        }

        var uiPlaybackKey = GetHeaderValue(response, "X-Ui-Playback-Key") ?? BuildUiPlaybackKey(poiId, languageCode);
        var effectiveLanguageCode = AppLanguage.NormalizeCode(
            GetHeaderValue(response, "X-Effective-Language-Code") ?? languageCode);
        var audioCacheKey = GetHeaderValue(response, "X-Audio-Cache-Key") ?? uiPlaybackKey;
        var source = GetHeaderValue(response, "X-Audio-Source") ?? "backend_audio";
        var ttsLocale = GetHeaderValue(response, "X-TTS-Locale");
        var estimatedDurationSeconds = TryParseDouble(
            GetHeaderValue(response, "X-Audio-Duration-Seconds"));

        _logger.LogInformation(
            "POI narration audio downloaded. poiId={PoiId}; requestedLanguage={RequestedLanguage}; effectiveLanguage={EffectiveLanguage}; source={Source}; audioBytes={AudioBytes}; cacheKey={CacheKey}",
            poiId,
            languageCode,
            effectiveLanguageCode,
            source,
            content.Length,
            audioCacheKey);

        return new DownloadedAudioPayload(
            content,
            contentType,
            uiPlaybackKey,
            audioCacheKey,
            effectiveLanguageCode,
            source,
            ttsLocale,
            estimatedDurationSeconds);
    }

    private async Task PlayCachedAudioAsync(CachedAudioAsset audio, PlaybackSession session)
    {
        FileStream? stream = null;
        AsyncAudioPlayer? player = null;
        var playbackStarted = false;

        try
        {
            stream = new FileStream(audio.LocalFilePath, FileMode.Open, FileAccess.Read, FileShare.Read);

            PoiAudioPlaybackSnapshot snapshotToPublish;
            await _stateLock.WaitAsync(session.Token);
            try
            {
                if (session.Id != _sessionId || session.Token.IsCancellationRequested)
                {
                    return;
                }

                DisposeActiveAudioResourcesLocked();
                _audioStream = stream;
                _audioPlayer = _audioManager.CreateAsyncPlayer(_audioStream);
                player = _audioPlayer;
                stream = null;

                snapshotToPublish = new PoiAudioPlaybackSnapshot(
                    PoiAudioPlaybackStatus.Playing,
                    session.PoiId,
                    session.RequestedLanguageCode,
                    audio.EffectiveLanguageCode,
                    audio.UiPlaybackKey,
                    audio.AudioCacheKey,
                    audio.PlaybackSource,
                    null);
                _snapshot = snapshotToPublish;
            }
            finally
            {
                _stateLock.Release();
            }

            PublishSnapshotChanged(snapshotToPublish);
            playbackStarted = true;
            await player!.PlayAsync(session.Token);

            try
            {
                await _dataService.TrackAudioPlayAsync(
                    session.PoiId,
                    audio.EffectiveLanguageCode,
                    audio.PlaybackSource,
                    ToTelemetryDurationSeconds(audio.EstimatedDurationSeconds));
            }
            catch
            {
            }
        }
        catch (OperationCanceledException) when (session.Token.IsCancellationRequested)
        {
        }
        finally
        {
            stream?.Dispose();

            PoiAudioPlaybackSnapshot? snapshotToPublish = null;
            await _stateLock.WaitAsync();
            try
            {
                if (session.Id == _sessionId)
                {
                    if (ReferenceEquals(_audioPlayer, player))
                    {
                        DisposeActiveAudioResourcesLocked();
                    }

                    if (playbackStarted &&
                        (_snapshot.Status == PoiAudioPlaybackStatus.Playing ||
                         _snapshot.Status == PoiAudioPlaybackStatus.Loading))
                    {
                        snapshotToPublish = _snapshot with
                        {
                            Status = PoiAudioPlaybackStatus.Stopped,
                            Source = null,
                            ErrorMessage = null
                        };
                        _snapshot = snapshotToPublish;
                    }
                }
            }
            finally
            {
                _stateLock.Release();
            }

            if (snapshotToPublish is not null)
            {
                PublishSnapshotChanged(snapshotToPublish);
            }
        }
    }

    private async Task PlayDeviceTextToSpeechAsync(
        string poiId,
        string languageCode,
        string uiPlaybackKey,
        string narrationText,
        PlaybackSession session)
    {
        var snapshotToPublish = new PoiAudioPlaybackSnapshot(
            PoiAudioPlaybackStatus.Playing,
            poiId,
            languageCode,
            languageCode,
            uiPlaybackKey,
            null,
            "device_tts",
            null);

        await _stateLock.WaitAsync(session.Token);
        try
        {
            if (session.Id != _sessionId || session.Token.IsCancellationRequested)
            {
                return;
            }

            _snapshot = snapshotToPublish;
        }
        finally
        {
            _stateLock.Release();
        }

        PublishSnapshotChanged(snapshotToPublish);

        try
        {
            await TextToSpeech.Default.SpeakAsync(narrationText, null, session.Token);

            try
            {
                await _dataService.TrackAudioPlayAsync(poiId, languageCode, "device_tts");
            }
            catch
            {
            }
        }
        catch (OperationCanceledException) when (session.Token.IsCancellationRequested)
        {
        }
        finally
        {
            PoiAudioPlaybackSnapshot? stoppedSnapshot = null;
            await _stateLock.WaitAsync();
            try
            {
                if (session.Id == _sessionId &&
                    (_snapshot.Status == PoiAudioPlaybackStatus.Playing ||
                     _snapshot.Status == PoiAudioPlaybackStatus.Loading))
                {
                    stoppedSnapshot = _snapshot with
                    {
                        Status = PoiAudioPlaybackStatus.Stopped,
                        Source = null,
                        ErrorMessage = null
                    };
                    _snapshot = stoppedSnapshot;
                }
            }
            finally
            {
                _stateLock.Release();
            }

            if (stoppedSnapshot is not null)
            {
                PublishSnapshotChanged(stoppedSnapshot);
            }
        }
    }

    private async Task<PlaybackSession> BeginPlaybackSessionAsync(
        string poiId,
        string languageCode,
        string uiPlaybackKey,
        CancellationToken cancellationToken)
    {
        var snapshotToPublish = PoiAudioPlaybackSnapshot.Idle;

        await _stateLock.WaitAsync(cancellationToken);
        try
        {
            Interlocked.Increment(ref _sessionId);
            CancelActiveSessionLocked();
            DisposeActiveAudioResourcesLocked();
            _activeSessionSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

            snapshotToPublish = new PoiAudioPlaybackSnapshot(
                PoiAudioPlaybackStatus.Loading,
                poiId,
                languageCode,
                null,
                uiPlaybackKey,
                null,
                null,
                null);
            _snapshot = snapshotToPublish;
            return new PlaybackSession(_sessionId, poiId, languageCode, uiPlaybackKey, _activeSessionSource.Token);
        }
        finally
        {
            _stateLock.Release();
            PublishSnapshotChanged(snapshotToPublish);
        }
    }

    private async Task PublishErrorAsync(string poiId, string languageCode, string message)
    {
        PoiAudioPlaybackSnapshot snapshotToPublish;

        await _stateLock.WaitAsync();
        try
        {
            snapshotToPublish = new PoiAudioPlaybackSnapshot(
                PoiAudioPlaybackStatus.Error,
                poiId,
                languageCode,
                languageCode,
                BuildUiPlaybackKey(poiId, languageCode),
                _snapshot.AudioCacheKey,
                null,
                message);
            _snapshot = snapshotToPublish;
        }
        finally
        {
            _stateLock.Release();
        }

        PublishSnapshotChanged(snapshotToPublish);
    }

    private void CancelActiveSessionLocked()
    {
        _activeSessionSource?.Cancel();
        _activeSessionSource?.Dispose();
        _activeSessionSource = null;
    }

    private void DisposeActiveAudioResourcesLocked()
    {
        _audioPlayer?.Stop();
        _audioPlayer?.Dispose();
        _audioPlayer = null;

        _audioStream?.Dispose();
        _audioStream = null;
    }

    private void PublishSnapshotChanged(PoiAudioPlaybackSnapshot snapshot)
        => PlaybackStateChanged?.Invoke(this, snapshot);

    private async Task<CachedAudioAsset?> TryGetCachedAudioAsync(string uiPlaybackKey)
    {
        await _cacheLock.WaitAsync();
        try
        {
            _cacheIndex ??= await LoadCacheIndexAsync();
            if (!_cacheIndex.TryGetValue(uiPlaybackKey, out var indexEntry))
            {
                return null;
            }

            var filePath = Path.Combine(GetCacheDirectoryPath(), indexEntry.FileName);
            if (File.Exists(filePath))
            {
                return new CachedAudioAsset(
                    ResolvePoiId(uiPlaybackKey),
                    ResolveLanguageCode(uiPlaybackKey),
                    AppLanguage.NormalizeCode(indexEntry.EffectiveLanguageCode),
                    uiPlaybackKey,
                    indexEntry.AudioCacheKey,
                    filePath,
                    indexEntry.ContentType,
                    indexEntry.Source,
                    indexEntry.TtsLocale,
                    indexEntry.EstimatedDurationSeconds,
                    "local_cache");
            }

            _cacheIndex.Remove(uiPlaybackKey);
            await SaveCacheIndexAsync(_cacheIndex);
            return null;
        }
        finally
        {
            _cacheLock.Release();
        }
    }

    private async Task SaveCacheIndexEntryAsync(AudioCacheIndexEntry indexEntry)
    {
        await _cacheLock.WaitAsync();
        try
        {
            _cacheIndex ??= await LoadCacheIndexAsync();
            _cacheIndex[indexEntry.UiPlaybackKey] = indexEntry;
            await SaveCacheIndexAsync(_cacheIndex);
        }
        finally
        {
            _cacheLock.Release();
        }
    }

    private async Task<Dictionary<string, AudioCacheIndexEntry>> LoadCacheIndexAsync()
    {
        var indexPath = GetCacheIndexPath();
        if (!File.Exists(indexPath))
        {
            return new Dictionary<string, AudioCacheIndexEntry>(StringComparer.OrdinalIgnoreCase);
        }

        try
        {
            var content = await File.ReadAllTextAsync(indexPath);
            var entries = JsonSerializer.Deserialize<List<AudioCacheIndexEntry>>(content, _jsonOptions) ?? [];
            return entries
                .Where(item => !string.IsNullOrWhiteSpace(item.UiPlaybackKey))
                .GroupBy(item => item.UiPlaybackKey, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(group => group.Key, group => group.Last(), StringComparer.OrdinalIgnoreCase);
        }
        catch
        {
            return new Dictionary<string, AudioCacheIndexEntry>(StringComparer.OrdinalIgnoreCase);
        }
    }

    private async Task SaveCacheIndexAsync(Dictionary<string, AudioCacheIndexEntry> index)
    {
        var indexPath = GetCacheIndexPath();
        Directory.CreateDirectory(Path.GetDirectoryName(indexPath)!);
        var payload = JsonSerializer.Serialize(index.Values.OrderBy(item => item.UiPlaybackKey).ToArray(), _jsonOptions);
        await File.WriteAllTextAsync(indexPath, payload);
    }

    private async Task<string?> GetApiBaseUrlAsync()
    {
        var runtimeSettings = await LoadRuntimeSettingsAsync();
        var nextBaseUrl = MobileApiEndpointHelper.EnsureTrailingSlash(
            MobileApiEndpointHelper.ResolveBaseUrl(runtimeSettings.ApiBaseUrl, runtimeSettings.PlatformApiBaseUrls));
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

    private static string ResolveFallbackNarrationText(PoiExperienceDetail detail, string languageCode)
    {
        foreach (var candidate in AppLanguage.GetCandidateCodes(languageCode))
        {
            if (detail.Description.Values.TryGetValue(candidate, out var description) &&
                LocalizationFallbackPolicy.IsUsableTextForLanguage(description, candidate))
            {
                return description.Trim();
            }

            if (detail.Summary.Values.TryGetValue(candidate, out var summary) &&
                LocalizationFallbackPolicy.IsUsableTextForLanguage(summary, candidate))
            {
                return summary.Trim();
            }
        }

        return string.Empty;
    }

    private static string BuildUiPlaybackKey(string poiId, string languageCode)
        => $"{poiId}:{AppLanguage.NormalizeCode(languageCode)}";

    private static string ResolvePoiId(string uiPlaybackKey)
        => uiPlaybackKey.Split(':', 2)[0];

    private static string ResolveLanguageCode(string uiPlaybackKey)
        => uiPlaybackKey.Split(':', 2).ElementAtOrDefault(1) ?? AppLanguage.FallbackLanguage;

    private static string GetCacheDirectoryPath()
        => Path.Combine(FileSystem.Current.AppDataDirectory, "poi-audio-cache");

    private static string GetCacheIndexPath()
        => Path.Combine(GetCacheDirectoryPath(), CacheIndexFileName);

    private static string GetFileExtension(string contentType)
        => contentType.Contains("audio/wav", StringComparison.OrdinalIgnoreCase) ? ".wav"
            : contentType.Contains("audio/ogg", StringComparison.OrdinalIgnoreCase) ? ".ogg"
            : contentType.Contains("audio/opus", StringComparison.OrdinalIgnoreCase) ? ".opus"
            : ".mp3";

    private static string? GetHeaderValue(HttpResponseMessage response, string headerName)
        => response.Headers.TryGetValues(headerName, out var values)
            ? values.FirstOrDefault()
            : null;

    private static int? ToTelemetryDurationSeconds(double? estimatedDurationSeconds)
    {
        if (!estimatedDurationSeconds.HasValue || estimatedDurationSeconds.Value <= 0)
        {
            return null;
        }

        return (int)Math.Ceiling(estimatedDurationSeconds.Value);
    }

    private static double? TryParseDouble(string? value)
        => double.TryParse(value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : null;

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

    private static string CreateCacheHash(string value)
    {
        var hash = SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(value));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private sealed record PlaybackSession(
        long Id,
        string PoiId,
        string RequestedLanguageCode,
        string UiPlaybackKey,
        CancellationToken Token);

    private sealed record CachedAudioAsset(
        string PoiId,
        string RequestedLanguageCode,
        string EffectiveLanguageCode,
        string UiPlaybackKey,
        string AudioCacheKey,
        string LocalFilePath,
        string ContentType,
        string Source,
        string? TtsLocale,
        double? EstimatedDurationSeconds,
        string PlaybackSource);

    private sealed record DownloadedAudioPayload(
        byte[] Content,
        string ContentType,
        string UiPlaybackKey,
        string AudioCacheKey,
        string EffectiveLanguageCode,
        string Source,
        string? TtsLocale,
        double? EstimatedDurationSeconds);

    private sealed class MobileRuntimeAppSettings
    {
        public string? ApiBaseUrl { get; set; }
        public Dictionary<string, string> PlatformApiBaseUrls { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    }

    private sealed class AudioCacheIndexEntry
    {
        public string UiPlaybackKey { get; set; } = string.Empty;
        public string AudioCacheKey { get; set; } = string.Empty;
        public string FileName { get; set; } = string.Empty;
        public string ContentType { get; set; } = "audio/mpeg";
        public string EffectiveLanguageCode { get; set; } = AppLanguage.FallbackLanguage;
        public string Source { get; set; } = "backend_audio";
        public string? TtsLocale { get; set; }
        public double? EstimatedDurationSeconds { get; set; }
        public DateTimeOffset CachedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    }
}
