using Microsoft.Extensions.Logging;
using VinhKhanh.MobileApp.Helpers;

namespace VinhKhanh.MobileApp.Services;

public sealed partial class FoodStreetApiDataService
{
    public async Task<string> EnsureAllowedLanguageSelectionAsync()
    {
        var snapshot = await GetBootstrapSnapshotAsync();
        var languages = snapshot?.SupportedLanguages.Count > 0
            ? snapshot.SupportedLanguages
            : BuildSupportedLanguages(null);
        var supportedLanguageCodes = languages
            .Select(item => AppLanguage.NormalizeCode(item.Code))
            .Where(code => !string.IsNullOrWhiteSpace(code))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (supportedLanguageCodes.Count == 0)
        {
            supportedLanguageCodes.Add(AppLanguage.DefaultLanguage);
        }

        var currentLanguageCode = AppLanguage.NormalizeCode(_languageService.CurrentLanguage);
        if (supportedLanguageCodes.Contains(currentLanguageCode, StringComparer.OrdinalIgnoreCase))
        {
            return currentLanguageCode;
        }

        var fallbackLanguageCode = supportedLanguageCodes[0];
        _logger.LogWarning(
            "Current language '{CurrentLanguage}' is not available in the active language set. Restoring to '{FallbackLanguage}'.",
            currentLanguageCode,
            fallbackLanguageCode);
        await _languageService.SetLanguageAsync(fallbackLanguageCode);
        return fallbackLanguageCode;
    }

    public Task<string> RestoreToAllowedLanguageAsync()
        => EnsureAllowedLanguageSelectionAsync();

    private void InvalidateBootstrapSnapshot()
    {
        _offlinePackageLoadAttempted = false;
        _bootstrapSource = null;
        _bootstrapSnapshot = null;
        _bootstrapSnapshotLanguageCode = null;
        _syncState = null;
        _lastSyncCheckAt = DateTimeOffset.MinValue;
    }
}
