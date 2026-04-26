using Microsoft.Extensions.Caching.Memory;
using VinhKhanh.BackendApi.Contracts;
using VinhKhanh.BackendApi.Models;

namespace VinhKhanh.BackendApi.Infrastructure;

public sealed class MobileOfflinePackageBuilder(
    AdminDataRepository repository,
    BootstrapLocalizationService bootstrapLocalizationService,
    ResponseUrlNormalizer responseUrlNormalizer,
    IMemoryCache memoryCache,
    ILogger<MobileOfflinePackageBuilder> logger)
{
    private static readonly TimeSpan CacheTtl = TimeSpan.FromMinutes(2);

    public async Task<AdminBootstrapResponse> BuildAsync(
        string? requestedLanguageCode,
        CancellationToken cancellationToken)
    {
        var syncState = repository.GetSyncState();
        var publicBootstrap = repository.GetBootstrap();
        publicBootstrap = publicBootstrap with
        {
            SyncState = publicBootstrap.SyncState ?? syncState
        };

        var normalizedRequestedLanguage = ResolveRequestedLanguage(
            publicBootstrap.Settings,
            requestedLanguageCode);
        var cacheKey = $"mobile-offline-package:{normalizedRequestedLanguage}:{syncState.Version}";
        if (memoryCache.TryGetValue(cacheKey, out AdminBootstrapResponse? cachedPackage) &&
            cachedPackage is not null)
        {
            logger.LogInformation(
                "[OfflinePackage] Reusing cached mobile offline package. requestedLanguage={RequestedLanguage}; version={Version}",
                normalizedRequestedLanguage,
                syncState.Version);
            return cachedPackage;
        }

        var requestedLanguageBootstrap = await bootstrapLocalizationService.ApplyAutoTranslationsAsync(
            publicBootstrap,
            normalizedRequestedLanguage,
            cancellationToken);
        var packageLanguages = GetPackageLanguageCodes(publicBootstrap.Settings, normalizedRequestedLanguage);
        var mergedTranslations = new List<Translation>(publicBootstrap.Translations.Count * Math.Max(1, packageLanguages.Count));
        mergedTranslations.AddRange(publicBootstrap.Translations);
        mergedTranslations.AddRange(requestedLanguageBootstrap.Translations);

        foreach (var packageLanguage in packageLanguages)
        {
            if (string.Equals(packageLanguage, normalizedRequestedLanguage, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var localizedBootstrap = await bootstrapLocalizationService.ApplyAutoTranslationsAsync(
                publicBootstrap,
                packageLanguage,
                cancellationToken);
            mergedTranslations.AddRange(localizedBootstrap.Translations);

            logger.LogInformation(
                "[OfflinePackage] Added localized translations to mobile offline package. requestedLanguage={RequestedLanguage}; packageLanguage={PackageLanguage}; translations={TranslationCount}",
                normalizedRequestedLanguage,
                packageLanguage,
                localizedBootstrap.Translations.Count);
        }

        var builtPackage = requestedLanguageBootstrap with
        {
            Translations = CollapseTranslations(mergedTranslations),
            SyncState = requestedLanguageBootstrap.SyncState ?? syncState
        };
        builtPackage = responseUrlNormalizer.Normalize(builtPackage);

        memoryCache.Set(
            cacheKey,
            builtPackage,
            new MemoryCacheEntryOptions
            {
                AbsoluteExpirationRelativeToNow = CacheTtl,
                Size = EstimateCacheSize(builtPackage)
            });

        logger.LogInformation(
            "[OfflinePackage] Built mobile offline package. requestedLanguage={RequestedLanguage}; version={Version}; packageLanguages={PackageLanguages}; pois={PoiCount}; translations={TranslationCount}; audioGuides={AudioGuideCount}; mediaAssets={MediaAssetCount}",
            normalizedRequestedLanguage,
            builtPackage.SyncState?.Version,
            string.Join(",", packageLanguages),
            builtPackage.Pois.Count,
            builtPackage.Translations.Count,
            builtPackage.AudioGuides.Count,
            builtPackage.MediaAssets.Count);

        return builtPackage;
    }

    private static string ResolveRequestedLanguage(SystemSetting settings, string? requestedLanguageCode)
    {
        var normalizedRequestedLanguage = PremiumAccessCatalog.NormalizeLanguageCode(requestedLanguageCode);
        if (!string.IsNullOrWhiteSpace(normalizedRequestedLanguage))
        {
            return normalizedRequestedLanguage;
        }

        return PremiumAccessCatalog.NormalizeLanguageCode(settings.DefaultLanguage);
    }

    private static IReadOnlyList<string> GetPackageLanguageCodes(SystemSetting settings, string requestedLanguageCode)
    {
        var languages = new List<string>();

        AddLanguage(languages, requestedLanguageCode);
        AddLanguage(languages, settings.DefaultLanguage);
        AddLanguage(languages, settings.FallbackLanguage);

        foreach (var supportedLanguage in settings.SupportedLanguages)
        {
            AddLanguage(languages, supportedLanguage);
        }

        return languages;
    }

    private static void AddLanguage(ICollection<string> languages, string? languageCode)
    {
        var normalizedLanguageCode = PremiumAccessCatalog.NormalizeLanguageCode(languageCode);
        if (string.IsNullOrWhiteSpace(normalizedLanguageCode) ||
            languages.Any(language => string.Equals(language, normalizedLanguageCode, StringComparison.OrdinalIgnoreCase)))
        {
            return;
        }

        languages.Add(normalizedLanguageCode);
    }

    private static IReadOnlyList<Translation> CollapseTranslations(IEnumerable<Translation> translations)
    {
        return translations
            .Where(item =>
                !string.IsNullOrWhiteSpace(item.EntityType) &&
                !string.IsNullOrWhiteSpace(item.EntityId) &&
                !string.IsNullOrWhiteSpace(item.LanguageCode))
            .GroupBy(item => (
                EntityType: item.EntityType.Trim().ToLowerInvariant(),
                EntityId: item.EntityId.Trim(),
                LanguageCode: PremiumAccessCatalog.NormalizeLanguageCode(item.LanguageCode)))
            .Select(group =>
                group
                    .OrderByDescending(item => item.UpdatedAt)
                    .ThenByDescending(item => item.Id, StringComparer.OrdinalIgnoreCase)
                    .First())
            .OrderBy(item => item.EntityType, StringComparer.OrdinalIgnoreCase)
            .ThenBy(item => item.EntityId, StringComparer.OrdinalIgnoreCase)
            .ThenBy(item => item.LanguageCode, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static int EstimateCacheSize(AdminBootstrapResponse bootstrap)
        => Math.Max(
            1,
            bootstrap.Categories.Count +
            bootstrap.Pois.Count +
            bootstrap.Translations.Count +
            bootstrap.AudioGuides.Count +
            bootstrap.MediaAssets.Count +
            bootstrap.FoodItems.Count +
            bootstrap.Routes.Count +
            bootstrap.Promotions.Count);
}
