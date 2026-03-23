namespace VinhKhanh.BackendApi.Contracts;

public sealed record ApiResponse<T>(bool Success, T? Data, string? Message = null)
{
    public static ApiResponse<T> Ok(T data, string? message = null) => new(true, data, message);

    public static ApiResponse<T> Fail(string message) => new(false, default, message);
}

public sealed record AuthTokensResponse(
    string UserId,
    string Name,
    string Email,
    string Role,
    string AccessToken,
    string RefreshToken,
    DateTimeOffset ExpiresAt);

public sealed record LoginRequest(string Email, string Password);

public sealed record RefreshTokenRequest(string RefreshToken);

public sealed record LogoutRequest(string RefreshToken);

public sealed record PlaceUpsertRequest(
    string Slug,
    string Address,
    double Lat,
    double Lng,
    string CategoryId,
    string Status,
    bool Featured,
    string DefaultLanguageCode,
    string District,
    string Ward,
    string PriceRange,
    int AverageVisitDuration,
    int PopularityScore,
    List<string> Tags,
    string? OwnerUserId,
    string UpdatedBy);

public sealed record AdminUserUpsertRequest(
    string Name,
    string Email,
    string Phone,
    string Role,
    string Status,
    string AvatarColor,
    string? Password,
    string? ManagedPlaceId,
    string ActorName,
    string ActorRole);

public sealed record TranslationUpsertRequest(
    string EntityType,
    string EntityId,
    string LanguageCode,
    string Title,
    string ShortText,
    string FullText,
    string SeoTitle,
    string SeoDescription,
    bool IsPremium,
    string UpdatedBy);

public sealed record AudioGuideUpsertRequest(
    string EntityType,
    string EntityId,
    string LanguageCode,
    string AudioUrl,
    string VoiceType,
    string SourceType,
    string Status,
    string UpdatedBy);

public sealed record MediaAssetUpsertRequest(
    string EntityType,
    string EntityId,
    string Type,
    string Url,
    string AltText);

public sealed record FoodItemUpsertRequest(
    string PlaceId,
    string Name,
    string Description,
    string PriceRange,
    string ImageUrl,
    string SpicyLevel);

public sealed record TourRouteUpsertRequest(
    string Name,
    string Description,
    int DurationMinutes,
    string Difficulty,
    List<string> StopPlaceIds,
    bool IsFeatured,
    string ActorName,
    string ActorRole);

public sealed record PromotionUpsertRequest(
    string PlaceId,
    string Title,
    string Description,
    DateTimeOffset StartAt,
    DateTimeOffset EndAt,
    string Status,
    string ActorName,
    string ActorRole);

public sealed record ReviewCreateRequest(
    string PlaceId,
    string UserName,
    int Rating,
    string Comment,
    string LanguageCode);

public sealed record ReviewStatusRequest(
    string Status,
    string ActorName,
    string ActorRole);

public sealed record QrCodeStateRequest(
    bool IsActive,
    string ActorName,
    string ActorRole);

public sealed record QrCodeImageRequest(
    string QrImageUrl,
    string ActorName,
    string ActorRole);

public sealed record SystemSettingUpsertRequest(
    string AppName,
    string SupportEmail,
    string DefaultLanguage,
    string FallbackLanguage,
    List<string> FreeLanguages,
    List<string> PremiumLanguages,
    int PremiumUnlockPriceUsd,
    string MapProvider,
    string StorageProvider,
    string TtsProvider,
    int GeofenceRadiusMeters,
    bool QrAutoPlay,
    bool GuestReviewEnabled,
    int AnalyticsRetentionDays,
    string ActorName,
    string ActorRole);

public sealed record DashboardSummaryResponse(
    int TotalViews,
    int TotalListens,
    int PublishedPlaces,
    int MissingReadyAudio,
    int ActiveQrCount,
    int PendingReviews,
    int PremiumLanguageCount);

public sealed record AdminBootstrapResponse(
    IReadOnlyList<Models.AdminUser> Users,
    IReadOnlyList<Models.CustomerUser> CustomerUsers,
    IReadOnlyList<Models.PlaceCategory> Categories,
    IReadOnlyList<Models.Place> Places,
    IReadOnlyList<Models.Translation> Translations,
    IReadOnlyList<Models.AudioGuide> AudioGuides,
    IReadOnlyList<Models.MediaAsset> MediaAssets,
    IReadOnlyList<Models.FoodItem> FoodItems,
    IReadOnlyList<Models.QRCodeRecord> QrCodes,
    IReadOnlyList<Models.TourRoute> Routes,
    IReadOnlyList<Models.Promotion> Promotions,
    IReadOnlyList<Models.Review> Reviews,
    IReadOnlyList<Models.ViewLog> ViewLogs,
    IReadOnlyList<Models.AudioListenLog> AudioListenLogs,
    IReadOnlyList<Models.AuditLog> AuditLogs,
    Models.SystemSetting Settings);

public sealed record StoredFileResponse(
    string Url,
    string FileName,
    string ContentType,
    long Size);
