using Microsoft.Extensions.Logging;
using Microsoft.Maui.Networking;
using VinhKhanh.MobileApp.Helpers;

namespace VinhKhanh.MobileApp.Services;

public sealed partial class FoodStreetApiDataService : IAppLifecycleAwareService
{
    public async Task<string> EnsureAllowedLanguageSelectionAsync()
    {
        var languageSettings = await _systemSettingsService.GetLanguageSettingsAsync(forceRefresh: true);
        var allowedLanguageCodes = languageSettings.Languages
            .Select(item => AppLanguage.NormalizeCode(item.Code))
            .Where(code => !string.IsNullOrWhiteSpace(code))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (allowedLanguageCodes.Count == 0)
        {
            allowedLanguageCodes.Add(AppLanguage.DefaultLanguage);
        }

        var currentLanguageCode = AppLanguage.NormalizeCode(_languageService.CurrentLanguage);
        if (allowedLanguageCodes.Contains(currentLanguageCode, StringComparer.OrdinalIgnoreCase))
        {
            return currentLanguageCode;
        }

        var fallbackLanguageCode = AppLanguage.NormalizeCode(languageSettings.DefaultLanguage);
        if (!allowedLanguageCodes.Contains(fallbackLanguageCode, StringComparer.OrdinalIgnoreCase))
        {
            fallbackLanguageCode = allowedLanguageCodes.Contains(AppLanguage.DefaultLanguage, StringComparer.OrdinalIgnoreCase)
                ? AppLanguage.DefaultLanguage
                : allowedLanguageCodes[0];
        }

        _logger.LogWarning(
            "[LanguageGuard] Current language '{CurrentLanguage}' is disabled by backend settings. Restoring to '{FallbackLanguage}'. allowed={AllowedLanguages}",
            currentLanguageCode,
            fallbackLanguageCode,
            string.Join(",", allowedLanguageCodes));
        return await _languageService.SetLanguageAsync(fallbackLanguageCode);
    }

    public Task<string> RestoreToAllowedLanguageAsync()
        => EnsureAllowedLanguageSelectionAsync();

    public async Task HandleAppResumedAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogInformation(
            "[Network] App resumed. Invalidating mobile data caches. currentLanguage={LanguageCode}; networkAccess={NetworkAccess}",
            SelectedLanguageCode,
            Connectivity.Current.NetworkAccess);
        InvalidateBootstrapSnapshot();
        await TryFlushPendingUsageEventsAsync("app_resumed", cancellationToken: cancellationToken);
    }

    private void InvalidateBootstrapSnapshot()
    {
        _bootstrapSource = null;
        _bootstrapSourceLanguageCode = null;
        _bootstrapSnapshot = null;
        _bootstrapSnapshotLanguageCode = null;
        _syncState = null;
        _lastSyncCheckAt = DateTimeOffset.MinValue;
        _poiDetailCache.Clear();
        _inflightPoiDetailLoads.Clear();
    }

    private void OnConnectivityChanged(object? sender, ConnectivityChangedEventArgs e)
    {
        _logger.LogInformation(
            "[Network] Connectivity changed for mobile data. access={NetworkAccess}; profiles={Profiles}",
            e.NetworkAccess,
            string.Join(",", e.ConnectionProfiles));
        InvalidateBootstrapSnapshot();

        if (HasAnyNetworkAccess())
        {
            _ = TryFlushPendingUsageEventsAsync("connectivity_changed");
        }
    }
}
