namespace VinhKhanh.BackendApi.Domain.Entities;

public sealed class GuideAdminUser
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string Phone { get; set; } = string.Empty;
    public string Role { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? LastLoginAt { get; set; }
    public string AvatarColor { get; set; } = string.Empty;
    public string? ManagedPoiId { get; set; }
    public ICollection<GuidePoi> ManagedPois { get; set; } = [];
    public ICollection<GuideRefreshSession> RefreshSessions { get; set; } = [];
}

public sealed class GuideCategory
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Slug { get; set; } = string.Empty;
    public string Icon { get; set; } = string.Empty;
    public string Color { get; set; } = string.Empty;
    public ICollection<GuidePoi> Pois { get; set; } = [];
}

public sealed class GuidePoi
{
    public string Id { get; set; } = string.Empty;
    public string Slug { get; set; } = string.Empty;
    public string AddressLine { get; set; } = string.Empty;
    public decimal Latitude { get; set; }
    public decimal Longitude { get; set; }
    public string CategoryId { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public bool IsFeatured { get; set; }
    public string DefaultLanguageCode { get; set; } = "vi";
    public string District { get; set; } = string.Empty;
    public string Ward { get; set; } = string.Empty;
    public string PriceRange { get; set; } = string.Empty;
    public int AverageVisitDurationMinutes { get; set; }
    public int PopularityScore { get; set; }
    public string UpdatedBy { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public string? OwnerUserId { get; set; }
    public string? QrCode { get; set; }
    public string? OpeningHours { get; set; }
    public GuideCategory? Category { get; set; }
    public GuideAdminUser? OwnerUser { get; set; }
    public ICollection<GuidePoiTag> Tags { get; set; } = [];
    public ICollection<GuidePoiTranslation> Translations { get; set; } = [];
    public ICollection<GuidePoiAudioGuide> AudioGuides { get; set; } = [];
    public ICollection<GuideMediaAsset> MediaAssets { get; set; } = [];
    public ICollection<GuideFoodItem> FoodItems { get; set; } = [];
    public ICollection<GuideViewLog> ViewLogs { get; set; } = [];
    public ICollection<GuideAudioListenLog> AudioListenLogs { get; set; } = [];
    public ICollection<GuideRouteStop> RouteStops { get; set; } = [];
}

public sealed class GuidePoiTag
{
    public string PoiId { get; set; } = string.Empty;
    public string TagValue { get; set; } = string.Empty;
    public GuidePoi? Poi { get; set; }
}

public sealed class GuidePoiTranslation
{
    public string Id { get; set; } = string.Empty;
    public string EntityType { get; set; } = "poi";
    public string EntityId { get; set; } = string.Empty;
    public string LanguageCode { get; set; } = "vi";
    public string Title { get; set; } = string.Empty;
    public string ShortText { get; set; } = string.Empty;
    public string FullText { get; set; } = string.Empty;
    public string SeoTitle { get; set; } = string.Empty;
    public string SeoDescription { get; set; } = string.Empty;
    public bool IsPremium { get; set; }
    public string UpdatedBy { get; set; } = string.Empty;
    public DateTimeOffset UpdatedAt { get; set; }
    public GuidePoi? Poi { get; set; }
}

public sealed class GuidePoiAudioGuide
{
    public string Id { get; set; } = string.Empty;
    public string EntityType { get; set; } = "poi";
    public string EntityId { get; set; } = string.Empty;
    public string LanguageCode { get; set; } = "vi";
    public string AudioUrl { get; set; } = string.Empty;
    public string VoiceType { get; set; } = "standard";
    public string SourceType { get; set; } = "tts";
    public string Status { get; set; } = "ready";
    public string UpdatedBy { get; set; } = string.Empty;
    public DateTimeOffset UpdatedAt { get; set; }
    public GuidePoi? Poi { get; set; }
}

public sealed class GuideMediaAsset
{
    public string Id { get; set; } = string.Empty;
    public string EntityType { get; set; } = "poi";
    public string EntityId { get; set; } = string.Empty;
    public string MediaType { get; set; } = "image";
    public string Url { get; set; } = string.Empty;
    public string AltText { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
    public GuidePoi? Poi { get; set; }
}

public sealed class GuideFoodItem
{
    public string Id { get; set; } = string.Empty;
    public string PoiId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string PriceRange { get; set; } = string.Empty;
    public string ImageUrl { get; set; } = string.Empty;
    public string SpicyLevel { get; set; } = "mild";
    public GuidePoi? Poi { get; set; }
}

public sealed class GuideTourRoute
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public int DurationMinutes { get; set; }
    public string Difficulty { get; set; } = "easy";
    public bool IsFeatured { get; set; }
    public ICollection<GuideRouteStop> Stops { get; set; } = [];
}

public sealed class GuideRouteStop
{
    public string RouteId { get; set; } = string.Empty;
    public int StopOrder { get; set; }
    public string PoiId { get; set; } = string.Empty;
    public GuideTourRoute? Route { get; set; }
    public GuidePoi? Poi { get; set; }
}

public sealed class GuideViewLog
{
    public string Id { get; set; } = string.Empty;
    public string PoiId { get; set; } = string.Empty;
    public string LanguageCode { get; set; } = "vi";
    public string DeviceType { get; set; } = "android";
    public DateTimeOffset ViewedAt { get; set; }
    public GuidePoi? Poi { get; set; }
}

public sealed class GuideAudioListenLog
{
    public string Id { get; set; } = string.Empty;
    public string PoiId { get; set; } = string.Empty;
    public string LanguageCode { get; set; } = "vi";
    public DateTimeOffset ListenedAt { get; set; }
    public int DurationInSeconds { get; set; }
    public GuidePoi? Poi { get; set; }
}

public sealed class GuideRefreshSession
{
    public string RefreshToken { get; set; } = string.Empty;
    public string UserId { get; set; } = string.Empty;
    public DateTimeOffset ExpiresAt { get; set; }
    public GuideAdminUser? User { get; set; }
}

public sealed class GuideSystemSetting
{
    public int Id { get; set; }
    public string AppName { get; set; } = string.Empty;
    public string SupportEmail { get; set; } = string.Empty;
    public string DefaultLanguage { get; set; } = "vi";
    public string FallbackLanguage { get; set; } = "en";
    public int PremiumUnlockPriceUsd { get; set; }
    public string MapProvider { get; set; } = "google-maps";
    public string StorageProvider { get; set; } = "local";
    public string TtsProvider { get; set; } = "native";
    public int GeofenceRadiusMeters { get; set; }
    public bool GuestReviewEnabled { get; set; }
    public int AnalyticsRetentionDays { get; set; }
    public ICollection<GuideSystemSettingLanguage> Languages { get; set; } = [];
}

public sealed class GuideSystemSettingLanguage
{
    public int SettingId { get; set; }
    public string LanguageType { get; set; } = "free";
    public string LanguageCode { get; set; } = "vi";
    public GuideSystemSetting? Setting { get; set; }
}
