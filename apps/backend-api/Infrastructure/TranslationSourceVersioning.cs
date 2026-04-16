using System.Security.Cryptography;
using System.Text;
using VinhKhanh.BackendApi.Models;

namespace VinhKhanh.BackendApi.Infrastructure;

internal sealed record TranslationSourceSnapshot(
    string EntityType,
    string EntityId,
    string LanguageCode,
    string Title,
    string ShortText,
    string FullText,
    string Hash,
    DateTimeOffset UpdatedAt)
{
    public string Body => !string.IsNullOrWhiteSpace(FullText) ? FullText : ShortText;
}

internal static class TranslationSourceVersioning
{
    public static TranslationSourceSnapshot? ResolveCurrentSource(
        string entityType,
        string entityId,
        IEnumerable<Translation> translations,
        string defaultLanguageCode,
        string fallbackLanguageCode,
        string? fallbackTitle,
        string? fallbackShortText,
        string? fallbackFullText,
        DateTimeOffset fallbackUpdatedAt)
    {
        ArgumentNullException.ThrowIfNull(translations);

        var normalizedEntityType = NormalizeEntityType(entityType);
        var entityTranslations = translations
            .Where(item =>
                string.Equals(NormalizeEntityType(item.EntityType), normalizedEntityType, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(item.EntityId, entityId, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(item => item.UpdatedAt)
            .ToList();
        var sourceTranslation = FindSourceTranslation(
            entityTranslations,
            defaultLanguageCode,
            fallbackLanguageCode);

        if (sourceTranslation is not null)
        {
            var sourceLanguageCode = NormalizeLanguageCode(sourceTranslation.LanguageCode);
            var title = CleanForLanguage(sourceTranslation.Title, sourceLanguageCode);
            var shortText = CleanForLanguage(sourceTranslation.ShortText, sourceLanguageCode);
            var fullText = CleanForLanguage(sourceTranslation.FullText, sourceLanguageCode);
            if (HasAnySourceContent(title, shortText, fullText))
            {
                return CreateSnapshot(
                    normalizedEntityType,
                    entityId,
                    sourceLanguageCode,
                    title,
                    shortText,
                    fullText,
                    sourceTranslation.UpdatedAt);
            }
        }

        var fallbackLanguage = NormalizeLanguageCode(defaultLanguageCode);
        var cleanedFallbackTitle = CleanForLanguage(fallbackTitle, fallbackLanguage);
        var cleanedFallbackShortText = CleanForLanguage(fallbackShortText, fallbackLanguage);
        var cleanedFallbackFullText = CleanForLanguage(fallbackFullText, fallbackLanguage);
        if (!HasAnySourceContent(cleanedFallbackTitle, cleanedFallbackShortText, cleanedFallbackFullText))
        {
            return null;
        }

        return CreateSnapshot(
            normalizedEntityType,
            entityId,
            fallbackLanguage,
            cleanedFallbackTitle,
            cleanedFallbackShortText,
            cleanedFallbackFullText,
            fallbackUpdatedAt);
    }

    public static bool MatchesCurrentSource(
        Translation? translation,
        TranslationSourceSnapshot? sourceSnapshot)
    {
        if (translation is null || sourceSnapshot is null)
        {
            return false;
        }

        var translationLanguageCode = NormalizeLanguageCode(translation.LanguageCode);
        if (string.Equals(translationLanguageCode, sourceSnapshot.LanguageCode, StringComparison.OrdinalIgnoreCase))
        {
            return string.Equals(
                CreateSourceHash(
                    translation.Title,
                    translation.ShortText,
                    translation.FullText,
                    translationLanguageCode),
                sourceSnapshot.Hash,
                StringComparison.OrdinalIgnoreCase);
        }

        return
            !string.IsNullOrWhiteSpace(translation.SourceHash) &&
            string.Equals(
                NormalizeLanguageCode(translation.SourceLanguageCode),
                sourceSnapshot.LanguageCode,
                StringComparison.OrdinalIgnoreCase) &&
            string.Equals(
                translation.SourceHash,
                sourceSnapshot.Hash,
                StringComparison.OrdinalIgnoreCase);
    }

    public static string CreateSourceHash(
        string? title,
        string? shortText,
        string? fullText,
        string? languageCode)
    {
        var normalizedLanguageCode = NormalizeLanguageCode(languageCode);
        var payload = string.Join(
            '\u001f',
            normalizedLanguageCode,
            CleanForLanguage(title, normalizedLanguageCode) ?? string.Empty,
            CleanForLanguage(shortText, normalizedLanguageCode) ?? string.Empty,
            CleanForLanguage(fullText, normalizedLanguageCode) ?? string.Empty);
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(payload));
        return Convert.ToHexString(hash);
    }

    public static bool IsEquivalentText(string? left, string? right)
        => string.Equals(
            NormalizeText(left),
            NormalizeText(right),
            StringComparison.Ordinal);

    private static TranslationSourceSnapshot CreateSnapshot(
        string entityType,
        string entityId,
        string languageCode,
        string? title,
        string? shortText,
        string? fullText,
        DateTimeOffset updatedAt)
        => new(
            entityType,
            entityId,
            languageCode,
            title ?? string.Empty,
            shortText ?? string.Empty,
            fullText ?? string.Empty,
            CreateSourceHash(title, shortText, fullText, languageCode),
            updatedAt);

    private static Translation? FindSourceTranslation(
        IReadOnlyList<Translation> translations,
        string defaultLanguageCode,
        string fallbackLanguageCode)
    {
        foreach (var languageCode in BuildSourceLanguagePreference(defaultLanguageCode, fallbackLanguageCode))
        {
            var matched = translations.FirstOrDefault(item =>
            {
                var normalizedLanguageCode = NormalizeLanguageCode(item.LanguageCode);
                return string.Equals(normalizedLanguageCode, languageCode, StringComparison.OrdinalIgnoreCase) &&
                    HasAnySourceContent(
                        CleanForLanguage(item.Title, normalizedLanguageCode),
                        CleanForLanguage(item.ShortText, normalizedLanguageCode),
                        CleanForLanguage(item.FullText, normalizedLanguageCode));
            });

            if (matched is not null)
            {
                return matched;
            }
        }

        return null;
    }

    private static IReadOnlyList<string> BuildSourceLanguagePreference(
        string defaultLanguageCode,
        string fallbackLanguageCode)
    {
        var languages = new List<string>();
        AddPreferredLanguage(languages, defaultLanguageCode);
        AddPreferredLanguage(languages, fallbackLanguageCode);
        return languages;
    }

    private static void AddPreferredLanguage(ICollection<string> languages, string? languageCode)
    {
        var normalized = NormalizeLanguageCode(languageCode);
        if (!languages.Contains(normalized, StringComparer.OrdinalIgnoreCase))
        {
            languages.Add(normalized);
        }
    }

    private static bool HasAnySourceContent(string? title, string? shortText, string? fullText)
        => !string.IsNullOrWhiteSpace(title) ||
           !string.IsNullOrWhiteSpace(shortText) ||
           !string.IsNullOrWhiteSpace(fullText);

    private static string NormalizeEntityType(string? entityType)
        => string.Equals(entityType?.Trim(), "place", StringComparison.OrdinalIgnoreCase)
            ? "poi"
            : string.Equals(entityType?.Trim(), "food-item", StringComparison.OrdinalIgnoreCase)
                ? "food_item"
                : entityType?.Trim().ToLowerInvariant() ?? string.Empty;

    private static string NormalizeLanguageCode(string? languageCode)
        => PremiumAccessCatalog.NormalizeLanguageCode(languageCode);

    private static string? CleanForLanguage(string? value, string? languageCode)
        => LocalizationContentPolicy.CleanForLanguage(value, languageCode);

    private static string NormalizeText(string? value)
        => string.Join(
            " ",
            (value ?? string.Empty)
                .Split(default(string[]), StringSplitOptions.RemoveEmptyEntries))
            .Trim();
}
