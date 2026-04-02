using System.Text.Json;
using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.Media;
using Microsoft.Maui.Storage;
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
    private readonly SemaphoreSlim _speechLock = new(1, 1);
    private IReadOnlyList<Locale>? _cachedLocales;

    public async Task PlayAsync(PoiExperienceDetail detail, string languageCode, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(detail);

        var audioUrl = LocalizedTextHelper.GetLocalizedText(detail.AudioUrls, languageCode);
        if (!string.IsNullOrWhiteSpace(audioUrl) &&
            Uri.TryCreate(audioUrl, UriKind.Absolute, out var audioUri))
        {
            await Launcher.Default.OpenAsync(audioUri);
            return;
        }

        var narrationText = LocalizedTextHelper.GetLocalizedText(detail.Description, languageCode);
        if (string.IsNullOrWhiteSpace(narrationText))
        {
            narrationText = LocalizedTextHelper.GetLocalizedText(detail.Summary, languageCode);
        }

        if (string.IsNullOrWhiteSpace(narrationText))
        {
            return;
        }

        await _speechLock.WaitAsync(cancellationToken);
        try
        {
            var options = new SpeechOptions();
            var locale = await ResolveLocaleAsync(languageCode);
            if (locale is not null)
            {
                options.Locale = locale;
            }

            cancellationToken.ThrowIfCancellationRequested();
            await TextToSpeech.Default.SpeakAsync(narrationText, options);
        }
        finally
        {
            _speechLock.Release();
        }
    }

    public Task StopAsync() => Task.CompletedTask;

    private async Task<Locale?> ResolveLocaleAsync(string languageCode)
    {
        _cachedLocales ??= (await TextToSpeech.Default.GetLocalesAsync()).ToList();
        if (_cachedLocales.Count == 0)
        {
            return null;
        }

        var normalized = NormalizeLanguageCode(languageCode);
        return _cachedLocales.FirstOrDefault(locale =>
                   string.Equals(locale.Language, normalized, StringComparison.OrdinalIgnoreCase))
               ?? _cachedLocales.FirstOrDefault(locale =>
                   string.Equals($"{locale.Language}-{locale.Country}", normalized, StringComparison.OrdinalIgnoreCase))
               ?? _cachedLocales.FirstOrDefault(locale =>
                   normalized.StartsWith(locale.Language, StringComparison.OrdinalIgnoreCase));
    }

    private static string NormalizeLanguageCode(string? languageCode)
    {
        if (string.IsNullOrWhiteSpace(languageCode))
        {
            return "en";
        }

        return languageCode.Trim() switch
        {
            "zh-CN" => "zh",
            "en-US" => "en",
            "ja-JP" => "ja",
            "ko-KR" => "ko",
            _ => languageCode.Trim()
        };
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
