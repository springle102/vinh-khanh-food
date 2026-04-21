using Microsoft.Extensions.Logging;
using Microsoft.Maui.Networking;
using VinhKhanh.MobileApp.Helpers;

namespace VinhKhanh.MobileApp.Services;

public sealed partial class FoodStreetApiDataService : IAppLifecycleAwareService
{
    public async Task<string> EnsureAllowedLanguageSelectionAsync()
    {
        var snapshot = await GetBootstrapSnapshotAsync();
        var appSupportedLanguageCodes = AppLanguage.SupportedLanguages
            .Select(item => AppLanguage.NormalizeCode(item.Code))
            .Where(code => !string.IsNullOrWhiteSpace(code))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (appSupportedLanguageCodes.Count == 0)
        {
            appSupportedLanguageCodes.Add(AppLanguage.DefaultLanguage);
        }

        var currentLanguageCode = AppLanguage.NormalizeCode(_languageService.CurrentLanguage);
        if (appSupportedLanguageCodes.Contains(currentLanguageCode, StringComparer.OrdinalIgnoreCase))
        {
            var backendAdvertisedLanguageCodes = snapshot?.SupportedLanguages
                .Select(item => AppLanguage.NormalizeCode(item.Code))
                .Where(code => !string.IsNullOrWhiteSpace(code))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList()
                ?? [];

            if (backendAdvertisedLanguageCodes.Count > 0 &&
                !backendAdvertisedLanguageCodes.Contains(currentLanguageCode, StringComparer.OrdinalIgnoreCase))
            {
                _logger.LogWarning(
                    "[LanguageGuard] Keeping user-selected language '{CurrentLanguage}' even though the current bootstrap does not advertise it. BackendLanguages={BackendLanguages}",
                    currentLanguageCode,
                    string.Join(",", backendAdvertisedLanguageCodes));
            }

            return currentLanguageCode;
        }

        _logger.LogWarning(
            "[LanguageGuard] Current language '{CurrentLanguage}' is not supported by the app. Restoring to an app-supported language without consulting bootstrap data.",
            currentLanguageCode);
        return await _languageService.RestoreToAllowedLanguageAsync();
    }

    public Task<string> RestoreToAllowedLanguageAsync()
        => EnsureAllowedLanguageSelectionAsync();

    public Task HandleAppResumedAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogInformation(
            "[Network] App resumed. Invalidating mobile data caches. currentLanguage={LanguageCode}; networkAccess={NetworkAccess}",
            SelectedLanguageCode,
            Connectivity.Current.NetworkAccess);
        InvalidateBootstrapSnapshot();
        return Task.CompletedTask;
    }

    private void InvalidateBootstrapSnapshot()
    {
        _offlinePackageLoadAttempted = false;
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
    }
}
