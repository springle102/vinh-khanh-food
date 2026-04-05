using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.Storage;
using Plugin.Maui.Audio;
using VinhKhanh.MobileApp.Helpers;
using VinhKhanh.MobileApp.Models;

namespace VinhKhanh.MobileApp.Services;

public interface IPoiNarrationService
{
    Task PlayAsync(PoiExperienceDetail detail, string languageCode, CancellationToken cancellationToken = default);
    Task StopAsync();
}

public interface IPoiTourStoreService
{
    Task<bool> IsSavedAsync(string poiId);
    Task<bool> ToggleSavedAsync(string poiId);
}

public sealed class PoiNarrationService : IPoiNarrationService
{
    private const string AppSettingsFileName = "appsettings.json";
    private const string NarrationEndpointFormat = "api/v1/pois/{0}/narration?languageCode={1}&voiceType=standard";
    private const int GoogleTtsMaxChars = 180;

    private static readonly IReadOnlyDictionary<string, string> GoogleTtsLanguages =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["vi"] = "vi",
            ["en"] = "en",
            ["zh-CN"] = "zh-CN",
            ["ko"] = "ko",
            ["ja"] = "ja",
            ["fr"] = "fr"
        };

    private readonly SemaphoreSlim _stateLock = new(1, 1);
    private readonly JsonSerializerOptions _jsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };
    private readonly IAudioManager _audioManager;
    private readonly HttpClient _mediaClient;
    private HttpClient? _apiClient;
    private string? _resolvedBaseUrl;
    private MobileRuntimeAppSettings? _runtimeSettings;
    private CancellationTokenSource? _playbackCancellationSource;
    private AsyncAudioPlayer? _audioPlayer;
    private MemoryStream? _audioBuffer;
    private long _playbackId;

    public PoiNarrationService(IAudioManager audioManager)
    {
        _audioManager = audioManager;
        _mediaClient = new HttpClient(new HttpClientHandler
        {
            ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator
        })
        {
            Timeout = TimeSpan.FromSeconds(20)
        };
        _mediaClient.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Linux; Android 12; VinhKhanhMobile)");
    }

    public async Task PlayAsync(PoiExperienceDetail detail, string languageCode, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(detail);

        var requestedLanguageCode = AppLanguage.NormalizeCode(languageCode);
        var session = await BeginPlaybackSessionAsync(cancellationToken);
        try
        {
            var resolvedNarration = await TryResolveNarrationAsync(detail.Id, requestedLanguageCode, session.Token);
            var effectiveLanguageCode = AppLanguage.NormalizeCode(resolvedNarration?.EffectiveLanguageCode ?? requestedLanguageCode);
            var canUseResolvedNarration = CanUseResolvedNarration(resolvedNarration, requestedLanguageCode, effectiveLanguageCode);
            var narrationText = FirstNonEmpty(
                canUseResolvedNarration ? resolvedNarration?.TranslatedText : null,
                canUseResolvedNarration ? resolvedNarration?.TtsInputText : null,
                GetRequestedLocalizedText(detail.Description, requestedLanguageCode),
                GetRequestedLocalizedText(detail.Summary, requestedLanguageCode));

            var audioUrl = FirstNonEmpty(
                CanUseResolvedAudioGuide(resolvedNarration, requestedLanguageCode, effectiveLanguageCode)
                    ? resolvedNarration?.AudioGuide?.AudioUrl
                    : null,
                GetRequestedLocalizedText(detail.AudioUrls, requestedLanguageCode));
            if (IsPlaceholderAudioUrl(audioUrl))
            {
                audioUrl = string.Empty;
            }

            var playbackUrls = !string.IsNullOrWhiteSpace(audioUrl)
                ? new[] { audioUrl }
                : BuildGoogleTtsAudioUrls(narrationText, requestedLanguageCode);

            if (playbackUrls.Count > 0)
            {
                var playedAudio = await TryPlayRemoteAudioSequenceAsync(playbackUrls, session.PlaybackId, session.Token);
                if (playedAudio)
                {
                    return;
                }
            }

            if (string.IsNullOrWhiteSpace(narrationText))
            {
                return;
            }
        }
        catch (OperationCanceledException) when (session.Token.IsCancellationRequested)
        {
        }
        finally
        {
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

    private static bool CanUseResolvedNarration(
        PoiNarrationResponseDto? resolvedNarration,
        string requestedLanguageCode,
        string effectiveLanguageCode)
    {
        if (resolvedNarration is null)
        {
            return false;
        }

        if (string.Equals(effectiveLanguageCode, requestedLanguageCode, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return string.Equals(resolvedNarration.TranslationStatus, "auto_translated", StringComparison.OrdinalIgnoreCase) &&
               !string.IsNullOrWhiteSpace(resolvedNarration.TranslatedText);
    }

    private static bool CanUseResolvedAudioGuide(
        PoiNarrationResponseDto? resolvedNarration,
        string requestedLanguageCode,
        string effectiveLanguageCode) =>
        resolvedNarration?.AudioGuide is not null &&
        string.Equals(effectiveLanguageCode, requestedLanguageCode, StringComparison.OrdinalIgnoreCase) &&
        string.Equals(resolvedNarration.AudioGuide.Status, "ready", StringComparison.OrdinalIgnoreCase) &&
        HasPlayableRemoteAudioUrl(resolvedNarration.AudioGuide.AudioUrl);

    private static string GetRequestedLocalizedText(LocalizedTextSet source, string languageCode)
    {
        foreach (var candidate in GetRequestedLanguageCandidates(languageCode))
        {
            if (source.Values.TryGetValue(candidate, out var value) && !string.IsNullOrWhiteSpace(value))
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
        catch
        {
            return false;
        }
    }

    private async Task PlayRemoteAudioSegmentAsync(string audioUrl, long playbackId, CancellationToken cancellationToken)
    {
        using var response = await _mediaClient.GetAsync(audioUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();

        await using var networkStream = await response.Content.ReadAsStreamAsync(cancellationToken);
        var buffer = new MemoryStream();
        await networkStream.CopyToAsync(buffer, cancellationToken);
        buffer.Position = 0;

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
            _audioBuffer = buffer;
            _audioPlayer = _audioManager.CreateAsyncPlayer(_audioBuffer);
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

    private async Task<PoiNarrationResponseDto?> TryResolveNarrationAsync(string poiId, string languageCode, CancellationToken cancellationToken)
    {
        try
        {
            var client = await GetApiClientAsync();
            if (client is null)
            {
                return null;
            }

            var relativeUrl = string.Format(
                NarrationEndpointFormat,
                Uri.EscapeDataString(poiId),
                Uri.EscapeDataString(languageCode));
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

    private static IReadOnlyList<string> BuildGoogleTtsAudioUrls(string text, string languageCode)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return Array.Empty<string>();
        }

        var normalizedLanguageCode = AppLanguage.NormalizeCode(languageCode);
        if (!GoogleTtsLanguages.TryGetValue(normalizedLanguageCode, out var googleLanguage))
        {
            return Array.Empty<string>();
        }

        var chunks = SplitNarrationIntoChunks(text.Trim(), GoogleTtsMaxChars);
        if (chunks.Count == 0)
        {
            return Array.Empty<string>();
        }

        return chunks
            .Select((chunk, index) =>
            {
                var query = new Dictionary<string, string>
                {
                    ["ie"] = "UTF-8",
                    ["client"] = "tw-ob",
                    ["tl"] = googleLanguage,
                    ["q"] = chunk,
                    ["total"] = chunks.Count.ToString(),
                    ["idx"] = index.ToString(),
                    ["textlen"] = chunk.Length.ToString(),
                    ["ttsspeed"] = "1"
                };

                return "https://translate.google.com/translate_tts?" +
                    string.Join("&", query.Select(item => $"{item.Key}={Uri.EscapeDataString(item.Value)}"));
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
        public string RequestedLanguageCode { get; set; } = "vi";
        public string EffectiveLanguageCode { get; set; } = "vi";
        public string TtsInputText { get; set; } = string.Empty;
        public string? TranslatedText { get; set; }
        public string TranslationStatus { get; set; } = "stored";
        public string TtsLocale { get; set; } = "vi-VN";
        public string? FallbackMessage { get; set; }
        public NarrationAudioGuideDto? AudioGuide { get; set; }
    }

    private sealed class NarrationAudioGuideDto
    {
        public string Id { get; set; } = string.Empty;
        public string AudioUrl { get; set; } = string.Empty;
        public string VoiceType { get; set; } = "standard";
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
