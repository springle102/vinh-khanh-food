using VinhKhanh.MobileApp.Helpers;
using VinhKhanh.MobileApp.Models;

namespace VinhKhanh.MobileApp.Services;

public sealed partial class FoodStreetMockDataService
{
    private readonly Dictionary<string, PoiExperienceDetail> _detailCache = new(StringComparer.OrdinalIgnoreCase);

    private PoiExperienceDetail CreateLocalizedDetail(PoiRuntimeSeed seed)
    {
        var template = seed.DetailTemplate;
        var detail = new PoiExperienceDetail
        {
            Id = template.Id,
            Category = LocalizeCategory(seed.CategoryName),
            Address = LocalizeAddress(seed.Poi.Address, seed.Poi.Id),
            Latitude = template.Latitude,
            Longitude = template.Longitude,
            Rating = template.Rating,
            ReviewCount = template.ReviewCount,
            IsFeatured = template.IsFeatured,
            Images = template.Images.ToList()
        };

        CopyLocalizedValues(detail.Name, template.Name);
        CopyLocalizedValues(detail.Summary, template.Summary);
        CopyLocalizedValues(detail.Description, template.Description);
        CopyLocalizedValues(detail.AudioUrls, template.AudioUrls);
        return detail;
    }

    private PoiExperienceDetail BuildPoiDetail(
        PoiDto poi,
        string category,
        IReadOnlyList<TranslationDto>? translations,
        IReadOnlyList<AudioGuideDto>? audioGuides,
        IReadOnlyList<ReviewDto>? reviews,
        IReadOnlyDictionary<string, IReadOnlyList<string>> poiImages,
        IReadOnlyDictionary<string, IReadOnlyList<string>> foodImages)
    {
        var localizedCategory = LocalizeCategory(category);
        var localizedAddress = LocalizeAddress(poi.Address, poi.Id);
        var fallbackTitle = FirstNonEmpty(CreateTitleFromSlug(poi.Slug), poi.Id);

        var detail = new PoiExperienceDetail
        {
            Id = poi.Id,
            Category = localizedCategory,
            Address = localizedAddress,
            Latitude = poi.Lat,
            Longitude = poi.Lng,
            IsFeatured = poi.Featured
        };

        PopulateLocalizedContent(detail.Name, translations, item => item.Title);
        EnsureLocalizedFallbacks(detail.Name, GetFallbackPoiNameTranslations(poi.Id, fallbackTitle));

        PopulateLocalizedContent(detail.Summary, translations, item => FirstNonEmpty(item.ShortText, item.FullText));
        EnsureLocalizedFallbacks(detail.Summary, GetFallbackPoiSummaryTranslations(poi.Id, fallbackTitle));

        PopulateLocalizedContent(detail.Description, translations, item => FirstNonEmpty(item.FullText, item.ShortText));
        EnsureLocalizedFallbacks(detail.Description, GetFallbackPoiDescriptionTranslations(poi.Id, localizedAddress, localizedCategory, fallbackTitle));

        if (audioGuides is not null)
        {
            foreach (var audioGuide in audioGuides
                         .Where(item => !string.IsNullOrWhiteSpace(item.AudioUrl))
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
            : Math.Round(4.0 + (Clamp(poi.PopularityScore / 100d, 0.0, 1.0) * 0.9), 1);

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
            PriceRange = poi.PriceRange,
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

        if (FallbackPoiImages.TryGetValue(poiId, out var fallbackImage) && !string.IsNullOrWhiteSpace(fallbackImage))
        {
            images.Add(fallbackImage);
        }

        if (images.Count == 0)
        {
            images.Add(DefaultBackdropImageUrl);
        }

        return images
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
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

    private static void CopyLocalizedValues(LocalizedTextSet target, LocalizedTextSet source)
    {
        foreach (var pair in source.Values)
        {
            target.Set(pair.Key, pair.Value);
        }
    }
}
