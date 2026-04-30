using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Maui.Storage;
using VinhKhanh.MobileApp.Helpers;

namespace VinhKhanh.MobileApp.Services;

public interface IMobileSystemSettingsService
{
    Task<MobileLanguageSettings> GetLanguageSettingsAsync(
        bool forceRefresh = false,
        CancellationToken cancellationToken = default);

    Task<SystemContactInfo> GetContactSettingsAsync(
        bool forceRefresh = false,
        CancellationToken cancellationToken = default);

    Task<MobileOfflinePackageSettings> GetOfflinePackageSettingsAsync(
        bool forceRefresh = false,
        CancellationToken cancellationToken = default);
}

public sealed class MobileSystemSettingsService(
    IMobileApiBaseUrlService apiBaseUrlService,
    IMobileDatasetRepository mobileDatasetRepository,
    ILogger<MobileSystemSettingsService> logger) : IMobileSystemSettingsService
{
    private static readonly string[] BootstrapSettingsEndpoints = ["api/mobile/bootstrap?scope=map", "api/v1/bootstrap?scope=map"];
    private const string SnapshotCacheKey = "vinh-khanh-mobile:system-settings:snapshot";
    private const string LanguagesCacheKey = "vinh-khanh-mobile:system-settings:languages";
    private const string ContactCacheKey = "vinh-khanh-mobile:system-settings:contact";
    private static readonly string[] SettingsEndpoints = ["api/v1/mobile/settings", "api/mobile/settings", "api/v1/system-settings", "api/system-settings"];
    private static readonly string[] LanguagesEndpoints = ["api/v1/mobile/settings/languages", "api/mobile/settings/languages"];
    private static readonly string[] ContactEndpoints = ["api/v1/mobile/settings/contact", "api/mobile/settings/contact"];
    private static readonly string[] OfflinePackageEndpoints = ["api/v1/mobile/settings/offline-package", "api/mobile/settings/offline-package", "api/v1/system-settings/offline-package", "api/system-settings/offline-package"];
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

    private HttpClient? _httpClient;
    private string? _resolvedBaseUrl;
    private readonly SemaphoreSlim _snapshotRefreshLock = new(1, 1);
    private MobileSystemSettingsSnapshot? _memorySnapshot;
    private DateTimeOffset _memorySnapshotFetchedAt = DateTimeOffset.MinValue;

    public async Task<MobileLanguageSettings> GetLanguageSettingsAsync(
        bool forceRefresh = false,
        CancellationToken cancellationToken = default)
        => (await GetSettingsSnapshotAsync(forceRefresh, cancellationToken)).Languages;

    public async Task<SystemContactInfo> GetContactSettingsAsync(
        bool forceRefresh = false,
        CancellationToken cancellationToken = default)
    {
        var contact = (await GetSettingsSnapshotAsync(forceRefresh, cancellationToken)).Contact;
        LogContactInfo("[MobileSettingsApi] normalized contact", contact);
        return contact;
    }

    public async Task<MobileOfflinePackageSettings> GetOfflinePackageSettingsAsync(
        bool forceRefresh = false,
        CancellationToken cancellationToken = default)
        => (await GetSettingsSnapshotAsync(forceRefresh, cancellationToken)).OfflinePackage;

    private async Task<MobileSystemSettingsSnapshot> GetSettingsSnapshotAsync(
        bool forceRefresh,
        CancellationToken cancellationToken)
    {
        if (TryUseMemorySnapshot(forceRefresh, out var memorySnapshot))
        {
            return memorySnapshot;
        }

        var hasCachedSnapshot = TryReadCachedSnapshot(out var cached);
        if (!forceRefresh && hasCachedSnapshot)
        {
            RememberSnapshot(cached);
            return cached;
        }

        await _snapshotRefreshLock.WaitAsync(cancellationToken);
        try
        {
            if (TryUseMemorySnapshot(forceRefresh, out memorySnapshot))
            {
                return memorySnapshot;
            }

            hasCachedSnapshot = TryReadCachedSnapshot(out cached);
            if (!forceRefresh && hasCachedSnapshot)
            {
                RememberSnapshot(cached);
                return cached;
            }

            var remoteSnapshot =
                SelectUsableSnapshot(await TryFetchCanonicalSettingsSnapshotAsync(cancellationToken)) ??
                SelectUsableSnapshot(await TryFetchBootstrapSettingsSnapshotAsync(cancellationToken)) ??
                SelectUsableSnapshot(await TryFetchLegacySettingsSnapshotAsync(cancellationToken));
            if (remoteSnapshot is not null)
            {
                var normalized = NormalizeSnapshot(remoteSnapshot);
                CacheSnapshot(normalized);
                RememberSnapshot(normalized);
                return normalized;
            }

            if (hasCachedSnapshot)
            {
                RememberSnapshot(cached);
                return cached;
            }

            var localSnapshot = SelectUsableSnapshot(await TryFetchLocalBootstrapSettingsSnapshotAsync(cancellationToken));
            if (localSnapshot is not null)
            {
                var normalized = NormalizeSnapshot(localSnapshot);
                RememberSnapshot(normalized);
                return normalized;
            }

            var fallback = CreateDefaultSettingsSnapshot();
            RememberSnapshot(fallback);
            return fallback;
        }
        finally
        {
            _snapshotRefreshLock.Release();
        }
    }

    private bool TryUseMemorySnapshot(bool forceRefresh, out MobileSystemSettingsSnapshot snapshot)
    {
        if (_memorySnapshot is not null && !forceRefresh)
        {
            snapshot = _memorySnapshot;
            return true;
        }

        snapshot = null!;
        return false;
    }

    private void RememberSnapshot(MobileSystemSettingsSnapshot snapshot)
    {
        _memorySnapshot = NormalizeSnapshot(snapshot);
        _memorySnapshotFetchedAt = DateTimeOffset.UtcNow;
    }

    private async Task<MobileSystemSettingsSnapshot?> TryFetchCanonicalSettingsSnapshotAsync(
        CancellationToken cancellationToken)
    {
        try
        {
            var snapshot = await TryFetchFirstEndpointAsync<MobileSystemSettingsSnapshot>(
                SettingsEndpoints,
                cancellationToken);
            return snapshot is null ? null : NormalizeSnapshot(snapshot);
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or JsonException)
        {
            logger.LogWarning(exception, "Unable to refresh mobile system settings from canonical endpoints.");
            return null;
        }
    }

    private async Task<MobileSystemSettingsSnapshot?> TryFetchLocalBootstrapSettingsSnapshotAsync(
        CancellationToken cancellationToken)
    {
        try
        {
            var localCache = await mobileDatasetRepository.LoadBootstrapEnvelopeAsync(cancellationToken);
            return TryCreateSnapshotFromBootstrapEnvelope(localCache?.EnvelopeJson);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogDebug(exception, "Unable to refresh mobile system settings from local bootstrap cache.");
            return null;
        }
    }

    private async Task<MobileSystemSettingsSnapshot?> TryFetchBootstrapSettingsSnapshotAsync(
        CancellationToken cancellationToken)
    {
        try
        {
            var bootstrap = await TryFetchFirstEndpointAsync<BootstrapSettingsPayload>(
                BootstrapSettingsEndpoints,
                cancellationToken);
            return bootstrap?.Settings is null
                ? null
                : CreateSnapshotFromSystemSettings(bootstrap.Settings);
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or JsonException)
        {
            logger.LogWarning(exception, "Unable to refresh mobile system settings from bootstrap.");
            return null;
        }
    }

    private async Task<MobileSystemSettingsSnapshot?> TryFetchLegacySettingsSnapshotAsync(
        CancellationToken cancellationToken)
    {
        try
        {
            var languages = await TryFetchFirstEndpointAsync<MobileLanguageSettings>(
                LanguagesEndpoints,
                cancellationToken);
            var contact = await TryFetchFirstEndpointAsync<SystemContactInfo>(
                ContactEndpoints,
                cancellationToken);

            return languages is null || contact is null
                ? null
                : NormalizeSnapshot(new MobileSystemSettingsSnapshot
                {
                    Languages = languages,
                    Contact = contact,
                    OfflinePackage = await TryFetchFirstEndpointAsync<MobileOfflinePackageSettings>(
                        OfflinePackageEndpoints,
                        cancellationToken) ?? new MobileOfflinePackageSettings()
                });
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or JsonException)
        {
            logger.LogWarning(exception, "Unable to refresh mobile system settings from legacy endpoints.");
            return null;
        }
    }

    private async Task<T?> TryFetchEndpointAsync<T>(
        string endpoint,
        CancellationToken cancellationToken)
        where T : class
    {
        var client = await GetClientAsync(cancellationToken);
        if (client is null)
        {
            return null;
        }

        using var request = new HttpRequestMessage(HttpMethod.Get, AddCacheBuster(endpoint));
        request.Headers.CacheControl = new CacheControlHeaderValue
        {
            NoCache = true,
            NoStore = true,
            MaxAge = TimeSpan.Zero
        };
        request.Headers.Pragma.Add(new NameValueHeaderValue("no-cache"));

        var requestUrl = BuildAbsoluteRequestUrl(client, request.RequestUri?.ToString() ?? endpoint);
        logger.LogInformation(
            "[MobileSettingsApi] GET {Url}",
            requestUrl);
        using var response = await client.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);
        var content = await response.Content.ReadAsStringAsync(cancellationToken);
        logger.LogInformation(
            "[MobileSettingsApi] Response url={Url}; statusCode={StatusCode}; body={Body}",
            requestUrl,
            (int)response.StatusCode,
            TruncateForLog(content));
        if (!response.IsSuccessStatusCode)
        {
            logger.LogWarning(
                "Mobile system settings endpoint {Endpoint} returned status {StatusCode}.",
                endpoint,
                (int)response.StatusCode);
            return null;
        }

        var envelope = JsonSerializer.Deserialize<ApiEnvelope<T>>(content, JsonOptions);
        if (envelope?.Success != true)
        {
            logger.LogWarning(
                "[MobileSettingsApi] Endpoint {Endpoint} returned unsuccessful envelope. message={Message}",
                endpoint,
                envelope?.Message ?? string.Empty);
            return null;
        }

        LogDeserializedPayload(endpoint, envelope.Data);
        return envelope.Data;
    }

    private async Task<T?> TryFetchFirstEndpointAsync<T>(
        IEnumerable<string> endpoints,
        CancellationToken cancellationToken)
        where T : class
    {
        foreach (var endpoint in endpoints)
        {
            var payload = await TryFetchEndpointAsync<T>(endpoint, cancellationToken);
            if (payload is not null)
            {
                return payload;
            }
        }

        return null;
    }

    private void LogDeserializedPayload<T>(string endpoint, T? payload)
    {
        switch (payload)
        {
            case MobileSystemSettingsSnapshot snapshot:
                var snapshotContact = NormalizeContactInfo(snapshot.Contact);
                logger.LogInformation(
                    "[MobileSettingsApi] deserialize result endpoint={Endpoint}; languagesCount={LanguagesCount}; contactNull={ContactNull}; offlinePackageNull={OfflinePackageNull}; phone='{Phone}'; complaintGuide='{ComplaintGuide}'",
                    endpoint,
                    snapshot.Languages?.Languages?.Count ?? 0,
                    snapshot.Contact is null,
                    snapshot.OfflinePackage is null,
                    snapshotContact.Phone,
                    snapshotContact.ComplaintGuide);
                break;
            case MobileLanguageSettings languages:
                logger.LogInformation(
                    "[MobileSettingsApi] deserialize result endpoint={Endpoint}; languagesCount={LanguagesCount}; defaultLanguage={DefaultLanguage}",
                    endpoint,
                    languages.Languages?.Count ?? 0,
                    languages.DefaultLanguage);
                break;
            case SystemContactInfo contact:
                var normalizedContact = NormalizeContactInfo(contact);
                logger.LogInformation(
                    "[MobileSettingsApi] deserialize result endpoint={Endpoint}; contactNull={ContactNull}; hasContact={HasContact}; phone='{Phone}'; complaintGuide='{ComplaintGuide}'",
                    endpoint,
                    contact is null,
                    contact is not null && HasUsableContact(contact),
                    normalizedContact.Phone,
                    normalizedContact.ComplaintGuide);
                break;
            case MobileOfflinePackageSettings offlinePackage:
                var normalizedOfflinePackage = NormalizeOfflinePackageSettings(offlinePackage);
                logger.LogInformation(
                    "[MobileSettingsApi] deserialize result endpoint={Endpoint}; offlineDownloadsEnabled={DownloadsEnabled}; maxPackageSizeMb={MaxPackageSizeMb}; hasDescription={HasDescription}",
                    endpoint,
                    normalizedOfflinePackage.DownloadsEnabled,
                    normalizedOfflinePackage.MaxPackageSizeMb,
                    !string.IsNullOrWhiteSpace(normalizedOfflinePackage.Description));
                break;
            case BootstrapSettingsPayload bootstrap:
                logger.LogInformation(
                    "[MobileSettingsApi] deserialize result endpoint={Endpoint}; bootstrapSettingsNull={SettingsNull}; languagesCount={LanguagesCount}; contactNull={ContactNull}",
                    endpoint,
                    bootstrap.Settings is null,
                    bootstrap.Settings?.SupportedLanguages?.Count ?? 0,
                    bootstrap.Settings is null);
                break;
            default:
                logger.LogInformation(
                    "[MobileSettingsApi] deserialize result endpoint={Endpoint}; payloadNull={PayloadNull}",
                    endpoint,
                    payload is null);
                break;
        }
    }

    private static string BuildAbsoluteRequestUrl(HttpClient client, string relativeUrl)
        => client.BaseAddress is null
            ? relativeUrl
            : new Uri(client.BaseAddress, relativeUrl).ToString();

    private static string TruncateForLog(string? value, int maxLength = 2000)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var normalized = value.Replace("\r", string.Empty).Replace("\n", " ");
        return normalized.Length <= maxLength
            ? normalized
            : $"{normalized[..maxLength]}...";
    }

    private static string AddCacheBuster(string endpoint)
    {
        var separator = endpoint.Contains('?') ? "&" : "?";
        return $"{endpoint}{separator}_={DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}";
    }

    private static void CacheSnapshot(MobileSystemSettingsSnapshot snapshot)
    {
        Preferences.Default.Remove(SnapshotCacheKey);
        Preferences.Default.Remove(LanguagesCacheKey);
        Preferences.Default.Remove(ContactCacheKey);
        Preferences.Default.Set(SnapshotCacheKey, JsonSerializer.Serialize(snapshot, JsonOptions));
        Preferences.Default.Set(LanguagesCacheKey, JsonSerializer.Serialize(snapshot.Languages, JsonOptions));
        Preferences.Default.Set(ContactCacheKey, JsonSerializer.Serialize(snapshot.Contact, JsonOptions));
    }

    private void LogContactInfo(string message, SystemContactInfo? contact)
    {
        var normalized = NormalizeContactInfo(contact);
        logger.LogInformation(
            "{Message}. contactNull={ContactNull}; phone='{Phone}'; complaintGuide='{ComplaintGuide}'; systemName='{SystemName}'; email='{Email}'; address='{Address}'",
            message,
            contact is null,
            normalized.Phone,
            normalized.ComplaintGuide,
            normalized.SystemName,
            normalized.Email,
            normalized.Address);
    }

    private static bool TryReadCachedSnapshot(out MobileSystemSettingsSnapshot snapshot)
    {
        if (TryReadCache(SnapshotCacheKey, NormalizeSnapshot, out snapshot))
        {
            return true;
        }

        if (TryReadCache(LanguagesCacheKey, NormalizeLanguageSettings, out MobileLanguageSettings languages) &&
            TryReadCache(ContactCacheKey, NormalizeContactInfo, out SystemContactInfo contact))
        {
            snapshot = NormalizeSnapshot(new MobileSystemSettingsSnapshot
            {
                Languages = languages,
                Contact = contact
            });
            CacheSnapshot(snapshot);
            return true;
        }

        snapshot = null!;
        return false;
    }

    private static MobileSystemSettingsSnapshot CreateSnapshotFromSystemSettings(SystemSettingsDto settings)
    {
        var languages = (settings.SupportedLanguages ?? [])
            .Where(code => !string.IsNullOrWhiteSpace(code))
            .Select(code =>
            {
                var definition = AppLanguage.GetDefinition(code);
                return new MobileLanguageOptionSetting
                {
                    Code = definition.Code,
                    DisplayName = definition.DisplayName
                };
            })
            .GroupBy(language => language.Code, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .ToList();

        return NormalizeSnapshot(new MobileSystemSettingsSnapshot
        {
            Languages = new MobileLanguageSettings
            {
                DefaultLanguage = settings.DefaultLanguage,
                Languages = languages
            },
            Contact = new SystemContactInfo
            {
                SystemName = settings.AppName,
                AppName = settings.AppName,
                Phone = settings.SupportPhone,
                SupportPhone = settings.SupportPhone,
                Email = settings.SupportEmail,
                SupportEmail = settings.SupportEmail,
                Address = settings.ContactAddress,
                ContactAddress = settings.ContactAddress,
                ComplaintGuide = settings.SupportInstructions,
                SupportInstructions = settings.SupportInstructions,
                SupportHours = settings.SupportHours,
                UpdatedAtUtc = settings.ContactUpdatedAtUtc,
                ContactUpdatedAtUtc = settings.ContactUpdatedAtUtc
            },
            OfflinePackage = new MobileOfflinePackageSettings
            {
                DownloadsEnabled = settings.OfflinePackageDownloadsEnabled,
                MaxPackageSizeMb = Math.Max(0, settings.OfflinePackageMaxSizeMb),
                Description = settings.OfflinePackageDescription?.Trim() ?? string.Empty
            }
        });
    }

    private static MobileSystemSettingsSnapshot? TryCreateSnapshotFromBootstrapEnvelope(string? bootstrapEnvelopeJson)
    {
        if (string.IsNullOrWhiteSpace(bootstrapEnvelopeJson))
        {
            return null;
        }

        try
        {
            var envelope = JsonSerializer.Deserialize<ApiEnvelope<BootstrapSettingsPayload>>(
                bootstrapEnvelopeJson,
                JsonOptions);
            return envelope?.Success == true && envelope.Data?.Settings is not null
                ? CreateSnapshotFromSystemSettings(envelope.Data.Settings)
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static MobileSystemSettingsSnapshot NormalizeSnapshot(MobileSystemSettingsSnapshot? snapshot)
    {
        return new MobileSystemSettingsSnapshot
        {
            Languages = NormalizeLanguageSettings(snapshot?.Languages),
            Contact = NormalizeContactInfo(snapshot?.Contact),
            OfflinePackage = NormalizeOfflinePackageSettings(snapshot?.OfflinePackage)
        };
    }

    private static MobileSystemSettingsSnapshot? SelectUsableSnapshot(MobileSystemSettingsSnapshot? snapshot)
    {
        if (snapshot is null)
        {
            return null;
        }

        var normalized = NormalizeSnapshot(snapshot);
        return normalized;
    }

    private static bool HasUsableContact(SystemContactInfo contact)
        => !string.IsNullOrWhiteSpace(contact.SystemName) ||
           !string.IsNullOrWhiteSpace(contact.AppName) ||
           !string.IsNullOrWhiteSpace(contact.Phone) ||
           !string.IsNullOrWhiteSpace(contact.SupportPhone) ||
           !string.IsNullOrWhiteSpace(contact.Email) ||
           !string.IsNullOrWhiteSpace(contact.SupportEmail) ||
           !string.IsNullOrWhiteSpace(contact.Address) ||
           !string.IsNullOrWhiteSpace(contact.ContactAddress) ||
           !string.IsNullOrWhiteSpace(contact.ComplaintGuide) ||
           !string.IsNullOrWhiteSpace(contact.SupportInstructions) ||
           !string.IsNullOrWhiteSpace(contact.SupportHours);

    private async Task<HttpClient?> GetClientAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var configuredBaseUrl = await apiBaseUrlService.GetBaseUrlAsync(cancellationToken);
        var nextBaseUrl = string.IsNullOrWhiteSpace(configuredBaseUrl)
            ? string.Empty
            : MobileApiEndpointHelper.EnsureTrailingSlash(configuredBaseUrl);
        if (string.IsNullOrWhiteSpace(nextBaseUrl))
        {
            return null;
        }

        if (_httpClient is not null &&
            string.Equals(_resolvedBaseUrl, nextBaseUrl, StringComparison.OrdinalIgnoreCase))
        {
            return _httpClient;
        }

        _httpClient?.Dispose();
        _httpClient = new HttpClient
        {
            BaseAddress = new Uri(nextBaseUrl, UriKind.Absolute),
            Timeout = TimeSpan.FromSeconds(30)
        };
        _resolvedBaseUrl = nextBaseUrl;
        logger.LogInformation(
            "[MobileSettingsApi] BaseUrl resolved. baseUrl={BaseUrl}",
            _resolvedBaseUrl);
        return _httpClient;
    }

    private static bool TryReadCache<T>(string cacheKey, Func<T, T> normalize, out T value)
        where T : class
    {
        var cachedJson = Preferences.Default.Get(cacheKey, string.Empty);
        if (!string.IsNullOrWhiteSpace(cachedJson))
        {
            try
            {
                var cached = JsonSerializer.Deserialize<T>(cachedJson, JsonOptions);
                if (cached is not null)
                {
                    value = normalize(cached);
                    return true;
                }
            }
            catch (Exception exception) when (exception is JsonException or NotSupportedException or InvalidOperationException)
            {
                Preferences.Default.Remove(cacheKey);
            }
        }

        value = null!;
        return false;
    }

    private static MobileLanguageSettings NormalizeLanguageSettings(MobileLanguageSettings? settings)
    {
        var sourceLanguages = settings?.Languages ?? [];
        if (sourceLanguages.Count == 0 && settings?.EnabledLanguages?.Count > 0)
        {
            sourceLanguages = settings.EnabledLanguages
                .Select(code =>
                {
                    var definition = AppLanguage.GetDefinition(code);
                    return new MobileLanguageOptionSetting
                    {
                        Code = definition.Code,
                        DisplayName = definition.DisplayName
                    };
                })
                .ToList();
        }

        var languages = sourceLanguages
            .Where(language => !string.IsNullOrWhiteSpace(language?.Code))
            .Select(language =>
            {
                var definition = AppLanguage.GetDefinition(language.Code);
                return new MobileLanguageOptionSetting
                {
                    Code = definition.Code,
                    DisplayName = string.IsNullOrWhiteSpace(language.DisplayName)
                        ? definition.DisplayName
                        : language.DisplayName.Trim()
                };
            })
            .GroupBy(language => language.Code, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .ToList();
        if (languages.Count == 0)
        {
            var fallback = AppLanguage.GetDefinition(AppLanguage.DefaultLanguage);
            languages.Add(new MobileLanguageOptionSetting
            {
                Code = fallback.Code,
                DisplayName = fallback.DisplayName
            });
        }

        var defaultLanguage = AppLanguage.NormalizeCode(settings?.DefaultLanguage);
        if (!languages.Any(language => string.Equals(language.Code, defaultLanguage, StringComparison.OrdinalIgnoreCase)))
        {
            defaultLanguage = languages.Any(language => string.Equals(language.Code, AppLanguage.DefaultLanguage, StringComparison.OrdinalIgnoreCase))
                ? AppLanguage.DefaultLanguage
                : languages[0].Code;
        }

        return new MobileLanguageSettings
        {
            DefaultLanguage = defaultLanguage,
            EnabledLanguages = languages.Select(language => language.Code).ToList(),
            Languages = languages
        };
    }

    private static SystemContactInfo NormalizeContactInfo(SystemContactInfo? settings)
    {
        var systemName = FirstNonEmpty(settings?.SystemName, settings?.AppName);
        var phone = FirstNonEmpty(settings?.Phone, settings?.SupportPhone);
        var email = FirstNonEmpty(settings?.Email, settings?.SupportEmail);
        var address = FirstNonEmpty(settings?.Address, settings?.ContactAddress);
        var complaintGuide = FirstNonEmpty(settings?.ComplaintGuide, settings?.SupportInstructions);

        return new()
        {
            SystemName = systemName,
            AppName = systemName,
            Phone = phone,
            SupportPhone = phone,
            Email = email,
            SupportEmail = email,
            Address = address,
            ContactAddress = address,
            ComplaintGuide = complaintGuide,
            SupportInstructions = complaintGuide,
            SupportHours = settings?.SupportHours?.Trim() ?? string.Empty,
            UpdatedAtUtc = settings?.UpdatedAtUtc ?? settings?.ContactUpdatedAtUtc,
            ContactUpdatedAtUtc = settings?.ContactUpdatedAtUtc ?? settings?.UpdatedAtUtc
        };
    }

    private static MobileOfflinePackageSettings NormalizeOfflinePackageSettings(MobileOfflinePackageSettings? settings)
        => new()
        {
            DownloadsEnabled = settings?.DownloadsEnabled ?? false,
            MaxPackageSizeMb = Math.Max(0, settings?.MaxPackageSizeMb ?? 0),
            Description = settings?.Description?.Trim() ?? string.Empty
        };

    private static MobileLanguageSettings CreateDefaultLanguageSettings()
        => new()
        {
            DefaultLanguage = AppLanguage.DefaultLanguage,
            EnabledLanguages = [AppLanguage.DefaultLanguage],
            Languages =
            [
                new MobileLanguageOptionSetting
                {
                    Code = AppLanguage.GetDefinition(AppLanguage.DefaultLanguage).Code,
                    DisplayName = AppLanguage.GetDefinition(AppLanguage.DefaultLanguage).DisplayName
                }
            ]
        };

    private static SystemContactInfo CreateDefaultContactInfo()
        => new()
        {
            AppName = string.Empty,
            SystemName = string.Empty,
            SupportPhone = string.Empty,
            Phone = string.Empty,
            SupportEmail = string.Empty,
            Email = string.Empty,
            ContactAddress = string.Empty,
            Address = string.Empty,
            SupportInstructions = string.Empty,
            ComplaintGuide = string.Empty,
            SupportHours = string.Empty
        };

    private static MobileSystemSettingsSnapshot CreateDefaultSettingsSnapshot()
        => new()
        {
            Languages = CreateDefaultLanguageSettings(),
            Contact = CreateDefaultContactInfo(),
            OfflinePackage = new MobileOfflinePackageSettings()
        };

    private static string FirstNonEmpty(params string?[] values)
        => values
               .Select(value => value?.Trim() ?? string.Empty)
               .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)) ??
           string.Empty;

    private sealed record ApiEnvelope<T>(bool Success, T? Data, string? Message);

    private sealed class MobileSystemSettingsSnapshot
    {
        public MobileLanguageSettings Languages { get; set; } = new();
        public SystemContactInfo Contact { get; set; } = new();
        public MobileOfflinePackageSettings OfflinePackage { get; set; } = new();
    }

    private sealed class BootstrapSettingsPayload
    {
        public SystemSettingsDto? Settings { get; set; }
    }

    private sealed class SystemSettingsDto
    {
        public string AppName { get; set; } = string.Empty;
        public string SupportPhone { get; set; } = string.Empty;
        public string SupportEmail { get; set; } = string.Empty;
        public string ContactAddress { get; set; } = string.Empty;
        public string SupportInstructions { get; set; } = string.Empty;
        public string SupportHours { get; set; } = string.Empty;
        public DateTimeOffset? ContactUpdatedAtUtc { get; set; }
        public string DefaultLanguage { get; set; } = AppLanguage.DefaultLanguage;
        public List<string> SupportedLanguages { get; set; } = [];
        public bool OfflinePackageDownloadsEnabled { get; set; }
        public int OfflinePackageMaxSizeMb { get; set; }
        public string OfflinePackageDescription { get; set; } = string.Empty;
    }
}

public sealed class MobileLanguageSettings
{
    public string DefaultLanguage { get; set; } = AppLanguage.DefaultLanguage;
    public List<string> EnabledLanguages { get; set; } = [];
    public List<MobileLanguageOptionSetting> Languages { get; set; } = [];
}

public sealed class MobileLanguageOptionSetting
{
    public string Code { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
}

public sealed class SystemContactInfo
{
    public string SystemName { get; set; } = string.Empty;
    public string AppName { get; set; } = string.Empty;
    public string Phone { get; set; } = string.Empty;
    public string SupportPhone { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string SupportEmail { get; set; } = string.Empty;
    public string Address { get; set; } = string.Empty;
    public string ContactAddress { get; set; } = string.Empty;
    public string ComplaintGuide { get; set; } = string.Empty;
    public string SupportInstructions { get; set; } = string.Empty;
    public string SupportHours { get; set; } = string.Empty;
    public DateTimeOffset? UpdatedAtUtc { get; set; }
    public DateTimeOffset? ContactUpdatedAtUtc { get; set; }
}

public sealed class MobileOfflinePackageSettings
{
    public bool DownloadsEnabled { get; set; }
    public int MaxPackageSizeMb { get; set; }
    public string Description { get; set; } = string.Empty;
}
