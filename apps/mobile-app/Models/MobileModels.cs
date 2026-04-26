using System.Collections.ObjectModel;
using System.Globalization;

namespace VinhKhanh.MobileApp.Models;

public sealed class AppLanguage
{
    public string Code { get; set; } = "vi";
    public string DisplayName { get; set; } = "Tiếng Việt";
    public bool IsPremium { get; set; }
    public bool IsSelected { get; set; }

    public string BadgeCode => Code switch
    {
        "vi" => "VN",
        "en" => "US",
        "zh-CN" => "CN",
        "ko" => "KR",
        "ja" => "JP",
        _ => Code.Length >= 2 ? Code[..2].ToUpperInvariant() : Code.ToUpperInvariant()
    };
}

public sealed class VoiceOption
{
    public string Id { get; set; } = "default";
    public string DisplayName { get; set; } = "Mặc định";
    public string Locale { get; set; } = "vi-VN";
    public bool IsSelected { get; set; }

    public string ShortName
    {
        get
        {
            var segments = DisplayName.Split('-', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
            return segments.LastOrDefault() ?? DisplayName;
        }
    }
}

public sealed class CategoryChipModel
{
    public string Key { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public bool IsSelected { get; set; }
}

public sealed class UserSettings
{
    public string SelectedLanguage { get; set; } = "vi";
    public string SelectedVoiceId { get; set; } = "default";
    public string SelectedVoiceLocale { get; set; } = "vi-VN";
    public bool AutoNarrationEnabled { get; set; } = true;
    public int GeofenceRadiusMeters { get; set; } = 10;
    public bool PreferPreparedAudio { get; set; } = true;
    public string ApiBaseUrl { get; set; } = "http://localhost:5080";
}

public sealed class ApiEnvelope<T>
{
    public bool Success { get; set; }
    public T? Data { get; set; }
    public string? Message { get; set; }
}

public sealed class PagedResultModel<T>
{
    public List<T> Items { get; set; } = [];
    public int Page { get; set; }
    public int PageSize { get; set; }
    public int TotalItems { get; set; }
    public int TotalPages { get; set; }
}

public sealed class MobileSettingsModel
{
    public string AppName { get; set; } = "VinhKhanhFoodGuide";
    public string SupportEmail { get; set; } = string.Empty;
    public string DefaultLanguage { get; set; } = "vi";
    public string FallbackLanguage { get; set; } = "en";
    public string MapProvider { get; set; } = "google-maps";
    public string TtsProvider { get; set; } = "elevenlabs";
    public int GeofenceRadiusMeters { get; set; } = 10;
    public int GeofenceCooldownMinutes { get; set; } = 20;
    public List<AppLanguage> SupportedLanguages { get; set; } = [];
}

public sealed class PoiSummaryModel
{
    public string Id { get; set; } = string.Empty;
    public string Slug { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string ShortDescription { get; set; } = string.Empty;
    public string Address { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public string CategoryColor { get; set; } = "#de6245";
    public string ThumbnailUrl { get; set; } = string.Empty;
    public double Latitude { get; set; }
    public double Longitude { get; set; }
    public string District { get; set; } = string.Empty;
    public string Ward { get; set; } = string.Empty;
    public string PriceRange { get; set; } = string.Empty;
    public string OpeningHours { get; set; } = string.Empty;
    public bool IsFeatured { get; set; }
    public bool HasAudioGuide { get; set; }
    public string DeepLink { get; set; } = string.Empty;
    public double? DistanceMeters { get; set; }
    public List<string> Tags { get; set; } = [];
    public List<string> HighlightedDishes { get; set; } = [];
    public double Rating { get; set; }

    public string ThumbnailSource => string.IsNullOrWhiteSpace(ThumbnailUrl) ? "coverdefault.svg" : ThumbnailUrl;

    public string DisplayRating => (Rating > 0 ? Rating : BuildFallbackRating(Id))
        .ToString("0.0", CultureInfo.InvariantCulture);

    public string CategoryDisplay => string.IsNullOrWhiteSpace(Category) ? "Ẩm thực" : Category.Trim();

    public string CapsuleText
    {
        get
        {
            var value = HighlightedDishes.FirstOrDefault() ?? Tags.FirstOrDefault() ?? CategoryDisplay;
            return value.ToUpperInvariant();
        }
    }

    public string AreaText
    {
        get
        {
            if (!string.IsNullOrWhiteSpace(District) && !string.IsNullOrWhiteSpace(Ward))
            {
                return $"{Ward}, {District}";
            }

            if (!string.IsNullOrWhiteSpace(District))
            {
                return District;
            }

            return string.IsNullOrWhiteSpace(Address) ? "Vĩnh Khánh, Quận 4" : Address;
        }
    }

    private static double BuildFallbackRating(string seed)
    {
        var hash = 17;
        foreach (var character in seed)
        {
            hash = (hash * 31) + character;
        }

        return 4.2d + (Math.Abs(hash) % 7) / 10d;
    }
}

public sealed class FoodItemModel
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string PriceRange { get; set; } = string.Empty;
    public string ImageUrl { get; set; } = string.Empty;
}

public sealed class MediaAssetModel
{
    public string Id { get; set; } = string.Empty;
    public string MediaType { get; set; } = string.Empty;
    public string Url { get; set; } = string.Empty;
    public string AltText { get; set; } = string.Empty;
}

public sealed class PoiNarrationModel
{
    public string LanguageCode { get; set; } = "vi";
    public string Title { get; set; } = string.Empty;
    public string ShortDescription { get; set; } = string.Empty;
    public string FullDescription { get; set; } = string.Empty;
    public string NarrationText { get; set; } = string.Empty;
    public string? AudioUrl { get; set; }
    public bool UsesGeneratedTtsFallback { get; set; }
}

public sealed class TourRouteStopModel
{
    public string PoiId { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public double Latitude { get; set; }
    public double Longitude { get; set; }
    public int StopOrder { get; set; }
}

public sealed class TourRouteModel
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public int DurationMinutes { get; set; }
    public string Difficulty { get; set; } = string.Empty;
    public bool IsFeatured { get; set; }
    public List<TourRouteStopModel> Stops { get; set; } = [];
}

public sealed class PoiDetailModel
{
    public string Id { get; set; } = string.Empty;
    public string Slug { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string ShortDescription { get; set; } = string.Empty;
    public string FullDescription { get; set; } = string.Empty;
    public string Address { get; set; } = string.Empty;
    public double Latitude { get; set; }
    public double Longitude { get; set; }
    public string Category { get; set; } = string.Empty;
    public string OpeningHours { get; set; } = string.Empty;
    public string PriceRange { get; set; } = string.Empty;
    public bool IsFeatured { get; set; }
    public string DeepLink { get; set; } = string.Empty;
    public string ThumbnailUrl { get; set; } = string.Empty;
    public List<string> Tags { get; set; } = [];
    public ObservableCollection<FoodItemModel> FoodItems { get; set; } = [];
    public ObservableCollection<MediaAssetModel> MediaAssets { get; set; } = [];
    public ObservableCollection<PoiNarrationModel> Narrations { get; set; } = [];
    public ObservableCollection<TourRouteModel> SuggestedRoutes { get; set; } = [];

    public string ThumbnailSource => string.IsNullOrWhiteSpace(ThumbnailUrl) ? "coverdefault.svg" : ThumbnailUrl;

    public string CategoryDisplay => string.IsNullOrWhiteSpace(Category) ? "Ẩm thực đường phố" : Category.Trim();

    public string Tagline => Tags.FirstOrDefault() ?? CategoryDisplay;
}

public sealed class TrackViewRequest
{
    public string LanguageCode { get; set; } = "vi";
    public string DeviceType { get; set; } = "android";
}

public sealed class TrackAudioRequest
{
    public string LanguageCode { get; set; } = "vi";
    public int DurationInSeconds { get; set; } = 0;
}

public sealed class LocationChangedMessage
{
    public double Latitude { get; set; }
    public double Longitude { get; set; }
}

