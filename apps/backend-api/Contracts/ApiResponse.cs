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

public sealed record LoginRequest(string Email, string Password, string? Portal);

public sealed record LoginAccountOptionResponse(
    string UserId,
    string Name,
    string Email,
    string Password,
    string Role,
    string Status,
    string? ManagedPoiId);

public sealed record RefreshTokenRequest(string RefreshToken);

public sealed record LogoutRequest(string RefreshToken);

public sealed record PoiUpsertRequest(
    string Slug,
    string Address,
    double Lat,
    double Lng,
    string CategoryId,
    string Status,
    string District,
    string Ward,
    string PriceRange,
    List<string> Tags,
    string? OwnerUserId,
    string UpdatedBy,
    string ActorRole,
    string ActorUserId,
    string? RequestedId);

public sealed record AdminUserUpsertRequest(
    string Name,
    string Email,
    string Phone,
    string Role,
    string Status,
    string AvatarColor,
    string? Password,
    string? ManagedPoiId,
    string ActorName,
    string ActorRole);

public sealed record AdminUserStatusUpdateRequest(
    string Status);

public sealed record PlaceOwnerRegistrationRequest(
    string Name,
    string Email,
    string Password,
    string ConfirmPassword,
    string Phone);

public sealed record PlaceOwnerRegistrationAccessRequest(
    string Email,
    string Password);

public sealed record PlaceOwnerRegistrationResubmitRequest(
    string Name,
    string Email,
    string Password,
    string ConfirmPassword,
    string Phone,
    string CurrentPassword);

public sealed record PlaceOwnerRegistrationDecisionRequest(
    string? Reason);

public sealed record PoiDecisionRequest(
    string? Reason);

public sealed record PoiActiveToggleRequest(
    bool IsActive);

public sealed record PlaceOwnerRegistrationResponse(
    string Id,
    string Name,
    string Email,
    string Phone,
    string Status,
    string ApprovalStatus,
    string? RejectionReason,
    DateTimeOffset CreatedAt,
    DateTimeOffset? RegistrationSubmittedAt,
    DateTimeOffset? RegistrationReviewedAt);

public sealed record CustomerProfileUpdateRequest(
    string Name,
    string Username,
    string Email,
    string Phone);

public sealed record CustomerRegistrationRequest(
    string Name,
    string Username,
    string Email,
    string Phone,
    string Password,
    string? PreferredLanguage,
    string? Country);

public sealed record CustomerLoginRequest(
    string Identifier,
    string Password);

public sealed record PremiumPurchaseRequest(
    string PaymentProvider,
    string PaymentMethod,
    string ClientRequestId,
    int? ExpectedPriceUsd,
    string? CardholderName,
    string? CardNumber,
    string? ExpiryMonth,
    string? ExpiryYear,
    string? Cvv,
    string? WalletProvider,
    string? WalletAccount,
    string? WalletPin);

public sealed record GeocodingLocationResponse(
    string Address,
    string District,
    string Ward,
    double Lat,
    double Lng);

public sealed record TextTranslationRequest(
    string TargetLanguageCode,
    string? SourceLanguageCode,
    IReadOnlyList<string> Texts);

public sealed record AppUsageEventCreateRequest(
    string EventType,
    string? PoiId,
    string? LanguageCode,
    string? Platform,
    string? SessionId,
    string? Source,
    string? Metadata,
    int? DurationInSeconds,
    DateTimeOffset? OccurredAt);

public sealed record TextTranslationResponse(
    string TargetLanguageCode,
    string? SourceLanguageCode,
    IReadOnlyList<string> Texts,
    string Provider);

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
    string PoiId,
    string Name,
    string Description,
    string PriceRange,
    string ImageUrl,
    string SpicyLevel);

public sealed record TourRouteUpsertRequest(
    string Name,
    string? Theme,
    string Description,
    int? DurationMinutes,
    string? Difficulty,
    string? CoverImageUrl,
    bool? IsFeatured,
    List<string> StopPoiIds,
    bool? IsActive,
    string ActorName,
    string ActorRole,
    string ActorUserId);

public sealed record PromotionUpsertRequest(
    string PoiId,
    string Title,
    string Description,
    DateTimeOffset StartAt,
    DateTimeOffset EndAt,
    string Status,
    string ActorName,
    string ActorRole);

public sealed record ReviewCreateRequest(
    string PoiId,
    string UserName,
    int Rating,
    string Comment,
    string LanguageCode);

public sealed record ReviewStatusRequest(
    string Status,
    string ActorName,
    string ActorRole);

public sealed record SystemSettingUpsertRequest(
    string AppName,
    string SupportEmail,
    string DefaultLanguage,
    string FallbackLanguage,
    List<string> SupportedLanguages,
    List<string> FreeLanguages,
    List<string> PremiumLanguages,
    int PremiumUnlockPriceUsd,
    string MapProvider,
    string StorageProvider,
    string TtsProvider,
    int GeofenceRadiusMeters,
    bool GuestReviewEnabled,
    int AnalyticsRetentionDays,
    string ActorName,
    string ActorRole);

public sealed record PremiumPurchaseResponse(
    Models.CustomerUser Customer,
    int ChargedAmountUsd,
    string CurrencyCode,
    string PaymentProvider,
    string PaymentMethod,
    string TransactionId,
    DateTimeOffset PurchasedAt);

public sealed record DashboardSummaryResponse(
    int TotalViews,
    int TotalListens,
    int TotalQrScans,
    int PublishedPois,
    int MissingReadyAudio,
    int PendingReviews);

public sealed record DataSyncState(
    string Version,
    DateTimeOffset GeneratedAt,
    DateTimeOffset LastChangedAt);

public sealed record AdminBootstrapResponse(
    IReadOnlyList<Models.AdminUser> Users,
    IReadOnlyList<Models.CustomerUser> CustomerUsers,
    IReadOnlyList<Models.PoiCategory> Categories,
    IReadOnlyList<Models.Poi> Pois,
    IReadOnlyList<Models.Translation> Translations,
    IReadOnlyList<Models.AudioGuide> AudioGuides,
    IReadOnlyList<Models.MediaAsset> MediaAssets,
    IReadOnlyList<Models.FoodItem> FoodItems,
    IReadOnlyList<Models.TourRoute> Routes,
    IReadOnlyList<Models.Promotion> Promotions,
    IReadOnlyList<Models.Review> Reviews,
    IReadOnlyList<Models.AppUsageEvent> UsageEvents,
    IReadOnlyList<Models.ViewLog> ViewLogs,
    IReadOnlyList<Models.AudioListenLog> AudioListenLogs,
    IReadOnlyList<Models.AuditLog> AuditLogs,
    Models.SystemSetting Settings,
    DataSyncState? SyncState = null);

public sealed record PoiDetailResponse(
    Models.Poi Poi,
    IReadOnlyList<Models.Translation> Translations,
    IReadOnlyList<Models.AudioGuide> AudioGuides,
    IReadOnlyList<Models.FoodItem> FoodItems,
    IReadOnlyList<Models.Translation> FoodItemTranslations,
    IReadOnlyList<Models.Promotion> Promotions,
    IReadOnlyList<Models.Translation> PromotionTranslations,
    IReadOnlyList<Models.MediaAsset> MediaAssets);

public sealed record PoiNarrationResponse(
    string PoiId,
    string RequestedLanguageCode,
    string? SourceLanguageCode,
    string EffectiveLanguageCode,
    string DisplayTitle,
    string DisplayText,
    string TtsInputText,
    string SourceText,
    string? TranslatedText,
    string TranslationStatus,
    string? FallbackMessage,
    Models.AudioGuide? AudioGuide,
    string UiPlaybackKey,
    string AudioCacheKey,
    string TtsLocale);

public sealed record StoredFileResponse(
    string Url,
    string FileName,
    string ContentType,
    long Size);
