using VinhKhanh.BackendApi.Contracts;
using VinhKhanh.BackendApi.Models;

namespace VinhKhanh.BackendApi.Infrastructure;

public sealed class BootstrapLocalizationService(
    RuntimeTranslationService runtimeTranslationService,
    ILogger<BootstrapLocalizationService> logger)
{
    public async Task<AdminBootstrapResponse> ApplyAutoTranslationsAsync(
        AdminBootstrapResponse bootstrap,
        string? requestedLanguageCode,
        CancellationToken cancellationToken)
    {
        var targetLanguageCode = ResolveRequestedLanguage(bootstrap.Settings, requestedLanguageCode);
        if (!CanServeRequestedLanguage(bootstrap.Settings, targetLanguageCode))
        {
            logger.LogWarning(
                "Bootstrap localization skipped because requested language is not supported. requestedLanguage={RequestedLanguage}",
                targetLanguageCode);
            return bootstrap;
        }

        var fields = BuildTranslationFields(bootstrap, targetLanguageCode).ToList();
        var translatedFields = await runtimeTranslationService.TranslateFieldsAsync(
            fields,
            targetLanguageCode,
            cancellationToken);
        var textByKey = translatedFields.ToDictionary(CreateResultKey, StringComparer.OrdinalIgnoreCase);

        var categories = bootstrap.Categories
            .Select(category => CopyCategory(category, textByKey))
            .ToList();
        var pois = bootstrap.Pois
            .Select(poi => CopyPoi(poi, textByKey))
            .ToList();
        var foodItems = bootstrap.FoodItems
            .Select(foodItem => CopyFoodItem(foodItem, textByKey))
            .ToList();
        var routes = bootstrap.Routes
            .Select(route => CopyRoute(route, textByKey))
            .ToList();
        var promotions = bootstrap.Promotions
            .Select(promotion => CopyPromotion(promotion, textByKey))
            .ToList();
        var translations = BuildRuntimeTranslations(
            bootstrap.Settings,
            bootstrap.Pois,
            bootstrap.FoodItems,
            bootstrap.Routes,
            bootstrap.Promotions,
            textByKey,
            targetLanguageCode);

        logger.LogInformation(
            "Bootstrap runtime localization completed. requestedLanguage={RequestedLanguage}; fieldCount={FieldCount}; fallbackCount={FallbackCount}; translationsReturned={TranslationCount}",
            targetLanguageCode,
            translatedFields.Count,
            translatedFields.Count(item => item.UsedFallback),
            translations.Count);

        return bootstrap with
        {
            Categories = categories,
            Pois = pois,
            FoodItems = foodItems,
            Routes = routes,
            Promotions = promotions,
            Translations = translations
        };
    }

    private static IEnumerable<RuntimeTranslationField> BuildTranslationFields(
        AdminBootstrapResponse bootstrap,
        string targetLanguageCode)
    {
        var defaultSourceLanguageCode = ResolveSourceLanguage(bootstrap.Settings.DefaultLanguage);

        foreach (var category in bootstrap.Categories)
        {
            yield return Field("category", category.Id, "name", category.Name, defaultSourceLanguageCode);
        }

        foreach (var poi in bootstrap.Pois)
        {
            var sourceLanguageCode = ResolveSourceLanguage(poi.SourceLanguageCode, defaultSourceLanguageCode);
            yield return Field("poi", poi.Id, "title", string.IsNullOrWhiteSpace(poi.Title) ? poi.Slug : poi.Title, sourceLanguageCode);
            yield return Field("poi", poi.Id, "shortDescription", poi.ShortDescription, sourceLanguageCode);
            yield return Field("poi", poi.Id, "description", poi.Description, sourceLanguageCode);
            yield return Field("poi", poi.Id, "audioScript", poi.AudioScript, sourceLanguageCode);
        }

        foreach (var foodItem in bootstrap.FoodItems)
        {
            yield return Field("food_item", foodItem.Id, "name", foodItem.Name, defaultSourceLanguageCode);
            yield return Field("food_item", foodItem.Id, "description", foodItem.Description, defaultSourceLanguageCode);
        }

        foreach (var route in bootstrap.Routes)
        {
            yield return Field("route", route.Id, "name", route.Name, defaultSourceLanguageCode);
            yield return Field("route", route.Id, "theme", route.Theme, defaultSourceLanguageCode);
            yield return Field("route", route.Id, "description", route.Description, defaultSourceLanguageCode);
        }

        foreach (var promotion in bootstrap.Promotions)
        {
            yield return Field("promotion", promotion.Id, "title", promotion.Title, defaultSourceLanguageCode);
            yield return Field("promotion", promotion.Id, "description", promotion.Description, defaultSourceLanguageCode);
        }

        _ = targetLanguageCode;
    }

    private static IReadOnlyList<Translation> BuildRuntimeTranslations(
        SystemSetting settings,
        IReadOnlyList<Poi> sourcePois,
        IReadOnlyList<FoodItem> sourceFoodItems,
        IReadOnlyList<TourRoute> sourceRoutes,
        IReadOnlyList<Promotion> sourcePromotions,
        IReadOnlyDictionary<string, RuntimeTranslationResult> textByKey,
        string targetLanguageCode)
    {
        var defaultSourceLanguageCode = ResolveSourceLanguage(settings.DefaultLanguage);
        var translations = new List<Translation>();

        foreach (var poi in sourcePois)
        {
            var sourceLanguageCode = ResolveSourceLanguage(poi.SourceLanguageCode, defaultSourceLanguageCode);
            var sourceTitle = string.IsNullOrWhiteSpace(poi.Title) ? poi.Slug : poi.Title;
            var sourceFullText = !string.IsNullOrWhiteSpace(poi.AudioScript)
                ? poi.AudioScript
                : poi.Description;
            var title = GetText(textByKey, "poi", poi.Id, "title", sourceTitle);
            var shortText = GetText(textByKey, "poi", poi.Id, "shortDescription", poi.ShortDescription);
            var description = GetText(textByKey, "poi", poi.Id, "description", poi.Description);
            var audioScript = GetText(textByKey, "poi", poi.Id, "audioScript", poi.AudioScript);
            translations.Add(CreateRuntimeTranslation(
                "poi",
                poi.Id,
                targetLanguageCode,
                sourceLanguageCode,
                sourceTitle,
                poi.ShortDescription,
                sourceFullText,
                title,
                shortText,
                string.IsNullOrWhiteSpace(audioScript) ? description : audioScript,
                poi.UpdatedBy,
                poi.UpdatedAt));
        }

        foreach (var foodItem in sourceFoodItems)
        {
            translations.Add(CreateRuntimeTranslation(
                "food_item",
                foodItem.Id,
                targetLanguageCode,
                defaultSourceLanguageCode,
                foodItem.Name,
                string.Empty,
                foodItem.Description,
                GetText(textByKey, "food_item", foodItem.Id, "name", foodItem.Name),
                string.Empty,
                GetText(textByKey, "food_item", foodItem.Id, "description", foodItem.Description),
                "runtime-translation",
                DateTimeOffset.UtcNow));
        }

        foreach (var route in sourceRoutes)
        {
            translations.Add(CreateRuntimeTranslation(
                "route",
                route.Id,
                targetLanguageCode,
                defaultSourceLanguageCode,
                route.Name,
                route.Theme,
                route.Description,
                GetText(textByKey, "route", route.Id, "name", route.Name),
                GetText(textByKey, "route", route.Id, "theme", route.Theme),
                GetText(textByKey, "route", route.Id, "description", route.Description),
                route.UpdatedBy,
                route.UpdatedAt));
        }

        foreach (var promotion in sourcePromotions)
        {
            translations.Add(CreateRuntimeTranslation(
                "promotion",
                promotion.Id,
                targetLanguageCode,
                defaultSourceLanguageCode,
                promotion.Title,
                string.Empty,
                promotion.Description,
                GetText(textByKey, "promotion", promotion.Id, "title", promotion.Title),
                string.Empty,
                GetText(textByKey, "promotion", promotion.Id, "description", promotion.Description),
                "runtime-translation",
                DateTimeOffset.UtcNow));
        }

        return translations;
    }

    private static Translation CreateRuntimeTranslation(
        string entityType,
        string entityId,
        string targetLanguageCode,
        string sourceLanguageCode,
        string sourceTitle,
        string sourceShortText,
        string sourceFullText,
        string title,
        string shortText,
        string fullText,
        string updatedBy,
        DateTimeOffset updatedAt)
        => new()
        {
            Id = $"runtime-{entityType}-{entityId}-{targetLanguageCode}",
            EntityType = entityType,
            EntityId = entityId,
            LanguageCode = targetLanguageCode,
            Title = title,
            ShortText = shortText,
            FullText = fullText,
            SeoTitle = title,
            SeoDescription = string.IsNullOrWhiteSpace(shortText) ? fullText : shortText,
            SourceLanguageCode = sourceLanguageCode,
            SourceHash = TranslationSourceVersioning.CreateSourceHashForRuntime(
                sourceTitle,
                sourceShortText,
                sourceFullText,
                sourceLanguageCode),
            SourceUpdatedAt = updatedAt == DateTimeOffset.MinValue ? null : updatedAt,
            UpdatedBy = string.IsNullOrWhiteSpace(updatedBy) ? "runtime-translation" : updatedBy,
            UpdatedAt = updatedAt == DateTimeOffset.MinValue ? DateTimeOffset.UtcNow : updatedAt
        };

    private static PoiCategory CopyCategory(
        PoiCategory category,
        IReadOnlyDictionary<string, RuntimeTranslationResult> textByKey)
        => new()
        {
            Id = category.Id,
            Name = GetText(textByKey, "category", category.Id, "name", category.Name),
            Slug = category.Slug,
            Icon = category.Icon,
            Color = category.Color
        };

    private static Poi CopyPoi(
        Poi poi,
        IReadOnlyDictionary<string, RuntimeTranslationResult> textByKey)
        => new()
        {
            Id = poi.Id,
            Slug = poi.Slug,
            Title = GetText(textByKey, "poi", poi.Id, "title", string.IsNullOrWhiteSpace(poi.Title) ? poi.Slug : poi.Title),
            ShortDescription = GetText(textByKey, "poi", poi.Id, "shortDescription", poi.ShortDescription),
            Description = GetText(textByKey, "poi", poi.Id, "description", poi.Description),
            AudioScript = GetText(textByKey, "poi", poi.Id, "audioScript", poi.AudioScript),
            SourceLanguageCode = poi.SourceLanguageCode,
            Address = poi.Address,
            Lat = poi.Lat,
            Lng = poi.Lng,
            CategoryId = poi.CategoryId,
            Status = poi.Status,
            Featured = poi.Featured,
            IsActive = poi.IsActive,
            LockedBySuperAdmin = poi.LockedBySuperAdmin,
            District = poi.District,
            Ward = poi.Ward,
            PriceRange = poi.PriceRange,
            TriggerRadius = poi.TriggerRadius,
            Priority = poi.Priority,
            Tags = [.. poi.Tags],
            OwnerUserId = poi.OwnerUserId,
            UpdatedBy = poi.UpdatedBy,
            CreatedAt = poi.CreatedAt,
            UpdatedAt = poi.UpdatedAt,
            ApprovedAt = poi.ApprovedAt,
            RejectionReason = poi.RejectionReason,
            RejectedAt = poi.RejectedAt
        };

    private static FoodItem CopyFoodItem(
        FoodItem foodItem,
        IReadOnlyDictionary<string, RuntimeTranslationResult> textByKey)
        => new()
        {
            Id = foodItem.Id,
            PoiId = foodItem.PoiId,
            Name = GetText(textByKey, "food_item", foodItem.Id, "name", foodItem.Name),
            Description = GetText(textByKey, "food_item", foodItem.Id, "description", foodItem.Description),
            PriceRange = foodItem.PriceRange,
            ImageUrl = foodItem.ImageUrl,
            SpicyLevel = foodItem.SpicyLevel
        };

    private static TourRoute CopyRoute(
        TourRoute route,
        IReadOnlyDictionary<string, RuntimeTranslationResult> textByKey)
        => new()
        {
            Id = route.Id,
            Name = GetText(textByKey, "route", route.Id, "name", route.Name),
            Theme = GetText(textByKey, "route", route.Id, "theme", route.Theme),
            Description = GetText(textByKey, "route", route.Id, "description", route.Description),
            DurationMinutes = route.DurationMinutes,
            Difficulty = route.Difficulty,
            CoverImageUrl = route.CoverImageUrl,
            IsFeatured = route.IsFeatured,
            StopPoiIds = [.. route.StopPoiIds],
            IsActive = route.IsActive,
            IsSystemRoute = route.IsSystemRoute,
            OwnerUserId = route.OwnerUserId,
            UpdatedBy = route.UpdatedBy,
            UpdatedAt = route.UpdatedAt
        };

    private static Promotion CopyPromotion(
        Promotion promotion,
        IReadOnlyDictionary<string, RuntimeTranslationResult> textByKey)
        => new()
        {
            Id = promotion.Id,
            PoiId = promotion.PoiId,
            Title = GetText(textByKey, "promotion", promotion.Id, "title", promotion.Title),
            Description = GetText(textByKey, "promotion", promotion.Id, "description", promotion.Description),
            StartAt = promotion.StartAt,
            EndAt = promotion.EndAt,
            Status = promotion.Status
        };

    private static RuntimeTranslationField Field(
        string entityType,
        string entityId,
        string fieldName,
        string? sourceText,
        string sourceLanguageCode)
        => new(
            entityType,
            entityId,
            fieldName,
            sourceText ?? string.Empty,
            sourceLanguageCode);

    private static string GetText(
        IReadOnlyDictionary<string, RuntimeTranslationResult> textByKey,
        string entityType,
        string entityId,
        string fieldName,
        string? fallback)
        => textByKey.TryGetValue(CreateFieldKey(entityType, entityId, fieldName), out var result)
            ? result.Text
            : fallback ?? string.Empty;

    private static string CreateResultKey(RuntimeTranslationResult result)
        => CreateFieldKey(result.EntityType, result.EntityId, result.FieldName);

    private static string CreateFieldKey(string entityType, string entityId, string fieldName)
        => $"{entityType.Trim().ToLowerInvariant()}:{entityId.Trim().ToLowerInvariant()}:{fieldName.Trim().ToLowerInvariant()}";

    private static string ResolveRequestedLanguage(SystemSetting settings, string? requestedLanguageCode)
    {
        var normalized = PremiumAccessCatalog.NormalizeLanguageCode(requestedLanguageCode);
        if (!string.IsNullOrWhiteSpace(normalized))
        {
            return normalized;
        }

        return ResolveSourceLanguage(settings.DefaultLanguage);
    }

    private static string ResolveSourceLanguage(params string?[] languageCodes)
    {
        foreach (var languageCode in languageCodes)
        {
            var normalized = PremiumAccessCatalog.NormalizeLanguageCode(languageCode);
            if (!string.IsNullOrWhiteSpace(normalized))
            {
                return normalized;
            }
        }

        return "vi";
    }

    private static bool CanServeRequestedLanguage(SystemSetting settings, string targetLanguageCode)
    {
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
        supportedLanguages.Add("vi");
        return supportedLanguages.Contains(targetLanguageCode);
    }
}
