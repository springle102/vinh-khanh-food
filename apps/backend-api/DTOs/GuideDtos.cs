namespace VinhKhanh.BackendApi.DTOs;

public sealed record PagedResult<T>(
    IReadOnlyList<T> Items,
    int Page,
    int PageSize,
    int TotalItems,
    int TotalPages);

public sealed record LanguageOptionDto(
    string Code,
    string DisplayName,
    bool IsPremium);

public sealed record PoiSearchCriteria(
    string LanguageCode,
    string? Search,
    string? CategoryId,
    string? Area,
    string? Dish,
    bool? Featured,
    int Page,
    int PageSize);

public sealed record AdminLoginRequestDto(string Email, string Password);

public sealed record AdminRefreshTokenRequestDto(string RefreshToken);

public sealed record TokenResponseDto(
    string UserId,
    string Name,
    string Email,
    string Role,
    string? ManagedPoiId,
    string AccessToken,
    string RefreshToken,
    DateTimeOffset ExpiresAt);

public sealed record FoodItemDto(
    string Id,
    string Name,
    string Description,
    string PriceRange,
    string ImageUrl,
    string SpicyLevel);

public sealed record MediaAssetDto(
    string Id,
    string MediaType,
    string Url,
    string AltText);

public sealed record PoiNarrationDto(
    string LanguageCode,
    string Title,
    string ShortDescription,
    string FullDescription,
    string NarrationText,
    string? AudioUrl,
    string VoiceType,
    bool UsesGeneratedTtsFallback);

public sealed record TourRouteStopDto(
    string PoiId,
    string Title,
    double Latitude,
    double Longitude,
    int StopOrder);

public sealed record TourRouteDto(
    string Id,
    string Name,
    string Description,
    int DurationMinutes,
    string Difficulty,
    bool IsFeatured,
    IReadOnlyList<TourRouteStopDto> Stops);

public sealed record PoiSummaryDto(
    string Id,
    string Slug,
    string Title,
    string ShortDescription,
    string Address,
    string Category,
    string CategoryColor,
    string ThumbnailUrl,
    double Latitude,
    double Longitude,
    string District,
    string Ward,
    string PriceRange,
    string OpeningHours,
    bool IsFeatured,
    bool HasAudioGuide,
    string QrCode,
    string DeepLink,
    double? DistanceMeters,
    IReadOnlyList<string> Tags,
    IReadOnlyList<string> HighlightedDishes);

public sealed record PoiDetailDto(
    string Id,
    string Slug,
    string Title,
    string ShortDescription,
    string FullDescription,
    string Address,
    double Latitude,
    double Longitude,
    string Category,
    string OpeningHours,
    string PriceRange,
    bool IsFeatured,
    string QrCode,
    string DeepLink,
    string ThumbnailUrl,
    IReadOnlyList<string> Tags,
    IReadOnlyList<FoodItemDto> FoodItems,
    IReadOnlyList<MediaAssetDto> MediaAssets,
    IReadOnlyList<PoiNarrationDto> Narrations,
    IReadOnlyList<TourRouteDto> SuggestedRoutes);

public sealed record TrackPoiViewRequestDto(
    string LanguageCode,
    string DeviceType);

public sealed record TrackPoiAudioRequestDto(
    string LanguageCode,
    int DurationInSeconds);

public sealed record AdminPoiUpsertRequestDto(
    string Slug,
    string Address,
    double Latitude,
    double Longitude,
    string CategoryId,
    string Status,
    bool IsFeatured,
    string DefaultLanguageCode,
    string District,
    string Ward,
    string PriceRange,
    int AverageVisitDurationMinutes,
    int PopularityScore,
    string UpdatedBy,
    string? OwnerUserId,
    string? QrCode,
    string? OpeningHours,
    IReadOnlyList<string> Tags);

public sealed record AdminPoiTranslationUpsertRequestDto(
    string LanguageCode,
    string Title,
    string ShortText,
    string FullText,
    string SeoTitle,
    string SeoDescription,
    bool IsPremium,
    string UpdatedBy);

public sealed record AdminPoiAudioUpsertRequestDto(
    string LanguageCode,
    string AudioUrl,
    string VoiceType,
    string SourceType,
    string Status,
    string UpdatedBy);

public sealed record AdminFoodItemUpsertRequestDto(
    string Name,
    string Description,
    string PriceRange,
    string ImageUrl,
    string SpicyLevel);

public sealed record AdminMediaAssetUpsertRequestDto(
    string MediaType,
    string Url,
    string AltText);

public sealed record MobileSettingsDto(
    string AppName,
    string SupportEmail,
    string DefaultLanguage,
    string FallbackLanguage,
    string MapProvider,
    string TtsProvider,
    int GeofenceRadiusMeters,
    int GeofenceCooldownMinutes,
    IReadOnlyList<LanguageOptionDto> SupportedLanguages);

public sealed record PoiTrafficDto(
    string PoiId,
    string Title,
    int Views,
    int AudioPlays);

public sealed record AnalyticsOverviewDto(
    int PublishedPois,
    int TotalViews,
    int TotalAudioPlays,
    IReadOnlyList<PoiTrafficDto> TopPois);
