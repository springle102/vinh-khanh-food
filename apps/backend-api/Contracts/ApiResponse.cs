using VinhKhanh.Core.Pois;

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
    int TriggerRadius,
    int Priority,
    PoiPlaceTier PlaceTier,
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

public sealed record PoiChangeRequestCreateRequest(
    PoiUpsertRequest Poi,
    string LanguageCode,
    string Title,
    string FullText,
    string? SeoTitle,
    string? SeoDescription);

public sealed record PoiChangeRequestDecisionRequest(
    string? Reason);

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
    DateTimeOffset? OccurredAt,
    string? IdempotencyKey = null);

public sealed record AppPresenceHeartbeatRequest(
    string ClientId,
    string? Platform,
    string? AppVersion);

public sealed record AppPresenceResponse(
    string ClientId,
    DateTimeOffset LastSeenAtUtc,
    bool IsOnline);

public sealed record OnlineUsersResponse(
    int OnlineUsers,
    DateTimeOffset CheckedAtUtc,
    int TimeoutSeconds);

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
    string UpdatedBy);

public sealed record AudioGuideUpsertRequest(
    string EntityType,
    string EntityId,
    string LanguageCode,
    string AudioUrl,
    string SourceType,
    string Status,
    string UpdatedBy,
    string? TranscriptText = null,
    string? AudioFilePath = null,
    string? AudioFileName = null,
    string? Provider = null,
    string? VoiceId = null,
    string? ModelId = null,
    string? OutputFormat = null,
    double? DurationInSeconds = null,
    long? FileSizeBytes = null,
    string? TextHash = null,
    string? ContentVersion = null,
    DateTimeOffset? GeneratedAt = null,
    string? GenerationStatus = null,
    string? ErrorMessage = null,
    bool? IsOutdated = null,
    string? VoiceType = null);

public sealed record PoiAudioGenerationRequest(
    string LanguageCode,
    string? VoiceId,
    string? ModelId,
    string? OutputFormat,
    bool ForceRegenerate = false);

public sealed record PoiAudioBulkGenerationRequest(
    bool ForceRegenerate = false,
    bool IncludeMissing = true,
    bool IncludeFailed = true,
    bool IncludeOutdated = true);

public sealed record PoiAudioGenerationResult(
    string PoiId,
    string RequestedLanguageCode,
    string EffectiveLanguageCode,
    bool Success,
    bool Skipped,
    bool Regenerated,
    string Message,
    string TranscriptText,
    string TextHash,
    Models.AudioGuide? AudioGuide,
    int? ProviderStatusCode = null,
    string? ProviderErrorCode = null,
    string? ProviderErrorMessage = null,
    string? ProviderResponseBody = null,
    string? AttemptedVoiceId = null,
    string? AttemptedModelId = null,
    string? OutputFormat = null);

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
    string ImageUrl);

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
    string ActorRole,
    DateTimeOffset? VisibleFrom = null);

public sealed record AppLanguageSettingsUpdateRequest(
    string DefaultLanguage,
    List<string> EnabledLanguages,
    string ActorName,
    string ActorRole);

public sealed record AppLanguageSettingResponse(
    string Code,
    string DisplayName,
    bool IsEnabled,
    bool IsDefault);

public sealed record AppLanguageSettingsResponse(
    string DefaultLanguage,
    IReadOnlyList<AppLanguageSettingResponse> Languages);

public sealed record MobileLanguageOptionResponse(
    string Code,
    string DisplayName);

public sealed record MobileLanguageSettingsResponse(
    string DefaultLanguage,
    IReadOnlyList<MobileLanguageOptionResponse> Languages)
{
    public IReadOnlyList<string> EnabledLanguages { get; init; } =
        Languages.Select(language => language.Code).ToList();
}

public sealed record MobileSystemSettingsResponse(
    MobileLanguageSettingsResponse Languages,
    MobileContactSettingsResponse Contact,
    MobileOfflinePackageSettingsResponse OfflinePackage)
{
    public IReadOnlyList<string> EnabledLanguages => Languages.EnabledLanguages;
}

public sealed record MobileContactSettingsResponse(
    string SystemName,
    string Phone,
    string Email,
    string Address,
    string ComplaintGuide,
    string SupportHours,
    DateTimeOffset UpdatedAtUtc)
{
    public string AppName => SystemName;
    public string SupportPhone => Phone;
    public string SupportEmail => Email;
    public string ContactAddress => Address;
    public string SupportInstructions => ComplaintGuide;
    public DateTimeOffset ContactUpdatedAtUtc => UpdatedAtUtc;
}

public sealed record MobileOfflinePackageSettingsResponse(
    bool DownloadsEnabled,
    int MaxPackageSizeMb,
    string Description);

public sealed record SystemContactSettingsUpdateRequest(
    string AppName,
    string SupportPhone,
    string SupportEmail,
    string ContactAddress,
    string SupportInstructions,
    string? SupportHours,
    string ActorName,
    string ActorRole);

public sealed record SystemContactSettingsResponse(
    string AppName,
    string SupportPhone,
    string SupportEmail,
    string ContactAddress,
    string SupportInstructions,
    string SupportHours,
    DateTimeOffset ContactUpdatedAtUtc);

public sealed record DashboardAudioLanguageMetricResponse(
    string LanguageCode,
    int TotalAudioPlays);

public sealed record DashboardPoiViewMetricResponse(
    string PoiId,
    string PoiTitle,
    int TotalPoiViews);

public sealed record DashboardSummaryResponse(
    int TotalPoiViews,
    int TotalAudioPlays,
    int TotalQrScans,
    int TotalOfferViews,
    int TotalPois,
    int TotalTours,
    int TotalOffers,
    int OnlineUsers,
    IReadOnlyList<DashboardAudioLanguageMetricResponse> AudioPlaysByLanguage,
    IReadOnlyList<DashboardPoiViewMetricResponse> PoiViewsByPoi);

public sealed record QrScanDiagnosticsResponse(
    int QrScanCount,
    int PublicDownloadQrScanCount,
    int ApkDownloadAccessCount,
    int DashboardQrTotal,
    DateTimeOffset? LatestTrackedQrScanAt,
    string DatabaseServer,
    string DatabaseName);

public sealed record DataSyncState(
    string Version,
    DateTimeOffset GeneratedAt,
    DateTimeOffset LastChangedAt);

public sealed record AdminBootstrapResponse(
    IReadOnlyList<Models.AdminUser> Users,
    IReadOnlyList<Models.PoiCategory> Categories,
    IReadOnlyList<Models.Poi> Pois,
    IReadOnlyList<Models.Translation> Translations,
    IReadOnlyList<Models.AudioGuide> AudioGuides,
    IReadOnlyList<Models.MediaAsset> MediaAssets,
    IReadOnlyList<Models.FoodItem> FoodItems,
    IReadOnlyList<Models.TourRoute> Routes,
    IReadOnlyList<Models.Promotion> Promotions,
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
    string TtsLocale)
{
    public string AudioUrl => AudioGuide?.AudioUrl ?? string.Empty;
    public string Language => EffectiveLanguageCode;
    public bool IsPreGenerated =>
        AudioGuide is not null &&
        (!string.IsNullOrWhiteSpace(AudioGuide.AudioUrl) || !string.IsNullOrWhiteSpace(AudioGuide.AudioFilePath)) &&
        string.Equals(AudioGuide.GenerationStatus, "success", StringComparison.OrdinalIgnoreCase) &&
        string.Equals(AudioGuide.Status, "ready", StringComparison.OrdinalIgnoreCase);
}

public sealed record StoredFileResponse(
    string Url,
    string FileName,
    string ContentType,
    long Size,
    string? BlobPath = null,
    string StorageProvider = "local");
