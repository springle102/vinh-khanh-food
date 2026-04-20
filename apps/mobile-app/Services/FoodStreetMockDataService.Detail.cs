using System.Globalization;
using VinhKhanh.MobileApp.Helpers;
using VinhKhanh.MobileApp.Models;

namespace VinhKhanh.MobileApp.Services;

public sealed partial class FoodStreetApiDataService
{
    private PoiExperienceDetail BuildPoiDetail(
        PoiDto poi,
        string category,
        IReadOnlyList<TranslationDto>? translations,
        IReadOnlyList<AudioGuideDto>? audioGuides,
        IReadOnlyList<FoodItemDto>? foodItems,
        IReadOnlyDictionary<string, IReadOnlyList<TranslationDto>> foodItemTranslationsById,
        IReadOnlyList<PromotionDto>? promotions,
        IReadOnlyDictionary<string, IReadOnlyList<TranslationDto>> promotionTranslationsById,
        IReadOnlyDictionary<string, IReadOnlyList<string>> poiImages,
        IReadOnlyDictionary<string, IReadOnlyList<string>> foodImages,
        IReadOnlyDictionary<string, IReadOnlyList<string>> foodItemImagesById)
    {
        var localizedCategory = LocalizeCategory(category);
        var localizedAddress = LocalizeAddress(poi.Address, poi.Id);

        var detail = new PoiExperienceDetail
        {
            Id = poi.Id,
            Category = localizedCategory,
            Address = localizedAddress,
            PriceRange = poi.PriceRange?.Trim() ?? string.Empty,
            Latitude = poi.Lat,
            Longitude = poi.Lng,
            IsFeatured = poi.Featured,
            Tags = poi.Tags
                .Where(item => !string.IsNullOrWhiteSpace(item))
                .Select(LocalizeTag)
                .Where(item => !string.IsNullOrWhiteSpace(item))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList(),
            FoodItems = BuildFoodItems(foodItems, foodItemTranslationsById, foodItemImagesById).ToList(),
            Promotions = BuildPromotions(promotions, promotionTranslationsById).ToList()
        };

        PopulateLocalizedContent(detail.Name, translations, item => item.Title);
        PopulateLocalizedContent(detail.Summary, translations, item => FirstNonEmpty(item.ShortText, item.FullText));
        PopulateLocalizedContent(detail.Description, translations, item => item.FullText);
        SeedLocalizedSourceContent(detail.Name, poi.SourceLanguageCode, poi.Title);
        SeedLocalizedSourceContent(detail.Summary, poi.SourceLanguageCode, poi.ShortDescription, poi.Description);
        SeedLocalizedSourceContent(detail.Description, poi.SourceLanguageCode, poi.AudioScript, poi.Description, poi.ShortDescription);

        if (audioGuides is not null)
        {
            foreach (var audioGuide in audioGuides
                         .Where(item => IsPlayableAudioGuide(item))
                         .OrderByDescending(item => item.UpdatedAt))
            {
                detail.AudioUrls.Set(audioGuide.LanguageCode, audioGuide.AudioUrl);
                detail.AudioAssets.Set(new PoiAudioAsset
                {
                    AudioGuideId = audioGuide.Id,
                    PoiId = detail.Id,
                    LanguageCode = audioGuide.LanguageCode,
                    AudioUrl = audioGuide.AudioUrl,
                    SourceType = audioGuide.SourceType,
                    ContentVersion = audioGuide.ContentVersion,
                    TextHash = audioGuide.TextHash,
                    DurationInSeconds = audioGuide.DurationInSeconds,
                    FileSizeBytes = audioGuide.FileSizeBytes,
                    UpdatedAt = audioGuide.UpdatedAt
                });
            }
        }

        detail.Images = BuildImageGallery(poi.Id, poiImages, foodImages).ToList();
        return detail;
    }

    private PoiLocation CreatePoiLocation(PoiDto poi, PoiExperienceDetail detail)
    {
        var title = LocalizedTextHelper.GetLocalizedText(detail.Name, CurrentLanguageCode);
        var summary = LocalizedTextHelper.GetLocalizedText(detail.Summary, CurrentLanguageCode);

        return new PoiLocation
        {
            Id = detail.Id,
            Title = FirstNonEmpty(title, CreateSafePoiTitleFallback(poi.Slug), poi.Id),
            ShortDescription = FirstNonEmpty(
                summary,
                LocalizedTextHelper.GetLocalizedText(detail.Description, CurrentLanguageCode),
                detail.Category),
            Address = detail.Address,
            Category = detail.Category,
            PriceRange = poi.PriceRange?.Trim() ?? string.Empty,
            ThumbnailUrl = detail.Images.FirstOrDefault() ?? DefaultBackdropImageUrl,
            Latitude = detail.Latitude,
            Longitude = detail.Longitude,
            IsFeatured = detail.IsFeatured,
            TriggerRadius = double.IsFinite(poi.TriggerRadius) && poi.TriggerRadius >= 20d
                ? poi.TriggerRadius
                : 20d,
            Priority = Math.Max(0, poi.Priority),
            HeatIntensity = ResolveHeatIntensity(poi, []),
            DistanceText = FormatVisitDuration(Math.Max(10, poi.AverageVisitDuration))
        };
    }

    private PoiLocation CreatePoiLocation(
        PoiDto poi,
        string category,
        IReadOnlyList<TranslationDto>? translations,
        string thumbnailUrl)
    {
        var translation = SelectTranslation(translations, CurrentLanguageCode, "poi", poi.Id);
        var localizedTitle = FirstNonEmpty(
            GetTranslationText(translation, value => value.Title),
            GetSourceTextForCurrentLanguage(poi.Title),
            CreateSafePoiTitleFallback(poi.Slug),
            poi.Id);
        var localizedSummary = FirstNonEmpty(
            GetTranslationText(translation, value => value.ShortText, value => value.FullText),
            GetSourceTextForCurrentLanguage(poi.ShortDescription),
            category);

        return new PoiLocation
        {
            Id = poi.Id,
            Title = localizedTitle,
            ShortDescription = localizedSummary,
            Address = LocalizeAddress(poi.Address, poi.Id),
            Category = category,
            PriceRange = poi.PriceRange?.Trim() ?? string.Empty,
            ThumbnailUrl = string.IsNullOrWhiteSpace(thumbnailUrl) ? DefaultBackdropImageUrl : thumbnailUrl,
            Latitude = poi.Lat,
            Longitude = poi.Lng,
            IsFeatured = poi.Featured,
            TriggerRadius = double.IsFinite(poi.TriggerRadius) && poi.TriggerRadius >= 20d
                ? poi.TriggerRadius
                : 20d,
            Priority = Math.Max(0, poi.Priority),
            HeatIntensity = ResolveHeatIntensity(poi, []),
            DistanceText = FormatVisitDuration(Math.Max(10, poi.AverageVisitDuration))
        };
    }

    private PoiExperienceDetail? BuildFallbackPoiDetail(string poiId)
    {
        var poi = FallbackPois.FirstOrDefault(item => string.Equals(item.Id, poiId, StringComparison.OrdinalIgnoreCase));
        if (poi is null)
        {
            return null;
        }

        var detail = new PoiExperienceDetail
        {
            Id = poi.Id,
            Category = LocalizeCategory(poi.Category),
            Address = LocalizeAddress(poi.Address, poi.Id),
            Latitude = poi.Latitude,
            Longitude = poi.Longitude,
            IsFeatured = poi.IsFeatured,
            Images =
            [
                poi.ThumbnailUrl,
                GetBackdropImageUrl()
            ]
        };

        SetLocalizedValues(detail.Name, GetFallbackPoiNameTranslations(poi.Id, poi.Title));
        SetLocalizedValues(detail.Summary, GetFallbackPoiSummaryTranslations(poi.Id, poi.ShortDescription));
        SetLocalizedValues(detail.Description, GetFallbackPoiDescriptionTranslations(poi.Id, detail.Address, detail.Category, poi.ShortDescription));

        return detail;
    }

    private static IReadOnlyList<string> BuildImageGallery(
        string poiId,
        IReadOnlyDictionary<string, IReadOnlyList<string>> poiImages,
        IReadOnlyDictionary<string, IReadOnlyList<string>> foodImages)
    {
        var images = new List<string>();

        if (poiImages.TryGetValue(poiId, out var poiImageList))
        {
            images.AddRange(poiImageList.Where(item => !string.IsNullOrWhiteSpace(item)));
        }

        if (foodImages.TryGetValue(poiId, out var foodImageList))
        {
            images.AddRange(foodImageList.Where(item => !string.IsNullOrWhiteSpace(item)));
        }

        if (images.Count == 0)
        {
            images.Add(DefaultBackdropImageUrl);
        }

        return images
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private IReadOnlyList<PoiFoodItemDetail> BuildFoodItems(
        IReadOnlyList<FoodItemDto>? foodItems,
        IReadOnlyDictionary<string, IReadOnlyList<TranslationDto>> foodItemTranslationsById,
        IReadOnlyDictionary<string, IReadOnlyList<string>> foodItemImagesById)
    {
        if (foodItems is null || foodItems.Count == 0)
        {
            return [];
        }

        return foodItems
            .Select(item =>
            {
                foodItemTranslationsById.TryGetValue(item.Id, out var foodTranslations);
                var translation = SelectTranslation(foodTranslations, CurrentLanguageCode, "food_item", item.Id);
                var name = FirstNonEmpty(
                    GetTranslationText(translation, value => value.Title),
                    GetSourceTextForCurrentLanguage(item.Name),
                    item.Id);
                var description = FirstNonEmpty(
                    GetTranslationText(translation, value => value.FullText, value => value.ShortText),
                    GetSourceTextForCurrentLanguage(item.Description));
                var priceRange = item.PriceRange?.Trim() ?? string.Empty;

                return new PoiFoodItemDetail
                {
                    Id = item.Id,
                    Name = name,
                    Description = description,
                    PriceRange = priceRange,
                    ImageUrl = ResolveFoodItemImageUrl(item, foodItemImagesById),
                    SpicyLevel = item.SpicyLevel.Trim(),
                    SpicyLevelLabel = LocalizeSpicyLevel(item.SpicyLevel)
                };
            })
            .Where(item =>
                !string.IsNullOrWhiteSpace(item.Name) ||
                !string.IsNullOrWhiteSpace(item.Description) ||
                !string.IsNullOrWhiteSpace(item.PriceRange))
            .OrderBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(item => item.Id, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static string ResolveFoodItemImageUrl(
        FoodItemDto item,
        IReadOnlyDictionary<string, IReadOnlyList<string>> foodItemImagesById)
    {
        if (foodItemImagesById.TryGetValue(item.Id, out var imageUrls))
        {
            var mediaImage = imageUrls.FirstOrDefault(image => !string.IsNullOrWhiteSpace(image));
            if (!string.IsNullOrWhiteSpace(mediaImage))
            {
                return mediaImage.Trim();
            }
        }

        return item.ImageUrl.Trim();
    }

    private IReadOnlyList<PoiPromotionDetail> BuildPromotions(
        IReadOnlyList<PromotionDto>? promotions,
        IReadOnlyDictionary<string, IReadOnlyList<TranslationDto>> promotionTranslationsById)
    {
        if (promotions is null || promotions.Count == 0)
        {
            return [];
        }

        return promotions
            .OrderBy(item => ResolvePromotionSortOrder(item.Status))
            .ThenBy(item => item.StartAt)
            .ThenBy(item => item.EndAt)
            .Select(item =>
            {
                promotionTranslationsById.TryGetValue(item.Id, out var promotionTranslations);
                var translation = SelectTranslation(promotionTranslations, CurrentLanguageCode, "promotion", item.Id);
                var title = FirstNonEmpty(
                    GetTranslationText(translation, value => value.Title),
                    GetSourceTextForCurrentLanguage(item.Title));
                var description = FirstNonEmpty(
                    GetTranslationText(translation, value => value.FullText, value => value.ShortText),
                    GetSourceTextForCurrentLanguage(item.Description));

                return new PoiPromotionDetail
                {
                    Id = item.Id,
                    Title = title,
                    Description = description,
                    Status = item.Status?.Trim() ?? string.Empty,
                    StartAt = item.StartAt,
                    EndAt = item.EndAt,
                    StatusLabel = LocalizePromotionStatus(item.Status),
                    PeriodText = FormatPromotionPeriod(item.StartAt, item.EndAt)
                };
            })
            .Where(item =>
                !string.IsNullOrWhiteSpace(item.Title) ||
                !string.IsNullOrWhiteSpace(item.Description))
            .ToList();
    }

    private static int ResolvePromotionSortOrder(string? status)
        => status?.Trim().ToLowerInvariant() switch
        {
            "active" => 0,
            "upcoming" => 1,
            "expired" => 2,
            _ => 3
        };

    private string LocalizeSpicyLevel(string? spicyLevel)
        => spicyLevel?.Trim().ToLowerInvariant() switch
        {
            "mild" => _languageService.GetText("poi_detail_spicy_mild"),
            "medium" => _languageService.GetText("poi_detail_spicy_medium"),
            "hot" => _languageService.GetText("poi_detail_spicy_hot"),
            _ => spicyLevel?.Trim() ?? string.Empty
        };

    private string LocalizePromotionStatus(string? status)
        => status?.Trim().ToLowerInvariant() switch
        {
            "active" => _languageService.GetText("poi_detail_status_active"),
            "upcoming" => _languageService.GetText("poi_detail_status_upcoming"),
            "expired" => _languageService.GetText("poi_detail_status_expired"),
            _ => _languageService.GetText("poi_detail_status_info")
        };

    private string LocalizeTag(string? tag)
    {
        if (string.IsNullOrWhiteSpace(tag))
        {
            return string.Empty;
        }

        var normalizedTag = TextEncodingHelper.NormalizeDisplayText(tag).Trim();
        return NormalizeLookupKey(normalizedTag) switch
        {
            "an-vat" => SelectLocalizedText(CreateLocalizedMap("Ăn vặt", "Snacks", "小吃", "간식", "軽食", "Snacks")),
            "che" => SelectLocalizedText(CreateLocalizedMap("Chè", "Sweet soup", "甜汤", "베트남 디저트", "ベトナム甘味", "Dessert vietnamien")),
            "gia-dinh" => SelectLocalizedText(CreateLocalizedMap("Gia đình", "Family friendly", "适合家庭", "가족에게 좋음", "家族向け", "Adapté aux familles")),
            "dac-san" => SelectLocalizedText(CreateLocalizedMap("Đặc sản", "Signature dish", "招牌菜", "시그니처 메뉴", "名物料理", "Spécialité")),
            "oc" => SelectLocalizedText(CreateLocalizedMap("Ốc", "Snails", "螺贝", "달팽이 요리", "貝料理", "Escargots")),
            "do-song" => SelectLocalizedText(CreateLocalizedMap("Đồ sống", "Raw dishes", "生食", "생식", "生もの", "Plats crus")),
            "hai-san" => SelectLocalizedText(CreateLocalizedMap("Hải sản", "Seafood", "海鲜", "해산물", "海鮮", "Fruits de mer")),
            "tu-tap" => SelectLocalizedText(CreateLocalizedMap("Tụ tập", "Group-friendly", "聚会", "모임", "集まり向け", "Idéal en groupe")),
            "sushi" => SelectLocalizedText(CreateLocalizedMap("Sushi", "Sushi", "寿司", "스시", "寿司", "Sushi")),
            _ => LocalizationFallbackPolicy.SourceTextForLanguage(normalizedTag, CurrentLanguageCode)
        };
    }

    private string GetSourceTextForCurrentLanguage(string? value)
        => TextEncodingHelper.NormalizeDisplayText(
            LocalizationFallbackPolicy.SourceTextForLanguage(value, CurrentLanguageCode) ?? string.Empty);

    private static string GetTranslationText(
        TranslationDto? translation,
        params Func<TranslationDto, string?>[] valueSelectors)
    {
        if (translation is null)
        {
            return string.Empty;
        }

        foreach (var selector in valueSelectors)
        {
            var value = selector(translation);
            if (LocalizationFallbackPolicy.IsUsableTextForLanguage(value, translation.LanguageCode))
            {
                return TextEncodingHelper.NormalizeDisplayText(value);
            }
        }

        return string.Empty;
    }

    private string FormatPromotionPeriod(DateTimeOffset startAt, DateTimeOffset endAt)
    {
        if (startAt == default && endAt == default)
        {
            return string.Empty;
        }

        var culture = _languageService.CurrentCulture;
        const string dateFormat = "d";

        if (startAt != default && endAt != default)
        {
            return $"{startAt.ToLocalTime().ToString(dateFormat, culture)} - {endAt.ToLocalTime().ToString(dateFormat, culture)}";
        }

        if (startAt != default)
        {
            return $"{_languageService.GetText("poi_detail_period_from")} {startAt.ToLocalTime().ToString(dateFormat, culture)}";
        }

        return $"{_languageService.GetText("poi_detail_period_until")} {endAt.ToLocalTime().ToString(dateFormat, culture)}";
    }

    private static string ResolveBackdropImageUrl(IReadOnlyDictionary<string, PoiExperienceDetail> poiDetails)
        => poiDetails.Values
            .Where(item => item.IsFeatured)
            .SelectMany(item => item.Images)
            .FirstOrDefault(item => !string.IsNullOrWhiteSpace(item))
           ?? poiDetails.Values
               .SelectMany(item => item.Images)
               .FirstOrDefault(item => !string.IsNullOrWhiteSpace(item))
           ?? DefaultBackdropImageUrl;

    private static void PopulateLocalizedContent(
        LocalizedTextSet target,
        IReadOnlyList<TranslationDto>? translations,
        Func<TranslationDto, string?> valueSelector)
    {
        if (translations is null)
        {
            return;
        }

        foreach (var translation in translations)
        {
            var value = valueSelector(translation);
            if (LocalizationFallbackPolicy.IsUsableTextForLanguage(value, translation.LanguageCode))
            {
                target.Set(translation.LanguageCode, value);
            }
        }
    }

    private void SeedLocalizedSourceContent(
        LocalizedTextSet target,
        string? sourceLanguageCode,
        params string?[] candidateValues)
    {
        var normalizedSourceLanguage = AppLanguage.NormalizeCode(
            string.IsNullOrWhiteSpace(sourceLanguageCode)
                ? AppLanguage.DefaultLanguage
                : sourceLanguageCode);
        var normalizedCurrentLanguage = CurrentLanguageCode;

        foreach (var candidateValue in candidateValues)
        {
            if (string.IsNullOrWhiteSpace(candidateValue))
            {
                continue;
            }

            var normalizedValue = TextEncodingHelper.NormalizeDisplayText(candidateValue);
            if (string.IsNullOrWhiteSpace(normalizedValue))
            {
                continue;
            }

            if (LocalizationFallbackPolicy.IsUsableTextForLanguage(normalizedValue, normalizedCurrentLanguage))
            {
                SetLocalizedValue(target, normalizedCurrentLanguage, normalizedValue);
                return;
            }

            if (LocalizationFallbackPolicy.IsUsableTextForLanguage(normalizedValue, normalizedSourceLanguage))
            {
                SetLocalizedValue(target, normalizedSourceLanguage, normalizedValue);
                return;
            }
        }
    }

    private static void SetLocalizedValues(LocalizedTextSet target, IReadOnlyDictionary<string, string> values)
    {
        foreach (var pair in values)
        {
            target.Set(pair.Key, pair.Value);
        }
    }

    private static void SetLocalizedValue(LocalizedTextSet target, string languageCode, string value)
    {
        var normalizedLanguageCode = AppLanguage.NormalizeCode(languageCode);
        target.Set(normalizedLanguageCode, value);

        var separatorIndex = normalizedLanguageCode.IndexOf('-');
        if (separatorIndex > 0)
        {
            target.Set(normalizedLanguageCode[..separatorIndex], value);
        }
    }

    private static void EnsureLocalizedFallbacks(LocalizedTextSet target, IReadOnlyDictionary<string, string> values)
    {
        foreach (var pair in values)
        {
            if (!target.Values.ContainsKey(pair.Key))
            {
                target.Set(pair.Key, pair.Value);
            }
        }
    }
}
