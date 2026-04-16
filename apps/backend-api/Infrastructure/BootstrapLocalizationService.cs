using VinhKhanh.BackendApi.Contracts;
using VinhKhanh.BackendApi.Models;

namespace VinhKhanh.BackendApi.Infrastructure;

public sealed class BootstrapLocalizationService(
    ITextTranslationClient translationClient,
    ILogger<BootstrapLocalizationService> logger)
{
    public async Task<AdminBootstrapResponse> ApplyAutoTranslationsAsync(
        AdminBootstrapResponse bootstrap,
        string? requestedLanguageCode,
        CancellationToken cancellationToken)
    {
        var targetLanguageCode = PremiumAccessCatalog.NormalizeLanguageCode(requestedLanguageCode);
        if (!CanAutoTranslateRequestedLanguage(bootstrap.Settings, targetLanguageCode))
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

        if (segments.Count > 0)
        {
            try
            {
                var translated = await translationClient.TranslateAsync(
                    new TextTranslationRequest(
                        targetLanguageCode,
                        null,
                        segments.Select(item => item.SourceText).ToList()),
                    cancellationToken);

                for (var index = 0; index < segments.Count; index += 1)
                {
                    var translatedText = translated.Texts.ElementAtOrDefault(index);
                    segments[index].Apply(translatedText, targetLanguageCode);
                }
            }
            catch (Exception exception) when (
                exception is HttpRequestException ||
                exception is InvalidOperationException ||
                exception is TaskCanceledException)
            {
                logger.LogWarning(
                    exception,
                    "Unable to auto-translate bootstrap content to {LanguageCode}. Stale target translations will be omitted from the response.",
                    targetLanguageCode);
            }
        }

        var replacementKeys = workItems
            .Select(item => item.Key)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var generated = workItems
            .Where(item => item.ShouldMaterialize)
            .Select(item => item.ToTranslation(targetLanguageCode))
            .ToList();
        var nextTranslations = translations
            .Where(item => !replacementKeys.Contains(CreateTranslationKey(item.EntityType, item.EntityId, item.LanguageCode)))
            .Concat(generated)
            .ToList();

        foreach (var workItem in workItems)
        {
            logger.LogDebug(
                "Bootstrap localization resolved. entityType={EntityType}; entityId={EntityId}; requestedLanguage={RequestedLanguage}; dbSourceTitle={DbSourceTitle}; translatorInputTitle={TranslatorInputTitle}; translationCache={TranslationCache}; sourceHash={SourceHash}; finalTitle={FinalTitle}",
                workItem.EntityType,
                workItem.EntityId,
                targetLanguageCode,
                workItem.SourceTitle,
                workItem.TranslatorInputTitle,
                workItem.TranslationCacheStatus,
                workItem.SourceHash,
                workItem.Title);
        }

        logger.LogInformation(
            "Refreshed bootstrap localization for {LanguageCode}. workItems={WorkItemCount}, segments={SegmentCount}, translationsReturned={TranslationCount}",
            targetLanguageCode,
            workItems.Count,
            segments.Count,
            generated.Count);

        return bootstrap with { Translations = nextTranslations };
    }

    private static bool CanAutoTranslateRequestedLanguage(SystemSetting settings, string targetLanguageCode)
    {
        if (LocalizationContentPolicy.IsSourceLanguage(targetLanguageCode))
        {
            return false;
        }

        var supportedLanguages = settings.SupportedLanguages
            .Select(PremiumAccessCatalog.NormalizeLanguageCode)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (supportedLanguages.Count == 0)
        {
            supportedLanguages.UnionWith(PremiumAccessCatalog.FreeLanguages);
            supportedLanguages.UnionWith(PremiumAccessCatalog.PremiumLanguages);
        }

        supportedLanguages.Add(PremiumAccessCatalog.NormalizeLanguageCode(settings.DefaultLanguage));
        supportedLanguages.Add(PremiumAccessCatalog.NormalizeLanguageCode(settings.FallbackLanguage));
        return supportedLanguages.Contains(targetLanguageCode);
    }

    private static IEnumerable<BootstrapTranslationWorkItem> BuildWorkItems(
        AdminBootstrapResponse bootstrap,
        IReadOnlyList<Translation> translations,
        string targetLanguageCode)
    {
        foreach (var poi in bootstrap.Pois)
        {
            var workItem = BootstrapTranslationWorkItem.Create(
                "poi",
                poi.Id,
                FindTranslation(translations, "poi", poi.Id, targetLanguageCode),
                TranslationSourceVersioning.ResolveCurrentSource(
                    "poi",
                    poi.Id,
                    translations,
                    bootstrap.Settings.DefaultLanguage,
                    bootstrap.Settings.FallbackLanguage,
                    poi.Slug,
                    null,
                    null,
                    poi.UpdatedAt),
                targetLanguageCode);
            if (workItem is not null)
            {
                yield return workItem;
            }
        }

        foreach (var foodItem in bootstrap.FoodItems)
        {
            var workItem = BootstrapTranslationWorkItem.Create(
                "food_item",
                foodItem.Id,
                FindTranslation(translations, "food_item", foodItem.Id, targetLanguageCode),
                TranslationSourceVersioning.ResolveCurrentSource(
                    "food_item",
                    foodItem.Id,
                    translations,
                    bootstrap.Settings.DefaultLanguage,
                    bootstrap.Settings.FallbackLanguage,
                    foodItem.Name,
                    null,
                    foodItem.Description,
                    DateTimeOffset.MinValue),
                targetLanguageCode);
            if (workItem is not null)
            {
                yield return workItem;
            }
        }

        foreach (var promotion in bootstrap.Promotions)
        {
            var workItem = BootstrapTranslationWorkItem.Create(
                "promotion",
                promotion.Id,
                FindTranslation(translations, "promotion", promotion.Id, targetLanguageCode),
                TranslationSourceVersioning.ResolveCurrentSource(
                    "promotion",
                    promotion.Id,
                    translations,
                    bootstrap.Settings.DefaultLanguage,
                    bootstrap.Settings.FallbackLanguage,
                    promotion.Title,
                    null,
                    promotion.Description,
                    DateTimeOffset.MinValue),
                targetLanguageCode);
            if (workItem is not null)
            {
                yield return workItem;
            }
        }

        foreach (var route in bootstrap.Routes)
        {
            var workItem = BootstrapTranslationWorkItem.Create(
                "route",
                route.Id,
                FindTranslation(translations, "route", route.Id, targetLanguageCode),
                TranslationSourceVersioning.ResolveCurrentSource(
                    "route",
                    route.Id,
                    translations,
                    bootstrap.Settings.DefaultLanguage,
                    bootstrap.Settings.FallbackLanguage,
                    route.Name,
                    route.Theme,
                    route.Description,
                    route.UpdatedAt),
                targetLanguageCode);
            if (workItem is not null)
            {
                yield return workItem;
            }
        }
    }

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

    private sealed class BootstrapTranslationWorkItem
    {
        private readonly Translation? _existing;
        private readonly TranslationSourceSnapshot _sourceSnapshot;
        private readonly List<BootstrapTranslationSegment> _segments = [];
        private readonly string _targetLanguageCode;
        private readonly bool _canReuseExisting;
        private bool _titleQueued;

        private BootstrapTranslationWorkItem(
            string entityType,
            string entityId,
            Translation? existing,
            TranslationSourceSnapshot sourceSnapshot,
            string targetLanguageCode,
            bool canReuseExisting,
            string title,
            string shortText,
            string fullText)
        {
            EntityType = entityType;
            EntityId = entityId;
            _existing = existing;
            _sourceSnapshot = sourceSnapshot;
            _targetLanguageCode = targetLanguageCode;
            _canReuseExisting = canReuseExisting;
            Title = title;
            ShortText = shortText;
            FullText = fullText;
        }

        public string EntityType { get; }

        public string EntityId { get; }

        public string Key => CreateTranslationKey(EntityType, EntityId, _targetLanguageCode);

        public string SourceLanguageCode => _sourceSnapshot.LanguageCode;

        public string SourceHash => _sourceSnapshot.Hash;

        public string? SourceTitle => string.IsNullOrWhiteSpace(_sourceSnapshot.Title) ? null : _sourceSnapshot.Title;

        public string? TranslatorInputTitle => _titleQueued ? _sourceSnapshot.Title : null;

        public string TranslationCacheStatus => _canReuseExisting ? "hit" : "miss";

        public string Title { get; private set; }

        public string ShortText { get; private set; }

        public string FullText { get; private set; }

        public bool HasContent =>
            !string.IsNullOrWhiteSpace(Title) ||
            !string.IsNullOrWhiteSpace(ShortText) ||
            !string.IsNullOrWhiteSpace(FullText);

        public bool ShouldMaterialize => HasContent;

        public static BootstrapTranslationWorkItem? Create(
            string entityType,
            string entityId,
            Translation? existing,
            TranslationSourceSnapshot? sourceSnapshot,
            string targetLanguageCode)
        {
            if (sourceSnapshot is null)
            {
                return null;
            }

            var canReuseExisting = TranslationSourceVersioning.MatchesCurrentSource(existing, sourceSnapshot);
            var item = new BootstrapTranslationWorkItem(
                entityType,
                entityId,
                existing,
                sourceSnapshot,
                targetLanguageCode,
                canReuseExisting,
                canReuseExisting ? CleanTargetValue(existing?.Title, targetLanguageCode) : string.Empty,
                canReuseExisting ? CleanTargetValue(existing?.ShortText, targetLanguageCode) : string.Empty,
                canReuseExisting ? CleanTargetValue(existing?.FullText, targetLanguageCode) : string.Empty);

            item.QueueMissingSegments();
            return item.HasContent || item._segments.Count > 0 || existing is not null ? item : null;
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
                SeoTitle = CleanTargetValue(_existing?.SeoTitle, targetLanguageCode),
                SeoDescription = CleanTargetValue(_existing?.SeoDescription, targetLanguageCode),
                SourceLanguageCode = _sourceSnapshot.LanguageCode,
                SourceHash = _sourceSnapshot.Hash,
                SourceUpdatedAt = _sourceSnapshot.UpdatedAt,
                UpdatedBy = string.IsNullOrWhiteSpace(_existing?.UpdatedBy) ? "auto-translation" : _existing.UpdatedBy,
                UpdatedAt = _sourceSnapshot.UpdatedAt == DateTimeOffset.MinValue
                    ? _existing?.UpdatedAt ?? DateTimeOffset.UtcNow
                    : _sourceSnapshot.UpdatedAt
            };

        private void QueueMissingSegments()
        {
            if (string.IsNullOrWhiteSpace(Title) && !string.IsNullOrWhiteSpace(_sourceSnapshot.Title))
            {
                _titleQueued = true;
                _segments.Add(new BootstrapTranslationSegment(_sourceSnapshot.Title, value => Title = value));
            }

            if (string.IsNullOrWhiteSpace(ShortText) && !string.IsNullOrWhiteSpace(_sourceSnapshot.ShortText))
            {
                _segments.Add(new BootstrapTranslationSegment(_sourceSnapshot.ShortText, value => ShortText = value));
            }

            if (string.IsNullOrWhiteSpace(FullText) && !string.IsNullOrWhiteSpace(_sourceSnapshot.FullText))
            {
                _segments.Add(new BootstrapTranslationSegment(_sourceSnapshot.FullText, value => FullText = value));
            }
        }

        private static string CleanTargetValue(string? value, string targetLanguageCode)
            => LocalizationContentPolicy.CleanForLanguage(value, targetLanguageCode) ?? string.Empty;
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
