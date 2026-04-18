using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Extensions.Logging;
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
    private const string MissingAudioErrorCode = "missing_pre_generated_audio";
    private const string UnavailableAudioErrorCode = "audio_playback_unavailable";
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
            var localAudio = TryResolvePackagedAudioAsset(detail, normalizedLanguageCode);
            if (localAudio is not null)
            {
                _logger.LogInformation(
                    "POI audio source selected. poiId={PoiId}; requestedLanguage={RequestedLanguage}; effectiveLanguage={EffectiveLanguage}; source={Source}; path={Path}",
                    detail.Id,
                    normalizedLanguageCode,
                    localAudio.EffectiveLanguageCode,
                    localAudio.PlaybackSource,
                    localAudio.LocalFilePath);
                await PlayCachedAudioAsync(localAudio, session);
                return;
            }

            var cachedAudio = await EnsureAudioAssetAsync(detail, normalizedLanguageCode, cancellationToken);
            if (cachedAudio is not null)
            {
                await PlayCachedAudioAsync(cachedAudio, session);
                return;
            }
            _logger.LogInformation(
                "POI audio source missing. poiId={PoiId}; requestedLanguage={RequestedLanguage}",
                detail.Id,
                normalizedLanguageCode);
            await PublishErrorAsync(detail.Id, normalizedLanguageCode, MissingAudioErrorCode);
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
            var normalizedLanguageCode = AppLanguage.NormalizeCode(languageCode);
            if (TryResolvePackagedAudioAsset(detail, normalizedLanguageCode) is not null)
            {
                return;
            }

            await EnsureAudioAssetAsync(detail, normalizedLanguageCode, cancellationToken);
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
        PoiExperienceDetail detail,
        string languageCode,
        CancellationToken cancellationToken)
    {
        var uiPlaybackKey = BuildUiPlaybackKey(detail.Id, languageCode);
        var cached = await TryGetCachedAudioAsync(uiPlaybackKey);
        if (cached is not null)
        {
            _logger.LogInformation(
                "POI audio source selected. poiId={PoiId}; requestedLanguage={RequestedLanguage}; effectiveLanguage={EffectiveLanguage}; source=local_cache; path={Path}",
                detail.Id,
                languageCode,
                cached.EffectiveLanguageCode,
                cached.LocalFilePath);
            return cached with { PlaybackSource = "local_cache" };
        }

        var remoteAudio = await ResolvePreparedRemoteAudioAsync(detail, languageCode);
        if (remoteAudio is null)
        {
            return null;
        }

        var loadTask = _inflightLoads.GetOrAdd(
            uiPlaybackKey,
            _ => LoadAndCacheAudioAsync(detail.Id, languageCode, remoteAudio));

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

    private async Task<CachedAudioAsset?> LoadAndCacheAudioAsync(
        string poiId,
        string requestedLanguageCode,
        ResolvedRemoteAudio remoteAudio)
    {
        var downloadedAudio = await DownloadPreparedAudioAsync(poiId, requestedLanguageCode, remoteAudio);
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

        _logger.LogInformation(
            "POI audio cached for playback. poiId={PoiId}; requestedLanguage={RequestedLanguage}; effectiveLanguage={EffectiveLanguage}; source={Source}; cachePath={Path}",
            poiId,
            requestedLanguageCode,
            downloadedAudio.EffectiveLanguageCode,
            downloadedAudio.Source,
            targetPath);

        return new CachedAudioAsset(
            poiId,
            requestedLanguageCode,
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

    private async Task<DownloadedAudioPayload?> DownloadPreparedAudioAsync(
        string poiId,
        string requestedLanguageCode,
        ResolvedRemoteAudio remoteAudio)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, remoteAudio.AudioUri);
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
                remoteAudio.AudioUri,
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
                remoteAudio.AudioUri,
                contentType);
            return null;
        }

        if (LooksLikeTextPayload(contentType, content))
        {
            _logger.LogWarning(
                "POI narration audio request returned a non-audio payload. request={Request}; contentType={ContentType}; responseBody={ResponseBody}",
                remoteAudio.AudioUri,
                contentType,
                DecodeTextPayload(content));
            return null;
        }

        var uiPlaybackKey = GetHeaderValue(response, "X-Ui-Playback-Key")
                            ?? BuildUiPlaybackKey(poiId, requestedLanguageCode);
        var effectiveLanguageCode = AppLanguage.NormalizeCode(
            GetHeaderValue(response, "X-Effective-Language-Code") ?? remoteAudio.EffectiveLanguageCode);
        var audioCacheKey = GetHeaderValue(response, "X-Audio-Cache-Key")
                            ?? CreateCacheHash(remoteAudio.AudioUri.ToString());
        var source = GetHeaderValue(response, "X-Audio-Source") ?? remoteAudio.Source;
        var estimatedDurationSeconds = TryParseDouble(
            GetHeaderValue(response, "X-Audio-Duration-Seconds"));

        _logger.LogInformation(
            "POI pre-generated audio downloaded. poiId={PoiId}; requestedLanguage={RequestedLanguage}; effectiveLanguage={EffectiveLanguage}; source={Source}; audioBytes={AudioBytes}; cacheKey={CacheKey}; audioUrl={AudioUrl}",
            poiId,
            requestedLanguageCode,
            effectiveLanguageCode,
            source,
            content.Length,
            audioCacheKey,
            remoteAudio.AudioUri);

        return new DownloadedAudioPayload(
            content,
            contentType,
            uiPlaybackKey,
            audioCacheKey,
            effectiveLanguageCode,
            source,
            null,
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
        var normalizedMessage = NormalizePlaybackErrorMessage(message);

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
                normalizedMessage);
            _snapshot = snapshotToPublish;
        }
        finally
        {
            _stateLock.Release();
        }

        PublishSnapshotChanged(snapshotToPublish);
    }

    private static string NormalizePlaybackErrorMessage(string message)
    {
        if (string.Equals(message, MissingAudioErrorCode, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(message, UnavailableAudioErrorCode, StringComparison.OrdinalIgnoreCase))
        {
            return message;
        }

        if (message.StartsWith("Ch", StringComparison.OrdinalIgnoreCase))
        {
            return MissingAudioErrorCode;
        }

        return UnavailableAudioErrorCode;
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

    private static CachedAudioAsset? TryResolvePackagedAudioAsset(PoiExperienceDetail detail, string languageCode)
    {
        foreach (var candidate in AppLanguage.GetCandidateCodes(languageCode))
        {
            if (!detail.AudioUrls.Values.TryGetValue(candidate, out var audioLocation) ||
                string.IsNullOrWhiteSpace(audioLocation))
            {
                continue;
            }

            var localFilePath = TryResolveLocalFilePath(audioLocation);
            if (string.IsNullOrWhiteSpace(localFilePath) || !File.Exists(localFilePath))
            {
                continue;
            }

            var normalizedCandidate = AppLanguage.NormalizeCode(candidate);
            var uiPlaybackKey = BuildUiPlaybackKey(detail.Id, languageCode);
            return new CachedAudioAsset(
                detail.Id,
                languageCode,
                normalizedCandidate,
                uiPlaybackKey,
                $"offline:{detail.Id}:{normalizedCandidate}",
                localFilePath,
                GuessContentType(localFilePath),
                "offline_package",
                null,
                null,
                "offline_package");
        }

        return null;
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

    private async Task<ResolvedRemoteAudio?> ResolvePreparedRemoteAudioAsync(
        PoiExperienceDetail detail,
        string requestedLanguageCode)
    {
        var apiBaseUrl = await GetApiBaseUrlAsync();
        foreach (var candidate in AppLanguage.GetCandidateCodes(requestedLanguageCode))
        {
            if (!detail.AudioUrls.Values.TryGetValue(candidate, out var audioLocation) ||
                string.IsNullOrWhiteSpace(audioLocation))
            {
                continue;
            }

            var normalizedAudioLocation = audioLocation.Trim();
            var localFilePath = TryResolveLocalFilePath(normalizedAudioLocation);
            if (!string.IsNullOrWhiteSpace(localFilePath))
            {
                if (!File.Exists(localFilePath))
                {
                    _logger.LogDebug(
                        "Skipping missing local POI audio asset. poiId={PoiId}; requestedLanguage={RequestedLanguage}; candidateLanguage={CandidateLanguage}; path={Path}",
                        detail.Id,
                        requestedLanguageCode,
                        candidate,
                        localFilePath);
                }

                continue;
            }

            var preparedAudioUri = ResolvePreparedAudioUri(normalizedAudioLocation, apiBaseUrl);
            if (preparedAudioUri is null)
            {
                continue;
            }

            var normalizedCandidate = AppLanguage.NormalizeCode(candidate);
            _logger.LogInformation(
                "POI audio source selected. poiId={PoiId}; requestedLanguage={RequestedLanguage}; effectiveLanguage={EffectiveLanguage}; source=remote_pre_generated; audioUrl={AudioUrl}",
                detail.Id,
                requestedLanguageCode,
                normalizedCandidate,
                preparedAudioUri);
            return new ResolvedRemoteAudio(
                normalizedCandidate,
                preparedAudioUri,
                "remote_pre_generated");
        }

        return null;
    }

    private static string? TryResolveLocalFilePath(string value)
    {
        var normalizedValue = value.Trim();
        if (Path.IsPathRooted(normalizedValue))
        {
            return normalizedValue;
        }

        return Uri.TryCreate(normalizedValue, UriKind.Absolute, out var uri) && uri.IsFile
            ? uri.LocalPath
            : null;
    }

    private static Uri? ResolvePreparedAudioUri(string audioLocation, string? apiBaseUrl)
    {
        if (Uri.TryCreate(audioLocation, UriKind.Absolute, out var absoluteUri))
        {
            return absoluteUri.IsFile ? null : absoluteUri;
        }

        if (string.IsNullOrWhiteSpace(apiBaseUrl))
        {
            return null;
        }

        return Uri.TryCreate(new Uri(apiBaseUrl, UriKind.Absolute), audioLocation, out var combinedUri)
            ? combinedUri
            : null;
    }

    private static string GuessContentType(string path)
        => Path.GetExtension(path).ToLowerInvariant() switch
        {
            ".wav" => "audio/wav",
            ".ogg" => "audio/ogg",
            ".opus" => "audio/opus",
            _ => "audio/mpeg"
        };

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

    private sealed record ResolvedRemoteAudio(
        string EffectiveLanguageCode,
        Uri AudioUri,
        string Source);

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
        public string Source { get; set; } = "remote_pre_generated";
        public string? TtsLocale { get; set; }
        public double? EstimatedDurationSeconds { get; set; }
        public DateTimeOffset CachedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    }
}
