using VinhKhanh.BackendApi.Domain.Entities;
using VinhKhanh.BackendApi.DTOs;

namespace VinhKhanh.BackendApi.Mappings;

public static class GuideMappingExtensions
{
    public static GuidePoiTranslation? SelectTranslation(this GuidePoi poi, string languageCode, string fallbackLanguage)
    {
        return poi.Translations.FirstOrDefault(item => item.LanguageCode.Equals(languageCode, StringComparison.OrdinalIgnoreCase))
            ?? poi.Translations.FirstOrDefault(item => item.LanguageCode.StartsWith(languageCode, StringComparison.OrdinalIgnoreCase))
            ?? poi.Translations.FirstOrDefault(item => item.LanguageCode.Equals(poi.DefaultLanguageCode, StringComparison.OrdinalIgnoreCase))
            ?? poi.Translations.FirstOrDefault(item => item.LanguageCode.Equals(fallbackLanguage, StringComparison.OrdinalIgnoreCase))
            ?? poi.Translations.OrderBy(item => item.LanguageCode).FirstOrDefault();
    }

    public static GuidePoiAudioGuide? SelectAudioGuide(this GuidePoi poi, string languageCode, string fallbackLanguage)
    {
        return poi.AudioGuides.FirstOrDefault(item =>
                   item.Status.Equals("ready", StringComparison.OrdinalIgnoreCase) &&
                   item.LanguageCode.Equals(languageCode, StringComparison.OrdinalIgnoreCase))
            ?? poi.AudioGuides.FirstOrDefault(item =>
                   item.Status.Equals("ready", StringComparison.OrdinalIgnoreCase) &&
                   item.LanguageCode.Equals(fallbackLanguage, StringComparison.OrdinalIgnoreCase))
            ?? poi.AudioGuides.FirstOrDefault(item => item.Status.Equals("ready", StringComparison.OrdinalIgnoreCase));
    }

    public static PoiSummaryDto ToSummaryDto(
        this GuidePoi poi,
        string languageCode,
        string fallbackLanguage,
        string deepLinkScheme,
        double? distanceMeters = null)
    {
        var translation = poi.SelectTranslation(languageCode, fallbackLanguage);
        var audioGuide = poi.SelectAudioGuide(languageCode, fallbackLanguage);
        var image = poi.MediaAssets
            .Where(item => item.MediaType.Equals("image", StringComparison.OrdinalIgnoreCase))
            .OrderBy(item => item.CreatedAt)
            .FirstOrDefault();

        return new PoiSummaryDto(
            poi.Id,
            poi.Slug,
            translation?.Title ?? poi.Slug,
            translation?.ShortText ?? translation?.FullText ?? string.Empty,
            poi.AddressLine,
            poi.Category?.Name ?? string.Empty,
            poi.Category?.Color ?? "#de6245",
            image?.Url ?? string.Empty,
            Convert.ToDouble(poi.Latitude),
            Convert.ToDouble(poi.Longitude),
            poi.District,
            poi.Ward,
            poi.PriceRange,
            string.IsNullOrWhiteSpace(poi.OpeningHours) ? "17:00 - 23:30" : poi.OpeningHours,
            poi.IsFeatured,
            audioGuide is not null,
            string.IsNullOrWhiteSpace(poi.QrCode) ? poi.Slug : poi.QrCode,
            $"{deepLinkScheme}://poi/{poi.Slug}",
            distanceMeters,
            poi.Tags.Select(item => item.TagValue).OrderBy(item => item).ToList(),
            poi.FoodItems.Select(item => item.Name).Take(4).ToList());
    }

    public static PoiDetailDto ToDetailDto(
        this GuidePoi poi,
        string languageCode,
        string fallbackLanguage,
        string deepLinkScheme,
        IReadOnlyList<GuideTourRoute> routes)
    {
        var selectedTranslation = poi.SelectTranslation(languageCode, fallbackLanguage);
        var image = poi.MediaAssets
            .Where(item => item.MediaType.Equals("image", StringComparison.OrdinalIgnoreCase))
            .OrderBy(item => item.CreatedAt)
            .FirstOrDefault();

        var routeDtos = routes
            .Where(route => route.Stops.Any(stop => stop.PoiId == poi.Id))
            .Select(route => new TourRouteDto(
                route.Id,
                route.Name,
                route.Description,
                route.DurationMinutes,
                route.Difficulty,
                route.IsFeatured,
                route.Stops
                    .OrderBy(stop => stop.StopOrder)
                    .Select(stop =>
                    {
                        var stopTranslation = stop.Poi?.SelectTranslation(languageCode, fallbackLanguage);
                        return new TourRouteStopDto(
                            stop.PoiId,
                            stopTranslation?.Title ?? stop.Poi?.Slug ?? stop.PoiId,
                            Convert.ToDouble(stop.Poi?.Latitude ?? 0),
                            Convert.ToDouble(stop.Poi?.Longitude ?? 0),
                            stop.StopOrder);
                    })
                    .ToList()))
            .ToList();

        var narrations = poi.Translations
            .OrderBy(item => item.LanguageCode)
            .Select(translation =>
            {
                var audioGuide = poi.AudioGuides.FirstOrDefault(item =>
                    item.Status.Equals("ready", StringComparison.OrdinalIgnoreCase) &&
                    item.LanguageCode.Equals(translation.LanguageCode, StringComparison.OrdinalIgnoreCase));

                return new PoiNarrationDto(
                    translation.LanguageCode,
                    translation.Title,
                    translation.ShortText,
                    translation.FullText,
                    translation.FullText,
                    string.IsNullOrWhiteSpace(audioGuide?.AudioUrl) ? null : audioGuide.AudioUrl,
                    audioGuide?.VoiceType ?? "standard",
                    string.IsNullOrWhiteSpace(audioGuide?.AudioUrl));
            })
            .ToList();

        return new PoiDetailDto(
            poi.Id,
            poi.Slug,
            selectedTranslation?.Title ?? poi.Slug,
            selectedTranslation?.ShortText ?? string.Empty,
            selectedTranslation?.FullText ?? string.Empty,
            poi.AddressLine,
            Convert.ToDouble(poi.Latitude),
            Convert.ToDouble(poi.Longitude),
            poi.Category?.Name ?? string.Empty,
            string.IsNullOrWhiteSpace(poi.OpeningHours) ? "17:00 - 23:30" : poi.OpeningHours,
            poi.PriceRange,
            poi.IsFeatured,
            string.IsNullOrWhiteSpace(poi.QrCode) ? poi.Slug : poi.QrCode,
            $"{deepLinkScheme}://poi/{poi.Slug}",
            image?.Url ?? string.Empty,
            poi.Tags.Select(item => item.TagValue).OrderBy(item => item).ToList(),
            poi.FoodItems
                .OrderBy(item => item.Name)
                .Select(item => new FoodItemDto(item.Id, item.Name, item.Description, item.PriceRange, item.ImageUrl, item.SpicyLevel))
                .ToList(),
            poi.MediaAssets
                .OrderBy(item => item.CreatedAt)
                .Select(item => new MediaAssetDto(item.Id, item.MediaType, item.Url, item.AltText))
                .ToList(),
            narrations,
            routeDtos);
    }

    public static double CalculateDistanceMeters(double startLatitude, double startLongitude, double endLatitude, double endLongitude)
    {
        const double earthRadiusMeters = 6371000;
        var dLat = DegreesToRadians(endLatitude - startLatitude);
        var dLon = DegreesToRadians(endLongitude - startLongitude);

        var lat1 = DegreesToRadians(startLatitude);
        var lat2 = DegreesToRadians(endLatitude);

        var a = Math.Sin(dLat / 2) * Math.Sin(dLat / 2) +
                Math.Cos(lat1) * Math.Cos(lat2) *
                Math.Sin(dLon / 2) * Math.Sin(dLon / 2);
        var c = 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));

        return earthRadiusMeters * c;
    }

    private static double DegreesToRadians(double degrees) => degrees * Math.PI / 180d;
}
