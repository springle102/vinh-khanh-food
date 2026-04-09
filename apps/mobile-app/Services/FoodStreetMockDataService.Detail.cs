using System.Globalization;
using VinhKhanh.MobileApp.Helpers;
using VinhKhanh.MobileApp.Models;

namespace VinhKhanh.MobileApp.Services;

public sealed partial class FoodStreetApiDataService
{
    private readonly Dictionary<string, PoiExperienceDetail> _detailCache = new(StringComparer.OrdinalIgnoreCase);

    private PoiExperienceDetail BuildPoiDetail(
        PoiDto poi,
        string category,
        IReadOnlyList<TranslationDto>? translations,
        IReadOnlyList<AudioGuideDto>? audioGuides,
        IReadOnlyList<ReviewDto>? reviews,
        IReadOnlyList<FoodItemDto>? foodItems,
        IReadOnlyDictionary<string, IReadOnlyList<TranslationDto>> foodItemTranslationsById,
        IReadOnlyList<PromotionDto>? promotions,
        IReadOnlyDictionary<string, IReadOnlyList<TranslationDto>> promotionTranslationsById,
        IReadOnlyDictionary<string, IReadOnlyList<string>> poiImages,
        IReadOnlyDictionary<string, IReadOnlyList<string>> foodImages)
    {
        var localizedCategory = LocalizeCategory(category);
        var localizedAddress = LocalizeAddress(poi.Address, poi.Id);

        var detail = new PoiExperienceDetail
        {
            Id = poi.Id,
            Category = localizedCategory,
            Address = localizedAddress,
            PriceRange = TextEncodingHelper.NormalizeDisplayText(poi.PriceRange),
            Latitude = poi.Lat,
            Longitude = poi.Lng,
            IsFeatured = poi.Featured,
            Tags = poi.Tags
                .Where(item => !string.IsNullOrWhiteSpace(item))
                .Select(item => TextEncodingHelper.NormalizeDisplayText(item.Trim()))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList(),
            FoodItems = BuildFoodItems(foodItems, foodItemTranslationsById).ToList(),
            Promotions = BuildPromotions(promotions, promotionTranslationsById).ToList()
        };

        PopulateLocalizedContent(detail.Name, translations, item => item.Title);

        PopulateLocalizedContent(detail.Summary, translations, item => FirstNonEmpty(item.ShortText, item.FullText));

        PopulateLocalizedContent(detail.Description, translations, item => FirstNonEmpty(item.FullText, item.ShortText));

        if (audioGuides is not null)
        {
            foreach (var audioGuide in audioGuides
                         .Where(item =>
                             string.Equals(item.SourceType, "uploaded", StringComparison.OrdinalIgnoreCase) &&
                             !string.IsNullOrWhiteSpace(item.AudioUrl))
                         .OrderByDescending(item => item.UpdatedAt))
            {
                detail.AudioUrls.Set(audioGuide.LanguageCode, audioGuide.AudioUrl);
            }
        }

        detail.Images = BuildImageGallery(poi.Id, poiImages, foodImages).ToList();

        var reviewList = reviews?
            .Where(item => item.Rating > 0)
            .ToList() ?? [];
        detail.ReviewCount = reviewList.Count;
        detail.Rating = reviewList.Count > 0
            ? Math.Round(reviewList.Average(item => item.Rating), 1)
            : 0;

        return detail;
    }

    private PoiLocation CreatePoiLocation(PoiDto poi, PoiExperienceDetail detail)
    {
        var title = LocalizedTextHelper.GetLocalizedText(detail.Name, _languageService.CurrentLanguage);
        var summary = LocalizedTextHelper.GetLocalizedText(detail.Summary, _languageService.CurrentLanguage);

        return new PoiLocation
        {
            Id = detail.Id,
            Title = FirstNonEmpty(title, CreateTitleFromSlug(poi.Slug), poi.Id),
            ShortDescription = FirstNonEmpty(
                summary,
                LocalizedTextHelper.GetLocalizedText(detail.Description, _languageService.CurrentLanguage),
                detail.Category),
            Address = detail.Address,
            Category = detail.Category,
            PriceRange = TextEncodingHelper.NormalizeDisplayText(poi.PriceRange),
            ThumbnailUrl = detail.Images.FirstOrDefault() ?? DefaultBackdropImageUrl,
            Latitude = detail.Latitude,
            Longitude = detail.Longitude,
            IsFeatured = detail.IsFeatured,
            HeatIntensity = ResolveHeatIntensity(poi, [], []),
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
            Rating = poi.Id switch
            {
                "poi-snail-signature" => 4.8,
                "poi-bbq-night" => 4.7,
                "poi-sweet-lane" => 4.5,
                _ => 4.4
            },
            ReviewCount = poi.Id switch
            {
                "poi-snail-signature" => 248,
                "poi-bbq-night" => 196,
                "poi-sweet-lane" => 102,
                _ => 64
            },
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
        IReadOnlyDictionary<string, IReadOnlyList<TranslationDto>> foodItemTranslationsById)
    {
        if (foodItems is null || foodItems.Count == 0)
        {
            return [];
        }

        return foodItems
            .Select(item =>
            {
                foodItemTranslationsById.TryGetValue(item.Id, out var foodTranslations);
                var translation = SelectTranslation(foodTranslations, _languageService.CurrentLanguage);
                var name = TextEncodingHelper.NormalizeDisplayText(FirstNonEmpty(translation?.Title, item.Name, item.Id));
                var description = TextEncodingHelper.NormalizeDisplayText(FirstNonEmpty(translation?.FullText, translation?.ShortText, item.Description));
                var priceRange = TextEncodingHelper.NormalizeDisplayText(item.PriceRange);

                return new PoiFoodItemDetail
                {
                    Id = item.Id,
                    Name = name,
                    Description = description,
                    PriceRange = priceRange,
                    ImageUrl = item.ImageUrl.Trim(),
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
                var translation = SelectTranslation(promotionTranslations, _languageService.CurrentLanguage);
                var title = TextEncodingHelper.NormalizeDisplayText(FirstNonEmpty(translation?.Title, item.Title));
                var description = TextEncodingHelper.NormalizeDisplayText(FirstNonEmpty(translation?.FullText, translation?.ShortText, item.Description));

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
            _ => TextEncodingHelper.NormalizeDisplayText(spicyLevel)
        };

    private string LocalizePromotionStatus(string? status)
        => status?.Trim().ToLowerInvariant() switch
        {
            "active" => _languageService.GetText("poi_detail_status_active"),
            "upcoming" => _languageService.GetText("poi_detail_status_upcoming"),
            "expired" => _languageService.GetText("poi_detail_status_expired"),
            _ => _languageService.GetText("poi_detail_status_info")
        };

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
            if (!string.IsNullOrWhiteSpace(value))
            {
                target.Set(translation.LanguageCode, value);
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
