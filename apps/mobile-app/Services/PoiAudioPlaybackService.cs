using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Maui.Networking;
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
    Paused,
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

    public bool IsBusy => Status is PoiAudioPlaybackStatus.Loading
        or PoiAudioPlaybackStatus.Playing
        or PoiAudioPlaybackStatus.Paused;

    public bool Matches(string? poiId, string? languageCode) =>
        !string.IsNullOrWhiteSpace(PoiId) &&
        !string.IsNullOrWhiteSpace(poiId) &&
        string.Equals(PoiId, poiId, StringComparison.OrdinalIgnoreCase) &&
        string.Equals(
            AppLanguage.NormalizeCode(RequestedLanguageCode ?? string.Empty),
            AppLanguage.NormalizeCode(languageCode ?? string.Empty),
            StringComparison.OrdinalIgnoreCase);

    public bool MatchesAudioKey(string? audioCacheKey) =>
        !string.IsNullOrWhiteSpace(AudioCacheKey) &&
        !string.IsNullOrWhiteSpace(audioCacheKey) &&
        string.Equals(AudioCacheKey, audioCacheKey, StringComparison.OrdinalIgnoreCase);
}

public interface IPoiAudioPlaybackService
{
    PoiAudioPlaybackSnapshot Snapshot { get; }
    bool IsPlaying { get; }
    bool IsBusy { get; }
    event EventHandler<PoiAudioPlaybackSnapshot>? PlaybackStateChanged;

    Task PlayAsync(PoiExperienceDetail detail, string languageCode, CancellationToken cancellationToken = default);
    Task ToggleAsync(PoiExperienceDetail detail, string languageCode, CancellationToken cancellationToken = default);
    Task PauseAsync(CancellationToken cancellationToken = default);
    Task ResumeAsync(CancellationToken cancellationToken = default);
    Task PreloadAsync(PoiExperienceDetail detail, string languageCode, CancellationToken cancellationToken = default);
    Task StopAsync();
}

public sealed partial class PoiAudioPlaybackService : IPoiAudioPlaybackService, IAppLifecycleAwareService
{
    private const string AppSettingsFileName = "appsettings.json";
    private const string CacheIndexFileName = "poi-audio-cache-index.json";
    private const string MissingAudioErrorCode = "missing_pre_generated_audio";
    private const string UnavailableAudioErrorCode = "audio_playback_unavailable";
    private static readonly TimeSpan AudioRequestTimeout = TimeSpan.FromSeconds(45);
    private static readonly TimeSpan DuplicateRequestCooldown = TimeSpan.FromMilliseconds(350);

    private readonly SemaphoreSlim _stateLock = new(1, 1);
    private readonly SemaphoreSlim _cacheLock = new(1, 1);
    private readonly SemaphoreSlim _offlineInstallationLock = new(1, 1);
    private readonly JsonSerializerOptions _jsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = false
    };
    private readonly IAudioManager _audioManager;
    private readonly IFoodStreetDataService _dataService;
    private readonly IOfflineStorageService _offlineStorageService;
    private readonly IOfflinePackageService _offlinePackageService;
    private readonly IMobileApiBaseUrlService _apiBaseUrlService;
    private readonly ILogger<PoiAudioPlaybackService> _logger;
    private readonly HttpClient _httpClient;
    private readonly ConcurrentDictionary<string, Task<CachedAudioAsset?>> _inflightLoads = new(StringComparer.OrdinalIgnoreCase);

    private MobileRuntimeAppSettings? _runtimeSettings;
    private PoiAudioPlaybackSnapshot _snapshot = PoiAudioPlaybackSnapshot.Idle;
    private CancellationTokenSource? _activeSessionSource;
    private IAudioPlayer? _audioPlayer;
    private Stream? _audioStream;
    private TaskCompletionSource<PlaybackCompletion>? _playbackCompletion;
    private EventHandler? _playbackEndedHandler;
    private EventHandler? _playbackErrorHandler;
    private long _sessionId;
    private DateTimeOffset _lastAcceptedRequestAt = DateTimeOffset.MinValue;
    private string? _lastAcceptedRequestKey;
    private Dictionary<string, AudioCacheIndexEntry>? _cacheIndex;
    private OfflinePackageInstallation? _offlineInstallation;
    private bool _offlineInstallationLoaded;

    public PoiAudioPlaybackService(
        IAudioManager audioManager,
        IFoodStreetDataService dataService,
        IOfflineStorageService offlineStorageService,
        IOfflinePackageService offlinePackageService,
        IMobileApiBaseUrlService apiBaseUrlService,
        ILogger<PoiAudioPlaybackService> logger)
    {
        _audioManager = audioManager;
        _dataService = dataService;
        _offlineStorageService = offlineStorageService;
        _offlinePackageService = offlinePackageService;
        _apiBaseUrlService = apiBaseUrlService;
        _logger = logger;
        _httpClient = new HttpClient(new HttpClientHandler
        {
            ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator
        })
        {
            Timeout = Timeout.InfiniteTimeSpan
        };
        _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Linux; Android 12; VinhKhanhMobile)");
        _offlinePackageService.StateChanged += OnOfflinePackageStateChanged;
        Connectivity.Current.ConnectivityChanged += OnConnectivityChanged;
    }

    public PoiAudioPlaybackSnapshot Snapshot => _snapshot;

    public bool IsPlaying => Snapshot.Status == PoiAudioPlaybackStatus.Playing;

    public bool IsBusy => Snapshot.IsBusy;

    public event EventHandler<PoiAudioPlaybackSnapshot>? PlaybackStateChanged;

    public async Task HandleAppResumedAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogInformation(
            "[Network] App resumed for POI audio playback. networkAccess={NetworkAccess}",
            Connectivity.Current.NetworkAccess);
        InvalidateOfflineInstallationCache();
        await ResetErrorStateIfNeededAsync("app_resumed", cancellationToken);
    }

    public async Task ToggleAsync(
        PoiExperienceDetail detail,
        string languageCode,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(detail);

        var request = BuildPlaybackRequest(detail, languageCode);
        var currentSnapshot = Snapshot;
        if (currentSnapshot.Status == PoiAudioPlaybackStatus.Loading &&
            currentSnapshot.MatchesAudioKey(request.RequestKey))
        {
            _logger.LogDebug(
                "Ignoring duplicate POI audio toggle while loading. poiId={PoiId}; requestedLanguage={RequestedLanguage}; cacheKey={CacheKey}",
                request.PoiId,
                request.RequestedLanguageCode,
                request.RequestKey);
            return;
        }

        if (currentSnapshot.Status == PoiAudioPlaybackStatus.Playing &&
            currentSnapshot.MatchesAudioKey(request.RequestKey))
        {
            await PauseAsync(cancellationToken);
            return;
        }

        if (currentSnapshot.Status == PoiAudioPlaybackStatus.Paused &&
            currentSnapshot.MatchesAudioKey(request.RequestKey))
        {
            await ResumeAsync(cancellationToken);
            return;
        }

        await PlayAsync(detail, languageCode, cancellationToken);
    }

    public async Task PlayAsync(
        PoiExperienceDetail detail,
        string languageCode,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(detail);

        var request = BuildPlaybackRequest(detail, languageCode);
        _logger.LogInformation(
            "[AudioRequest] poiId={PoiId}; requestedLang={RequestedLanguage}; normalizedLang={NormalizedLanguage}; candidateCount={CandidateCount}; requestKey={RequestKey}",
            detail.Id,
            languageCode,
            request.RequestedLanguageCode,
            request.Candidates.Count,
            request.RequestKey);
        var session = await TryBeginPlaybackSessionAsync(request, cancellationToken);
        if (session is null)
        {
            return;
        }

        try
        {
            var resolution = await ResolveAudioForPlaybackAsync(request, session.Token);
            if (resolution.Audio is null)
            {
                _logger.LogInformation(
                    "POI audio source unavailable. poiId={PoiId}; requestedLanguage={RequestedLanguage}; requestKey={RequestKey}; failureCode={FailureCode}",
                    request.PoiId,
                    request.RequestedLanguageCode,
                    request.RequestKey,
                    resolution.FailureCode ?? MissingAudioErrorCode);
                await PublishErrorAsync(session, resolution.FailureCode ?? MissingAudioErrorCode);
                return;
            }

            await PlayLocalAudioAsync(resolution.Audio, session);
        }
        catch (OperationCanceledException) when (session.Token.IsCancellationRequested)
        {
            _logger.LogDebug(
                "POI audio request canceled. poiId={PoiId}; requestedLanguage={RequestedLanguage}; requestKey={RequestKey}",
                request.PoiId,
                request.RequestedLanguageCode,
                request.RequestKey);
        }
        catch (Exception exception)
        {
            _logger.LogWarning(
                exception,
                "Unable to play POI narration. poiId={PoiId}; requestedLanguage={RequestedLanguage}; requestKey={RequestKey}",
                request.PoiId,
                request.RequestedLanguageCode,
                request.RequestKey);
            await PublishErrorAsync(session, UnavailableAudioErrorCode);
        }
    }

    public async Task PauseAsync(CancellationToken cancellationToken = default)
    {
        PoiAudioPlaybackSnapshot? snapshotToPublish = null;

        await _stateLock.WaitAsync(cancellationToken);
        try
        {
            if (_snapshot.Status != PoiAudioPlaybackStatus.Playing || _audioPlayer is null)
            {
                return;
            }

            _audioPlayer.Pause();
            snapshotToPublish = _snapshot with
            {
                Status = PoiAudioPlaybackStatus.Paused,
                ErrorMessage = null
            };
            _snapshot = snapshotToPublish;

            _logger.LogInformation(
                "POI audio paused. poiId={PoiId}; requestedLanguage={RequestedLanguage}; cacheKey={CacheKey}",
                snapshotToPublish.PoiId,
                snapshotToPublish.RequestedLanguageCode,
                snapshotToPublish.AudioCacheKey);
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

    public async Task ResumeAsync(CancellationToken cancellationToken = default)
    {
        PoiAudioPlaybackSnapshot? snapshotToPublish = null;

        await _stateLock.WaitAsync(cancellationToken);
        try
        {
            if (_snapshot.Status != PoiAudioPlaybackStatus.Paused || _audioPlayer is null)
            {
                return;
            }

            _audioPlayer.Play();
            snapshotToPublish = _snapshot with
            {
                Status = PoiAudioPlaybackStatus.Playing,
                ErrorMessage = null
            };
            _snapshot = snapshotToPublish;

            _logger.LogInformation(
                "POI audio resumed. poiId={PoiId}; requestedLanguage={RequestedLanguage}; cacheKey={CacheKey}",
                snapshotToPublish.PoiId,
                snapshotToPublish.RequestedLanguageCode,
                snapshotToPublish.AudioCacheKey);
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

    public async Task PreloadAsync(
        PoiExperienceDetail detail,
        string languageCode,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(detail);

        try
        {
            var request = BuildPlaybackRequest(detail, languageCode);
            _logger.LogDebug(
                "[AudioPreload] poiId={PoiId}; requestedLang={RequestedLanguage}; normalizedLang={NormalizedLanguage}; candidateCount={CandidateCount}; requestKey={RequestKey}",
                detail.Id,
                languageCode,
                request.RequestedLanguageCode,
                request.Candidates.Count,
                request.RequestKey);
            if (await TryGetCachedAudioAsync(request.Candidates.Select(item => item.AudioCacheKey), cancellationToken) is not null)
            {
                _logger.LogDebug(
                    "POI audio preload skipped because local cache is ready. poiId={PoiId}; requestedLanguage={RequestedLanguage}; requestKey={RequestKey}",
                    request.PoiId,
                    request.RequestedLanguageCode,
                    request.RequestKey);
                return;
            }

            if (await TryResolvePackagedAudioAssetAsync(request, cancellationToken) is not null)
            {
                _logger.LogDebug(
                    "POI audio preload skipped because packaged audio is ready. poiId={PoiId}; requestedLanguage={RequestedLanguage}; requestKey={RequestKey}",
                    request.PoiId,
                    request.RequestedLanguageCode,
                    request.RequestKey);
                return;
            }

            var remoteAudio = await ResolveAndCacheFirstRemoteAudioAsync(request, cancellationToken);
            if (remoteAudio is null)
            {
                _logger.LogDebug(
                    "POI audio preload found no remote prepared audio. poiId={PoiId}; requestedLanguage={RequestedLanguage}; requestKey={RequestKey}",
                    request.PoiId,
                    request.RequestedLanguageCode,
                    request.RequestKey);
                return;
            }

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
            DisposeActivePlayerLocked();
            snapshotToPublish = string.IsNullOrWhiteSpace(_snapshot.PoiId)
                ? PoiAudioPlaybackSnapshot.Idle
                : _snapshot with
                {
                    Status = PoiAudioPlaybackStatus.Stopped,
                    Source = null,
                    ErrorMessage = null
                };
            _snapshot = snapshotToPublish;

            _logger.LogInformation(
                "POI audio stopped. poiId={PoiId}; requestedLanguage={RequestedLanguage}; cacheKey={CacheKey}",
                snapshotToPublish.PoiId,
                snapshotToPublish.RequestedLanguageCode,
                snapshotToPublish.AudioCacheKey);
        }
        finally
        {
            _stateLock.Release();
        }

        PublishSnapshotChanged(snapshotToPublish);
    }

    private async Task<PlaybackSession?> TryBeginPlaybackSessionAsync(
        AudioPlaybackRequest request,
        CancellationToken cancellationToken)
    {
        PoiAudioPlaybackSnapshot? snapshotToPublish = null;
        var now = DateTimeOffset.UtcNow;

        await _stateLock.WaitAsync(cancellationToken);
        try
        {
            if (_snapshot.Status == PoiAudioPlaybackStatus.Loading &&
                _snapshot.MatchesAudioKey(request.RequestKey))
            {
                _logger.LogInformation(
                    "Ignoring duplicate POI audio request while loading. poiId={PoiId}; requestedLanguage={RequestedLanguage}; cacheKey={CacheKey}",
                    request.PoiId,
                    request.RequestedLanguageCode,
                    request.RequestKey);
                return null;
            }

            if (_snapshot.Status == PoiAudioPlaybackStatus.Playing &&
                _snapshot.MatchesAudioKey(request.RequestKey))
            {
                _logger.LogInformation(
                    "Ignoring duplicate POI audio request because it is already playing. poiId={PoiId}; requestedLanguage={RequestedLanguage}; cacheKey={CacheKey}",
                    request.PoiId,
                    request.RequestedLanguageCode,
                    request.RequestKey);
                return null;
            }

            if (_snapshot.Status == PoiAudioPlaybackStatus.Paused &&
                _snapshot.MatchesAudioKey(request.RequestKey))
            {
                _audioPlayer?.Play();
                snapshotToPublish = _snapshot with
                {
                    Status = PoiAudioPlaybackStatus.Playing,
                    ErrorMessage = null
                };
                _snapshot = snapshotToPublish;
                _logger.LogInformation(
                    "Resuming paused POI audio from play request. poiId={PoiId}; requestedLanguage={RequestedLanguage}; cacheKey={CacheKey}",
                    request.PoiId,
                    request.RequestedLanguageCode,
                    request.RequestKey);
                return null;
            }

            if (string.Equals(_lastAcceptedRequestKey, request.RequestKey, StringComparison.OrdinalIgnoreCase) &&
                now - _lastAcceptedRequestAt < DuplicateRequestCooldown)
            {
                _logger.LogInformation(
                    "Ignoring rapid duplicate POI audio request. poiId={PoiId}; requestedLanguage={RequestedLanguage}; cacheKey={CacheKey}; cooldownMs={CooldownMs}",
                    request.PoiId,
                    request.RequestedLanguageCode,
                    request.RequestKey,
                    DuplicateRequestCooldown.TotalMilliseconds);
                return null;
            }

            var previousSnapshot = _snapshot;
            Interlocked.Increment(ref _sessionId);
            CancelActiveSessionLocked();
            DisposeActivePlayerLocked();
            _activeSessionSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            _lastAcceptedRequestAt = now;
            _lastAcceptedRequestKey = request.RequestKey;

            snapshotToPublish = new PoiAudioPlaybackSnapshot(
                PoiAudioPlaybackStatus.Loading,
                request.PoiId,
                request.RequestedLanguageCode,
                null,
                request.UiPlaybackKey,
                request.RequestKey,
                null,
                null);
            _snapshot = snapshotToPublish;

            if (previousSnapshot.Status is PoiAudioPlaybackStatus.Loading
                or PoiAudioPlaybackStatus.Playing
                or PoiAudioPlaybackStatus.Paused)
            {
                _logger.LogInformation(
                    "Canceling previous POI audio for a new request. previousPoiId={PreviousPoiId}; previousCacheKey={PreviousCacheKey}; nextPoiId={NextPoiId}; nextCacheKey={NextCacheKey}",
                    previousSnapshot.PoiId,
                    previousSnapshot.AudioCacheKey,
                    request.PoiId,
                    request.RequestKey);
            }

            return new PlaybackSession(
                _sessionId,
                request.PoiId,
                request.RequestedLanguageCode,
                request.UiPlaybackKey,
                request.RequestKey,
                _activeSessionSource.Token);
        }
        finally
        {
            _stateLock.Release();
            if (snapshotToPublish is not null)
            {
                PublishSnapshotChanged(snapshotToPublish);
            }
        }
    }

    private async Task<AudioResolutionResult> ResolveAudioForPlaybackAsync(
        AudioPlaybackRequest request,
        CancellationToken cancellationToken)
    {
        if (request.Candidates.Count == 0)
        {
            return new AudioResolutionResult(null, MissingAudioErrorCode);
        }

        var cached = await TryGetCachedAudioAsync(request.Candidates.Select(item => item.AudioCacheKey), cancellationToken);
        if (cached is not null)
        {
            _logger.LogInformation(
                "POI audio source selected. poiId={PoiId}; requestedLanguage={RequestedLanguage}; effectiveLanguage={EffectiveLanguage}; source=local_cache; path={Path}; cacheKey={CacheKey}",
                request.PoiId,
                request.RequestedLanguageCode,
                cached.EffectiveLanguageCode,
                cached.LocalFilePath,
                cached.AudioCacheKey);
            return new AudioResolutionResult(cached with { PlaybackSource = "local_cache" }, null);
        }

        var packagedAudio = await TryResolvePackagedAudioAssetAsync(request, cancellationToken);
        if (packagedAudio is not null)
        {
            _logger.LogInformation(
                "POI audio source selected. poiId={PoiId}; requestedLanguage={RequestedLanguage}; effectiveLanguage={EffectiveLanguage}; source=offline_package; path={Path}; cacheKey={CacheKey}",
                request.PoiId,
                request.RequestedLanguageCode,
                packagedAudio.EffectiveLanguageCode,
                packagedAudio.LocalFilePath,
                packagedAudio.AudioCacheKey);
            return new AudioResolutionResult(packagedAudio, null);
        }

        if (!HasInternetAccess())
        {
            _logger.LogInformation(
                "[AudioResolve] No usable internet access for remote fallback. poiId={PoiId}; requestedLanguage={RequestedLanguage}; candidateCount={CandidateCount}",
                request.PoiId,
                request.RequestedLanguageCode,
                request.Candidates.Count);
            return new AudioResolutionResult(null, UnavailableAudioErrorCode);
        }

        var remoteAudio = await ResolveAndCacheFirstRemoteAudioAsync(request, cancellationToken);
        return remoteAudio is not null
            ? new AudioResolutionResult(remoteAudio, null)
            : new AudioResolutionResult(null, UnavailableAudioErrorCode);
    }

    private async Task<CachedAudioAsset?> ResolveAndCacheFirstRemoteAudioAsync(
        AudioPlaybackRequest request,
        CancellationToken cancellationToken)
    {
        var apiBaseUrl = await GetApiBaseUrlAsync();
        foreach (var candidate in request.Candidates)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var preparedAudioUri = ResolvePreparedAudioUri(
                FirstNonEmpty(candidate.RemoteAudioLocation, candidate.AudioLocation),
                apiBaseUrl);
            if (preparedAudioUri is null)
            {
                _logger.LogDebug(
                    "[AudioResolve] Remote audio URI could not be resolved. poiId={PoiId}; requestedLanguage={RequestedLanguage}; effectiveLanguage={EffectiveLanguage}; audioLocation={AudioLocation}; remoteAudioLocation={RemoteAudioLocation}",
                    request.PoiId,
                    request.RequestedLanguageCode,
                    candidate.EffectiveLanguageCode,
                    candidate.AudioLocation,
                    candidate.RemoteAudioLocation ?? string.Empty);
                continue;
            }

            _logger.LogInformation(
                "POI audio source selected. poiId={PoiId}; requestedLanguage={RequestedLanguage}; effectiveLanguage={EffectiveLanguage}; source=remote_pre_generated; audioUrl={AudioUrl}; cacheKey={CacheKey}",
                request.PoiId,
                request.RequestedLanguageCode,
                candidate.EffectiveLanguageCode,
                preparedAudioUri,
                candidate.AudioCacheKey);

            var remoteAudio = new ResolvedRemoteAudio(
                candidate.EffectiveLanguageCode,
                preparedAudioUri,
                candidate.AudioCacheKey,
                "remote_pre_generated",
                candidate.EstimatedDurationSeconds);
            var cached = await EnsureRemoteAudioCachedAsync(request, remoteAudio, cancellationToken);
            if (cached is not null)
            {
                return cached;
            }

            _logger.LogWarning(
                "[AudioFallback] Remote prepared audio failed; trying next candidate. poiId={PoiId}; requestedLanguage={RequestedLanguage}; failedLanguage={FailedLanguage}; audioUrl={AudioUrl}",
                request.PoiId,
                request.RequestedLanguageCode,
                candidate.EffectiveLanguageCode,
                preparedAudioUri);
        }

        return null;
    }

    private async Task<CachedAudioAsset?> EnsureRemoteAudioCachedAsync(
        AudioPlaybackRequest request,
        ResolvedRemoteAudio remoteAudio,
        CancellationToken cancellationToken)
    {
        var loadTask = _inflightLoads.GetOrAdd(
            remoteAudio.AudioCacheKey,
            _ => LoadAndCacheAudioAsync(request, remoteAudio));

        try
        {
            return await loadTask.WaitAsync(cancellationToken);
        }
        finally
        {
            if (loadTask.IsCompleted)
            {
                _inflightLoads.TryRemove(remoteAudio.AudioCacheKey, out _);
            }
        }
    }

    private async Task<CachedAudioAsset?> LoadAndCacheAudioAsync(
        AudioPlaybackRequest request,
        ResolvedRemoteAudio remoteAudio)
    {
        var downloadedAudio = await DownloadPreparedAudioAsync(request, remoteAudio);
        if (downloadedAudio is null || downloadedAudio.Content.Length == 0)
        {
            return null;
        }

        var extension = GetFileExtension(downloadedAudio.ContentType, remoteAudio.AudioUri);
        var fileName = $"{CreateCacheHash(downloadedAudio.AudioCacheKey)}{extension}";
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
            LookupKey = downloadedAudio.AudioCacheKey,
            UiPlaybackKey = downloadedAudio.UiPlaybackKey,
            AudioCacheKey = downloadedAudio.AudioCacheKey,
            PoiId = request.PoiId,
            RequestedLanguageCode = request.RequestedLanguageCode,
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
            "POI audio cached for playback. poiId={PoiId}; requestedLanguage={RequestedLanguage}; effectiveLanguage={EffectiveLanguage}; source={Source}; cachePath={Path}; cacheKey={CacheKey}",
            request.PoiId,
            request.RequestedLanguageCode,
            downloadedAudio.EffectiveLanguageCode,
            downloadedAudio.Source,
            targetPath,
            downloadedAudio.AudioCacheKey);

        return new CachedAudioAsset(
            request.PoiId,
            request.RequestedLanguageCode,
            downloadedAudio.EffectiveLanguageCode,
            downloadedAudio.UiPlaybackKey,
            downloadedAudio.AudioCacheKey,
            targetPath,
            downloadedAudio.ContentType,
            downloadedAudio.Source,
            downloadedAudio.TtsLocale,
            downloadedAudio.EstimatedDurationSeconds,
            "local_cache");
    }

    private async Task<DownloadedAudioPayload?> DownloadPreparedAudioAsync(
        AudioPlaybackRequest request,
        ResolvedRemoteAudio remoteAudio)
    {
        using var timeoutSource = new CancellationTokenSource(AudioRequestTimeout);
        using var httpRequest = new HttpRequestMessage(HttpMethod.Get, remoteAudio.AudioUri);
        using var response = await _httpClient.SendAsync(
            httpRequest,
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

        var contentType = response.Content.Headers.ContentType?.ToString() ?? GuessContentType(remoteAudio.AudioUri.AbsolutePath);
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

        if (LooksLikeTextPayload(contentType, content) ||
            (!LooksLikeKnownAudioPayload(content) && LooksLikeTextPayload(null, content)))
        {
            _logger.LogWarning(
                "POI narration audio request returned a non-audio payload. request={Request}; contentType={ContentType}; responseBody={ResponseBody}",
                remoteAudio.AudioUri,
                contentType,
                DecodeTextPayload(content));
            return null;
        }

        var uiPlaybackKey = GetHeaderValue(response, "X-Ui-Playback-Key") ?? request.UiPlaybackKey;
        var effectiveLanguageCode = AppLanguage.NormalizeCode(
            GetHeaderValue(response, "X-Effective-Language-Code") ?? remoteAudio.EffectiveLanguageCode);
        var audioCacheKey = GetHeaderValue(response, "X-Audio-Cache-Key") ?? remoteAudio.AudioCacheKey;
        var source = GetHeaderValue(response, "X-Audio-Source") ?? remoteAudio.Source;
        var estimatedDurationSeconds =
            TryParseDouble(GetHeaderValue(response, "X-Audio-Duration-Seconds")) ??
            remoteAudio.EstimatedDurationSeconds;

        _logger.LogInformation(
            "POI prepared audio downloaded. poiId={PoiId}; requestedLanguage={RequestedLanguage}; effectiveLanguage={EffectiveLanguage}; source={Source}; audioBytes={AudioBytes}; cacheKey={CacheKey}; audioUrl={AudioUrl}",
            request.PoiId,
            request.RequestedLanguageCode,
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

    private async Task PlayLocalAudioAsync(CachedAudioAsset audio, PlaybackSession session)
    {
        IAudioPlayer? player = null;
        Stream? playerStream = null;
        TaskCompletionSource<PlaybackCompletion>? completion = null;
        EventHandler? playbackEndedHandler = null;
        EventHandler? playbackErrorHandler = null;

        try
        {
            session.Token.ThrowIfCancellationRequested();
            if (!IsUsableAudioFile(audio.LocalFilePath))
            {
                throw new FileNotFoundException("Cached POI audio file is missing or invalid.", audio.LocalFilePath);
            }

            completion = new TaskCompletionSource<PlaybackCompletion>(TaskCreationOptions.RunContinuationsAsynchronously);
            playbackEndedHandler = (_, _) => completion.TrySetResult(PlaybackCompletion.Ended);
            playbackErrorHandler = (_, _) => completion.TrySetResult(PlaybackCompletion.Error);

            PoiAudioPlaybackSnapshot snapshotToPublish;
            await _stateLock.WaitAsync(session.Token);
            try
            {
                if (session.Id != _sessionId || session.Token.IsCancellationRequested)
                {
                    return;
                }

                DisposeActivePlayerLocked();
                playerStream = File.OpenRead(audio.LocalFilePath);
                player = _audioManager.CreatePlayer(playerStream);
                player.PlaybackEnded += playbackEndedHandler;
                player.Error += playbackErrorHandler;
                player.Volume = 1d;
                player.Play();

                _audioPlayer = player;
                _audioStream = playerStream;
                playerStream = null;
                _playbackCompletion = completion;
                _playbackEndedHandler = playbackEndedHandler;
                _playbackErrorHandler = playbackErrorHandler;

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

                _logger.LogInformation(
                    "POI audio playback started. poiId={PoiId}; requestedLanguage={RequestedLanguage}; effectiveLanguage={EffectiveLanguage}; source={Source}; cacheKey={CacheKey}; file={File}",
                    session.PoiId,
                    session.RequestedLanguageCode,
                    audio.EffectiveLanguageCode,
                    audio.PlaybackSource,
                    audio.AudioCacheKey,
                    audio.LocalFilePath);
            }
            finally
            {
                _stateLock.Release();
            }

            PublishSnapshotChanged(snapshotToPublish);
            _ = TrackAudioPlaySafeAsync(session, audio);
            _ = MonitorPlaybackCompletionAsync(audio, session, player, completion);
        }
        catch (OperationCanceledException) when (session.Token.IsCancellationRequested)
        {
        }
        finally
        {
            playerStream?.Dispose();
        }
    }

    private async Task MonitorPlaybackCompletionAsync(
        CachedAudioAsset audio,
        PlaybackSession session,
        IAudioPlayer player,
        TaskCompletionSource<PlaybackCompletion> completion)
    {
        var playbackCompletion = PlaybackCompletion.Canceled;
        try
        {
            using var cancellationRegistration = session.Token.Register(
                static state => ((TaskCompletionSource<PlaybackCompletion>)state!).TrySetCanceled(),
                completion);
            playbackCompletion = await completion.Task;
        }
        catch (OperationCanceledException) when (session.Token.IsCancellationRequested)
        {
            playbackCompletion = PlaybackCompletion.Canceled;
        }
        finally
        {
            PoiAudioPlaybackSnapshot? snapshotToPublish = null;
            await _stateLock.WaitAsync();
            try
            {
                if (session.Id == _sessionId)
                {
                    if (ReferenceEquals(_audioPlayer, player))
                    {
                        DisposeActivePlayerLocked();
                    }

                    _activeSessionSource?.Dispose();
                    _activeSessionSource = null;

                    if (playbackCompletion == PlaybackCompletion.Error)
                    {
                        snapshotToPublish = _snapshot with
                        {
                            Status = PoiAudioPlaybackStatus.Error,
                            Source = null,
                            ErrorMessage = UnavailableAudioErrorCode
                        };
                        _snapshot = snapshotToPublish;
                        _logger.LogWarning(
                            "POI audio playback error. poiId={PoiId}; requestedLanguage={RequestedLanguage}; cacheKey={CacheKey}; file={File}",
                            session.PoiId,
                            session.RequestedLanguageCode,
                            audio.AudioCacheKey,
                            audio.LocalFilePath);
                    }
                    else if (playbackCompletion == PlaybackCompletion.Ended &&
                             _snapshot.Status is PoiAudioPlaybackStatus.Playing or PoiAudioPlaybackStatus.Paused or PoiAudioPlaybackStatus.Loading)
                    {
                        snapshotToPublish = _snapshot with
                        {
                            Status = PoiAudioPlaybackStatus.Stopped,
                            Source = null,
                            ErrorMessage = null
                        };
                        _snapshot = snapshotToPublish;
                        _logger.LogInformation(
                            "POI audio playback completed. poiId={PoiId}; requestedLanguage={RequestedLanguage}; cacheKey={CacheKey}",
                            session.PoiId,
                            session.RequestedLanguageCode,
                            audio.AudioCacheKey);
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

    private async Task TrackAudioPlaySafeAsync(PlaybackSession session, CachedAudioAsset audio)
    {
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
            // Telemetry must never interrupt local playback.
        }
    }

    private async Task PublishErrorAsync(PlaybackSession session, string message)
    {
        PoiAudioPlaybackSnapshot? snapshotToPublish = null;
        var normalizedMessage = NormalizePlaybackErrorMessage(message);

        await _stateLock.WaitAsync();
        try
        {
            if (session.Id != _sessionId)
            {
                return;
            }

            DisposeActivePlayerLocked();
            _activeSessionSource?.Dispose();
            _activeSessionSource = null;

            snapshotToPublish = new PoiAudioPlaybackSnapshot(
                PoiAudioPlaybackStatus.Error,
                session.PoiId,
                session.RequestedLanguageCode,
                session.RequestedLanguageCode,
                session.UiPlaybackKey,
                session.AudioCacheKey,
                null,
                normalizedMessage);
            _snapshot = snapshotToPublish;
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

    private static string NormalizePlaybackErrorMessage(string message)
    {
        if (string.Equals(message, MissingAudioErrorCode, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(message, UnavailableAudioErrorCode, StringComparison.OrdinalIgnoreCase))
        {
            return message;
        }

        return UnavailableAudioErrorCode;
    }

    private void OnOfflinePackageStateChanged(object? sender, OfflinePackageState state)
    {
        InvalidateOfflineInstallationCache();
        _logger.LogInformation(
            "[AudioResolve] Offline package state changed. status={Status}; installed={IsInstalled}; version={Version}",
            state.Status,
            state.IsInstalled,
            state.InstalledVersion);
        _ = ResetErrorStateIfNeededAsync($"offline_package_{state.Status}");
    }

    private void OnConnectivityChanged(object? sender, ConnectivityChangedEventArgs e)
    {
        InvalidateOfflineInstallationCache();
        _logger.LogInformation(
            "[Network] Connectivity changed for POI audio playback. access={NetworkAccess}; profiles={Profiles}",
            e.NetworkAccess,
            string.Join(",", e.ConnectionProfiles));
        _ = ResetErrorStateIfNeededAsync("network_changed");
    }

    private async Task ResetErrorStateIfNeededAsync(
        string reason,
        CancellationToken cancellationToken = default)
    {
        PoiAudioPlaybackSnapshot? snapshotToPublish = null;

        await _stateLock.WaitAsync(cancellationToken);
        try
        {
            if (_snapshot.Status != PoiAudioPlaybackStatus.Error)
            {
                return;
            }

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

        if (snapshotToPublish is not null)
        {
            _logger.LogInformation(
                "[AudioResolve] Cleared stale playback error. reason={Reason}; poiId={PoiId}; requestedLanguage={RequestedLanguage}",
                reason,
                snapshotToPublish.PoiId,
                snapshotToPublish.RequestedLanguageCode);
            PublishSnapshotChanged(snapshotToPublish);
        }
    }

    private void CancelActiveSessionLocked()
    {
        _activeSessionSource?.Cancel();
        _activeSessionSource?.Dispose();
        _activeSessionSource = null;
        _playbackCompletion?.TrySetCanceled();
    }

    private void DisposeActivePlayerLocked()
    {
        if (_audioPlayer is not null)
        {
            if (_playbackEndedHandler is not null)
            {
                _audioPlayer.PlaybackEnded -= _playbackEndedHandler;
            }

            if (_playbackErrorHandler is not null)
            {
                _audioPlayer.Error -= _playbackErrorHandler;
            }

            try
            {
                _audioPlayer.Stop();
            }
            catch
            {
                // Best effort cleanup. The player is disposed immediately below.
            }

            try
            {
                _audioPlayer.Dispose();
            }
            catch
            {
                // Some platform players can throw during teardown after a native error.
            }
        }

        _audioPlayer = null;
        if (_audioStream is not null)
        {
            try
            {
                _audioStream.Dispose();
            }
            catch
            {
                // Stream cleanup is best effort because native players can keep handles briefly.
            }
        }

        _audioStream = null;
        _playbackCompletion = null;
        _playbackEndedHandler = null;
        _playbackErrorHandler = null;
    }

    private void PublishSnapshotChanged(PoiAudioPlaybackSnapshot snapshot)
        => PlaybackStateChanged?.Invoke(this, snapshot);

    private async Task<CachedAudioAsset?> TryGetCachedAudioAsync(
        IEnumerable<string> lookupKeys,
        CancellationToken cancellationToken)
    {
        await _cacheLock.WaitAsync(cancellationToken);
        try
        {
            _cacheIndex ??= await LoadCacheIndexAsync(cancellationToken);
            foreach (var lookupKey in lookupKeys.Where(item => !string.IsNullOrWhiteSpace(item)))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!_cacheIndex.TryGetValue(lookupKey, out var indexEntry))
                {
                    continue;
                }

                var filePath = Path.Combine(GetCacheDirectoryPath(), indexEntry.FileName);
                if (IsUsableAudioFile(filePath))
                {
                    return new CachedAudioAsset(
                        string.IsNullOrWhiteSpace(indexEntry.PoiId) ? ResolvePoiId(indexEntry.UiPlaybackKey) : indexEntry.PoiId,
                        string.IsNullOrWhiteSpace(indexEntry.RequestedLanguageCode) ? ResolveLanguageCode(indexEntry.UiPlaybackKey) : indexEntry.RequestedLanguageCode,
                        AppLanguage.NormalizeCode(indexEntry.EffectiveLanguageCode),
                        indexEntry.UiPlaybackKey,
                        indexEntry.AudioCacheKey,
                        filePath,
                        indexEntry.ContentType,
                        indexEntry.Source,
                        indexEntry.TtsLocale,
                        indexEntry.EstimatedDurationSeconds,
                        "local_cache");
                }

                _logger.LogDebug(
                    "Removing stale POI audio cache index entry. lookupKey={LookupKey}; invalidPath={Path}",
                    lookupKey,
                    filePath);
                TryDeleteFile(filePath);
                _cacheIndex.Remove(lookupKey);
                await SaveCacheIndexAsync(_cacheIndex, cancellationToken);
            }

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
            _cacheIndex ??= await LoadCacheIndexAsync(CancellationToken.None);
            _cacheIndex[ResolveIndexLookupKey(indexEntry)] = indexEntry;
            await SaveCacheIndexAsync(_cacheIndex, CancellationToken.None);
        }
        finally
        {
            _cacheLock.Release();
        }
    }

    private async Task<Dictionary<string, AudioCacheIndexEntry>> LoadCacheIndexAsync(CancellationToken cancellationToken)
    {
        var indexPath = GetCacheIndexPath();
        if (!File.Exists(indexPath))
        {
            return new Dictionary<string, AudioCacheIndexEntry>(StringComparer.OrdinalIgnoreCase);
        }

        try
        {
            var content = await File.ReadAllTextAsync(indexPath, cancellationToken);
            var entries = JsonSerializer.Deserialize<List<AudioCacheIndexEntry>>(content, _jsonOptions) ?? [];
            return entries
                .Where(item => !string.IsNullOrWhiteSpace(ResolveIndexLookupKey(item)))
                .GroupBy(ResolveIndexLookupKey, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(group => group.Key, group => group.Last(), StringComparer.OrdinalIgnoreCase);
        }
        catch
        {
            return new Dictionary<string, AudioCacheIndexEntry>(StringComparer.OrdinalIgnoreCase);
        }
    }

    private async Task SaveCacheIndexAsync(
        Dictionary<string, AudioCacheIndexEntry> index,
        CancellationToken cancellationToken)
    {
        var indexPath = GetCacheIndexPath();
        Directory.CreateDirectory(Path.GetDirectoryName(indexPath)!);
        var payload = JsonSerializer.Serialize(
            index.Values.OrderBy(ResolveIndexLookupKey, StringComparer.OrdinalIgnoreCase).ToArray(),
            _jsonOptions);
        await File.WriteAllTextAsync(indexPath, payload, cancellationToken);
    }

    private static string ResolveIndexLookupKey(AudioCacheIndexEntry indexEntry)
        => FirstNonEmpty(indexEntry.LookupKey, indexEntry.AudioCacheKey, indexEntry.UiPlaybackKey);

    private async Task<CachedAudioAsset?> TryResolvePackagedAudioAssetAsync(
        AudioPlaybackRequest request,
        CancellationToken cancellationToken)
    {
        var installation = await GetOfflineInstallationAsync(cancellationToken);
        var assetMap = installation?.AssetMap;

        foreach (var candidate in request.Candidates)
        {
            string? localFilePath = null;
            if (assetMap is not null &&
                OfflineAssetUrlHelper.TryResolveAssetPath(
                    assetMap,
                    FirstNonEmpty(candidate.RemoteAudioLocation, candidate.AudioLocation),
                    out var mappedPath))
            {
                localFilePath = mappedPath;
            }

            localFilePath ??= TryResolveLocalFilePath(candidate.AudioLocation);

            if (string.IsNullOrWhiteSpace(localFilePath))
            {
                _logger.LogDebug(
                    "[AudioResolve] Offline package has no local match. poiId={PoiId}; requestedLanguage={RequestedLanguage}; effectiveLanguage={EffectiveLanguage}; audioLocation={AudioLocation}; remoteAudioLocation={RemoteAudioLocation}",
                    request.PoiId,
                    request.RequestedLanguageCode,
                    candidate.EffectiveLanguageCode,
                    candidate.AudioLocation,
                    candidate.RemoteAudioLocation ?? string.Empty);
                continue;
            }

            if (!IsUsableAudioFile(localFilePath))
            {
                _logger.LogDebug(
                    "[AudioResolve] Skipping missing or invalid local POI audio asset. poiId={PoiId}; requestedLanguage={RequestedLanguage}; effectiveLanguage={EffectiveLanguage}; path={Path}",
                    request.PoiId,
                    request.RequestedLanguageCode,
                    candidate.EffectiveLanguageCode,
                    localFilePath);
                continue;
            }

            return new CachedAudioAsset(
                request.PoiId,
                request.RequestedLanguageCode,
                candidate.EffectiveLanguageCode,
                request.UiPlaybackKey,
                candidate.AudioCacheKey,
                localFilePath,
                GuessContentType(localFilePath),
                "offline_package",
                null,
                candidate.EstimatedDurationSeconds,
                "offline_package");
        }

        return null;
    }

    private async Task<OfflinePackageInstallation?> GetOfflineInstallationAsync(CancellationToken cancellationToken)
    {
        if (_offlineInstallationLoaded)
        {
            return _offlineInstallation;
        }

        await _offlineInstallationLock.WaitAsync(cancellationToken);
        try
        {
            if (_offlineInstallationLoaded)
            {
                return _offlineInstallation;
            }

            _offlineInstallation = await _offlineStorageService.LoadInstallationAsync(cancellationToken);
            _offlineInstallationLoaded = true;
            return _offlineInstallation;
        }
        finally
        {
            _offlineInstallationLock.Release();
        }
    }

    private void InvalidateOfflineInstallationCache()
    {
        _offlineInstallation = null;
        _offlineInstallationLoaded = false;
    }

    private async Task<string?> GetApiBaseUrlAsync()
    {
        var nextBaseUrl = MobileApiEndpointHelper.EnsureTrailingSlash(await _apiBaseUrlService.GetBaseUrlAsync());
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

    private async Task<ResolvedRemoteAudio?> ResolvePreparedRemoteAudioAsync(AudioPlaybackRequest request)
    {
        var apiBaseUrl = await GetApiBaseUrlAsync();
        foreach (var candidate in request.Candidates)
        {
            var preparedAudioUri = ResolvePreparedAudioUri(
                FirstNonEmpty(candidate.RemoteAudioLocation, candidate.AudioLocation),
                apiBaseUrl);
            if (preparedAudioUri is null)
            {
                continue;
            }

            _logger.LogInformation(
                "POI audio source selected. poiId={PoiId}; requestedLanguage={RequestedLanguage}; effectiveLanguage={EffectiveLanguage}; source=remote_pre_generated; audioUrl={AudioUrl}; cacheKey={CacheKey}",
                request.PoiId,
                request.RequestedLanguageCode,
                candidate.EffectiveLanguageCode,
                preparedAudioUri,
                candidate.AudioCacheKey);
            return new ResolvedRemoteAudio(
                candidate.EffectiveLanguageCode,
                preparedAudioUri,
                candidate.AudioCacheKey,
                "remote_pre_generated",
                candidate.EstimatedDurationSeconds);
        }

        return null;
    }

    private AudioPlaybackRequest BuildPlaybackRequest(PoiExperienceDetail detail, string languageCode)
    {
        var normalizedLanguageCode = AppLanguage.NormalizeCode(languageCode);
        var uiPlaybackKey = BuildUiPlaybackKey(detail.Id, normalizedLanguageCode);
        var candidates = new List<AudioSourceCandidate>();

        foreach (var candidateLanguage in LocalizationFallbackPolicy.GetDisplayTextFallbackCandidates(normalizedLanguageCode))
        {
            var normalizedCandidateLanguage = AppLanguage.NormalizeCode(candidateLanguage);
            PoiAudioAsset? audioAsset = null;
            string? audioLocation = null;
            string? remoteAudioLocation = null;
            if (detail.AudioAssets.TryGetValue(normalizedCandidateLanguage, out var richAsset))
            {
                audioAsset = richAsset;
                audioLocation = richAsset.AudioUrl;
                remoteAudioLocation = FirstNonEmpty(richAsset.RemoteAudioUrl, richAsset.AudioUrl);
            }
            else if (detail.AudioUrls.Values.TryGetValue(candidateLanguage, out var legacyLocation) ||
                     detail.AudioUrls.Values.TryGetValue(normalizedCandidateLanguage, out legacyLocation))
            {
                audioLocation = legacyLocation;
                remoteAudioLocation = legacyLocation;
            }

            if (string.IsNullOrWhiteSpace(audioLocation))
            {
                continue;
            }

            var audioCacheKey = BuildAudioCacheKey(
                detail.Id,
                normalizedLanguageCode,
                normalizedCandidateLanguage,
                audioLocation,
                audioAsset);
            if (candidates.Any(item => string.Equals(item.AudioCacheKey, audioCacheKey, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            candidates.Add(new AudioSourceCandidate(
                normalizedCandidateLanguage,
                audioLocation.Trim(),
                string.IsNullOrWhiteSpace(remoteAudioLocation) ? null : remoteAudioLocation.Trim(),
                audioCacheKey,
                audioAsset?.SourceType ?? "generated",
                null,
                audioAsset?.DurationInSeconds));
        }

        if (candidates.Count > 0 &&
            !string.Equals(candidates[0].EffectiveLanguageCode, normalizedLanguageCode, StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogInformation(
                "[AudioFallback] poiId={PoiId}; requestedLanguage={RequestedLanguage}; selectedLanguage={EffectiveLanguage}; candidateLanguages={CandidateLanguages}; reason=missing_requested_audio",
                detail.Id,
                normalizedLanguageCode,
                candidates[0].EffectiveLanguageCode,
                string.Join(",", candidates.Select(item => item.EffectiveLanguageCode)));
        }

        var requestKey = candidates.FirstOrDefault()?.AudioCacheKey
                         ?? BuildMissingAudioKey(detail.Id, normalizedLanguageCode);
        return new AudioPlaybackRequest(
            detail.Id,
            normalizedLanguageCode,
            uiPlaybackKey,
            requestKey,
            candidates);
    }

    private static string BuildAudioCacheKey(
        string poiId,
        string requestedLanguageCode,
        string effectiveLanguageCode,
        string audioLocation,
        PoiAudioAsset? audioAsset)
    {
        var fingerprint = FirstNonEmpty(
            audioAsset?.ContentVersion,
            audioAsset?.TextHash,
            audioAsset is null || audioAsset.UpdatedAt == default
                ? string.Empty
                : $"{audioAsset.AudioGuideId}:{audioAsset.UpdatedAt:O}",
            TryBuildLocalFileFingerprint(audioLocation),
            CreateCacheHash(audioLocation.Trim()));

        return string.Join(
            ":",
            "poi",
            NormalizeKeySegment(poiId),
            "requested",
            NormalizeKeySegment(requestedLanguageCode),
            "effective",
            NormalizeKeySegment(effectiveLanguageCode),
            "v",
            CreateCacheHash(fingerprint));
    }

    private static string BuildMissingAudioKey(string poiId, string requestedLanguageCode)
        => string.Join(
            ":",
            "poi",
            NormalizeKeySegment(poiId),
            "requested",
            NormalizeKeySegment(requestedLanguageCode),
            "missing");

    private static string? TryBuildLocalFileFingerprint(string audioLocation)
    {
        var localFilePath = TryResolveLocalFilePath(audioLocation);
        if (string.IsNullOrWhiteSpace(localFilePath) || !File.Exists(localFilePath))
        {
            return null;
        }

        var fileInfo = new FileInfo(localFilePath);
        return $"file:{fileInfo.Length}:{fileInfo.LastWriteTimeUtc.Ticks}";
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

    private static bool HasInternetAccess()
        => Connectivity.Current.NetworkAccess == NetworkAccess.Internet;

    private static Uri? ResolvePreparedAudioUri(string audioLocation, string? apiBaseUrl)
    {
        var normalizedAudioLocation = MobileApiEndpointHelper.NormalizeAssetUrl(audioLocation, apiBaseUrl);
        if (Uri.TryCreate(normalizedAudioLocation, UriKind.Absolute, out var absoluteUri))
        {
            return absoluteUri.IsFile ? null : absoluteUri;
        }

        if (string.IsNullOrWhiteSpace(apiBaseUrl))
        {
            return null;
        }

        return Uri.TryCreate(new Uri(apiBaseUrl, UriKind.Absolute), normalizedAudioLocation, out var combinedUri)
            ? combinedUri
            : null;
    }

    private static string GuessContentType(string path)
        => Path.GetExtension(path).ToLowerInvariant() switch
        {
            ".wav" => "audio/wav",
            ".ogg" => "audio/ogg",
            ".opus" => "audio/opus",
            ".aac" => "audio/aac",
            ".flac" => "audio/flac",
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

    private static string GetFileExtension(string contentType, Uri? uri = null)
    {
        if (uri is not null)
        {
            var extension = Path.GetExtension(uri.AbsolutePath);
            if (!string.IsNullOrWhiteSpace(extension) && extension.Length <= 8)
            {
                return extension;
            }
        }

        return contentType.Contains("audio/wav", StringComparison.OrdinalIgnoreCase) ? ".wav"
            : contentType.Contains("audio/ogg", StringComparison.OrdinalIgnoreCase) ? ".ogg"
            : contentType.Contains("audio/opus", StringComparison.OrdinalIgnoreCase) ? ".opus"
            : contentType.Contains("audio/aac", StringComparison.OrdinalIgnoreCase) ? ".aac"
            : contentType.Contains("audio/flac", StringComparison.OrdinalIgnoreCase) ? ".flac"
            : ".mp3";
    }

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

    private static bool IsUsableAudioFile(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
        {
            return false;
        }

        try
        {
            var fileInfo = new FileInfo(filePath);
            if (fileInfo.Length < 128)
            {
                return false;
            }

            var headerLength = (int)Math.Min(64, fileInfo.Length);
            var header = new byte[headerLength];
            using var stream = File.OpenRead(filePath);
            var bytesRead = stream.Read(header, 0, header.Length);
            if (bytesRead <= 0)
            {
                return false;
            }

            if (bytesRead != header.Length)
            {
                Array.Resize(ref header, bytesRead);
            }

            return LooksLikeKnownAudioPayload(header);
        }
        catch
        {
            return false;
        }
    }

    private static bool LooksLikeKnownAudioPayload(byte[] content)
    {
        if (content.Length >= 3 &&
            content[0] == (byte)'I' &&
            content[1] == (byte)'D' &&
            content[2] == (byte)'3')
        {
            return true;
        }

        if (content.Length >= 2 && content[0] == 0xFF && (content[1] & 0xE0) == 0xE0)
        {
            return true;
        }

        if (content.Length >= 4 &&
            content[0] == (byte)'R' &&
            content[1] == (byte)'I' &&
            content[2] == (byte)'F' &&
            content[3] == (byte)'F')
        {
            return true;
        }

        if (content.Length >= 4 &&
            content[0] == (byte)'O' &&
            content[1] == (byte)'g' &&
            content[2] == (byte)'g' &&
            content[3] == (byte)'S')
        {
            return true;
        }

        if (content.Length >= 4 &&
            content[0] == (byte)'f' &&
            content[1] == (byte)'L' &&
            content[2] == (byte)'a' &&
            content[3] == (byte)'C')
        {
            return true;
        }

        if (content.Length >= 4 &&
            content[0] == (byte)'A' &&
            content[1] == (byte)'D' &&
            content[2] == (byte)'I' &&
            content[3] == (byte)'F')
        {
            return true;
        }

        if (content.Length >= 12 &&
            content[4] == (byte)'f' &&
            content[5] == (byte)'t' &&
            content[6] == (byte)'y' &&
            content[7] == (byte)'p')
        {
            return true;
        }

        return false;
    }

    private static string DecodeTextPayload(byte[] content)
        => content.Length == 0
            ? string.Empty
            : System.Text.Encoding.UTF8.GetString(content).Trim();

    private static void TryDeleteFile(string filePath)
    {
        try
        {
            if (File.Exists(filePath))
            {
                File.Delete(filePath);
            }
        }
        catch
        {
            // Cache cleanup must not block playback fallback.
        }
    }

    private static string CreateCacheHash(string value)
    {
        var hash = SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(value));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static string NormalizeKeySegment(string value)
    {
        var builder = new StringBuilder();
        foreach (var character in value.Trim())
        {
            if (char.IsAsciiLetterOrDigit(character))
            {
                builder.Append(char.ToLowerInvariant(character));
            }
            else if (builder.Length == 0 || builder[^1] != '-')
            {
                builder.Append('-');
            }
        }

        return builder.ToString().Trim('-');
    }

    private static string FirstNonEmpty(params string?[] values)
        => values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))?.Trim() ?? string.Empty;

    private enum PlaybackCompletion
    {
        Ended,
        Error,
        Canceled
    }

    private sealed record PlaybackSession(
        long Id,
        string PoiId,
        string RequestedLanguageCode,
        string UiPlaybackKey,
        string AudioCacheKey,
        CancellationToken Token);

    private sealed record AudioPlaybackRequest(
        string PoiId,
        string RequestedLanguageCode,
        string UiPlaybackKey,
        string RequestKey,
        IReadOnlyList<AudioSourceCandidate> Candidates);

    private sealed record AudioSourceCandidate(
        string EffectiveLanguageCode,
        string AudioLocation,
        string? RemoteAudioLocation,
        string AudioCacheKey,
        string SourceType,
        string? TtsLocale,
        double? EstimatedDurationSeconds);

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

    private sealed record AudioResolutionResult(
        CachedAudioAsset? Audio,
        string? FailureCode);

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
        string AudioCacheKey,
        string Source,
        double? EstimatedDurationSeconds);

    private sealed class MobileRuntimeAppSettings
    {
        public string? ApiBaseUrl { get; set; }
        public Dictionary<string, string> PlatformApiBaseUrls { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    }

    private sealed class AudioCacheIndexEntry
    {
        public string LookupKey { get; set; } = string.Empty;
        public string UiPlaybackKey { get; set; } = string.Empty;
        public string AudioCacheKey { get; set; } = string.Empty;
        public string PoiId { get; set; } = string.Empty;
        public string RequestedLanguageCode { get; set; } = string.Empty;
        public string FileName { get; set; } = string.Empty;
        public string ContentType { get; set; } = "audio/mpeg";
        public string EffectiveLanguageCode { get; set; } = AppLanguage.FallbackLanguage;
        public string Source { get; set; } = "remote_pre_generated";
        public string? TtsLocale { get; set; }
        public double? EstimatedDurationSeconds { get; set; }
        public DateTimeOffset CachedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    }
}
