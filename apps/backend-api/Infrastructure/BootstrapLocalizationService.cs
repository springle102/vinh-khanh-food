using Microsoft.Extensions.Logging;
using VinhKhanh.BackendApi.Contracts;
using VinhKhanh.BackendApi.Models;

namespace VinhKhanh.BackendApi.Infrastructure;

public sealed class BootstrapLocalizationService(
    ITextTranslationClient translationClient,
    ILogger<BootstrapLocalizationService> logger)
{
    private static readonly HashSet<string> AutoTranslateLanguages = new(
        ["en", "zh-CN", "ko", "ja"],
        StringComparer.OrdinalIgnoreCase);

    public async Task<AdminBootstrapResponse> ApplyAutoTranslationsAsync(
        AdminBootstrapResponse bootstrap,
        string? requestedLanguageCode,
        CancellationToken cancellationToken)
    {
        var targetLanguageCode = PremiumAccessCatalog.NormalizeLanguageCode(requestedLanguageCode);
        if (!AutoTranslateLanguages.Contains(targetLanguageCode) ||
            LocalizationContentPolicy.IsSourceLanguage(targetLanguageCode))
        {
            return bootstrap;
        }

        var translations = bootstrap.Translations.ToList();
        var workItems = BuildWorkItems(bootstrap, translations, targetLanguageCode).ToList();
        if (workItems.Count == 0)
        {
            return bootstrap;
        }

        var segments = workItems
            .SelectMany(item => item.GetSegments())
            .ToList();
        if (segments.Count == 0)
        {
            return bootstrap;
        }

        TextTranslationResponse translated;
        try
        {
            translated = await translationClient.TranslateAsync(
                new TextTranslationRequest(
                    targetLanguageCode,
                    "vi",
                    segments.Select(item => item.SourceText).ToList()),
                cancellationToken);
        }
        catch (Exception exception) when (
            exception is HttpRequestException ||
            exception is InvalidOperationException ||
            exception is TaskCanceledException)
        {
            logger.LogWarning(
                exception,
                "Unable to auto-translate bootstrap content to {LanguageCode}.",
                targetLanguageCode);
            return bootstrap;
        }

        for (var index = 0; index < segments.Count; index += 1)
        {
            var translatedText = translated.Texts.ElementAtOrDefault(index);
            segments[index].Apply(translatedText, targetLanguageCode);
        }

        var generated = workItems
            .Where(item => item.HasContent)
            .Select(item => item.ToTranslation(targetLanguageCode))
            .ToList();
        if (generated.Count == 0)
        {
            return bootstrap;
        }

        var generatedKeys = generated
            .Select(item => CreateTranslationKey(item.EntityType, item.EntityId, item.LanguageCode))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var nextTranslations = translations
            .Where(item => !generatedKeys.Contains(CreateTranslationKey(item.EntityType, item.EntityId, item.LanguageCode)))
            .Concat(generated)
            .ToList();

        logger.LogInformation(
            "Auto-translated {TranslationCount} bootstrap translation records to {LanguageCode}.",
            generated.Count,
            targetLanguageCode);

        return bootstrap with { Translations = nextTranslations };
    }

    private static IEnumerable<BootstrapTranslationWorkItem> BuildWorkItems(
        AdminBootstrapResponse bootstrap,
        IReadOnlyList<Translation> translations,
        string targetLanguageCode)
    {
        foreach (var poi in bootstrap.Pois)
        {
            var exact = FindTranslation(translations, "poi", poi.Id, targetLanguageCode);
            var source = BuildSourceContent(
                FindTranslation(translations, "poi", poi.Id, "vi"),
                null,
                null,
                null);
            var workItem = BootstrapTranslationWorkItem.Create("poi", poi.Id, exact, source, targetLanguageCode);
            if (workItem is not null)
            {
                yield return workItem;
            }
        }

        foreach (var foodItem in bootstrap.FoodItems)
        {
            var exact = FindTranslation(translations, "food_item", foodItem.Id, targetLanguageCode);
            var source = BuildSourceContent(
                FindTranslation(translations, "food_item", foodItem.Id, "vi"),
                foodItem.Name,
                null,
                foodItem.Description);
            var workItem = BootstrapTranslationWorkItem.Create("food_item", foodItem.Id, exact, source, targetLanguageCode);
            if (workItem is not null)
            {
                yield return workItem;
            }
        }

        foreach (var promotion in bootstrap.Promotions)
        {
            var exact = FindTranslation(translations, "promotion", promotion.Id, targetLanguageCode);
            var source = BuildSourceContent(
                FindTranslation(translations, "promotion", promotion.Id, "vi"),
                promotion.Title,
                null,
                promotion.Description);
            var workItem = BootstrapTranslationWorkItem.Create("promotion", promotion.Id, exact, source, targetLanguageCode);
            if (workItem is not null)
            {
                yield return workItem;
            }
        }

        foreach (var route in bootstrap.Routes)
        {
            var exact = FindTranslation(translations, "route", route.Id, targetLanguageCode);
            var source = BuildSourceContent(
                FindTranslation(translations, "route", route.Id, "vi"),
                route.Name,
                route.Theme,
                route.Description);
            var workItem = BootstrapTranslationWorkItem.Create("route", route.Id, exact, source, targetLanguageCode);
            if (workItem is not null)
            {
                yield return workItem;
            }
        }
    }

    private static TranslationContent BuildSourceContent(
        Translation? source,
        string? fallbackTitle,
        string? fallbackShortText,
        string? fallbackFullText)
        => new(
            FirstNonEmptySource(source?.Title, fallbackTitle),
            FirstNonEmptySource(source?.ShortText, fallbackShortText),
            FirstNonEmptySource(source?.FullText, fallbackFullText, fallbackShortText));

    private static string FirstNonEmptySource(params string?[] values)
        => values
            .Select(value => LocalizationContentPolicy.CleanForLanguage(value, "vi"))
            .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)) ?? string.Empty;

    private static Translation? FindTranslation(
        IEnumerable<Translation> translations,
        string entityType,
        string entityId,
        string languageCode)
        => translations.FirstOrDefault(item =>
            string.Equals(item.EntityType, entityType, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(item.EntityId, entityId, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(PremiumAccessCatalog.NormalizeLanguageCode(item.LanguageCode), languageCode, StringComparison.OrdinalIgnoreCase));

    private static string CreateTranslationKey(string entityType, string entityId, string languageCode)
        => $"{entityType.Trim().ToLowerInvariant()}:{entityId.Trim().ToLowerInvariant()}:{PremiumAccessCatalog.NormalizeLanguageCode(languageCode).ToLowerInvariant()}";

    private sealed record TranslationContent(
        string Title,
        string ShortText,
        string FullText);

    private sealed class BootstrapTranslationWorkItem
    {
        private readonly Translation? _existing;
        private readonly TranslationContent _source;
        private readonly List<BootstrapTranslationSegment> _segments = [];

        private BootstrapTranslationWorkItem(
            string entityType,
            string entityId,
            Translation? existing,
            TranslationContent source,
            string title,
            string shortText,
            string fullText)
        {
            EntityType = entityType;
            EntityId = entityId;
            _existing = existing;
            _source = source;
            Title = title;
            ShortText = shortText;
            FullText = fullText;
        }

        public string EntityType { get; }

        public string EntityId { get; }

        public string Title { get; private set; }

        public string ShortText { get; private set; }

        public string FullText { get; private set; }

        public bool HasContent =>
            !string.IsNullOrWhiteSpace(Title) ||
            !string.IsNullOrWhiteSpace(ShortText) ||
            !string.IsNullOrWhiteSpace(FullText);

        public static BootstrapTranslationWorkItem? Create(
            string entityType,
            string entityId,
            Translation? existing,
            TranslationContent source,
            string targetLanguageCode)
        {
            var item = new BootstrapTranslationWorkItem(
                entityType,
                entityId,
                existing,
                source,
                LocalizationContentPolicy.CleanForLanguage(existing?.Title, targetLanguageCode) ?? string.Empty,
                LocalizationContentPolicy.CleanForLanguage(existing?.ShortText, targetLanguageCode) ?? string.Empty,
                LocalizationContentPolicy.CleanForLanguage(existing?.FullText, targetLanguageCode) ?? string.Empty);

            item.QueueMissingSegments();
            return item.HasContent || item._segments.Count > 0 ? item : null;
        }

        public IReadOnlyList<BootstrapTranslationSegment> GetSegments()
            => _segments;

        public Translation ToTranslation(string targetLanguageCode)
            => new()
            {
                Id = string.IsNullOrWhiteSpace(_existing?.Id)
                    ? $"auto-{EntityType}-{EntityId}-{targetLanguageCode}"
                    : _existing.Id,
                EntityType = EntityType,
                EntityId = EntityId,
                LanguageCode = targetLanguageCode,
                Title = Title,
                ShortText = ShortText,
                FullText = FullText,
                SeoTitle = LocalizationContentPolicy.CleanForLanguage(_existing?.SeoTitle, targetLanguageCode) ?? string.Empty,
                SeoDescription = LocalizationContentPolicy.CleanForLanguage(_existing?.SeoDescription, targetLanguageCode) ?? string.Empty,
                UpdatedBy = string.IsNullOrWhiteSpace(_existing?.UpdatedBy) ? "auto-translation" : _existing.UpdatedBy,
                UpdatedAt = _existing?.UpdatedAt ?? DateTimeOffset.UtcNow
            };

        private void QueueMissingSegments()
        {
            if (string.IsNullOrWhiteSpace(Title) && !string.IsNullOrWhiteSpace(_source.Title))
            {
                _segments.Add(new BootstrapTranslationSegment(_source.Title, value => Title = value));
            }

            if (string.IsNullOrWhiteSpace(ShortText) && !string.IsNullOrWhiteSpace(_source.ShortText))
            {
                _segments.Add(new BootstrapTranslationSegment(_source.ShortText, value => ShortText = value));
            }

            if (string.IsNullOrWhiteSpace(FullText) && !string.IsNullOrWhiteSpace(_source.FullText))
            {
                _segments.Add(new BootstrapTranslationSegment(_source.FullText, value => FullText = value));
            }
        }
    }

    private sealed class BootstrapTranslationSegment(
        string sourceText,
        Action<string> applyTranslatedText)
    {
        public string SourceText { get; } = sourceText;

        public void Apply(string? translatedText, string targetLanguageCode)
        {
            var cleaned = LocalizationContentPolicy.CleanForLanguage(translatedText, targetLanguageCode);
            if (!string.IsNullOrWhiteSpace(cleaned))
            {
                applyTranslatedText(cleaned);
            }
        }
    }
}
