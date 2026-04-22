using VinhKhanh.Core.Pois;

namespace VinhKhanh.BackendApi.Models;

public sealed class AdminUser
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
    public string ApprovalStatus { get; set; } = string.Empty;
    public string? RejectionReason { get; set; }
    public DateTimeOffset? RegistrationSubmittedAt { get; set; }
    public DateTimeOffset? RegistrationReviewedAt { get; set; }
}

public sealed class PoiCategory
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Slug { get; set; } = string.Empty;
    public string Icon { get; set; } = string.Empty;
    public string Color { get; set; } = string.Empty;
}

public sealed class Poi
{
    public string Id { get; set; } = string.Empty;
    public string Slug { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string ShortDescription { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string AudioScript { get; set; } = string.Empty;
    public string SourceLanguageCode { get; set; } = "vi";
    public string Address { get; set; } = string.Empty;
    public double Lat { get; set; }
    public double Lng { get; set; }
    public string CategoryId { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public bool Featured { get; set; }
    public bool IsActive { get; set; }
    public bool LockedBySuperAdmin { get; set; }
    public string District { get; set; } = string.Empty;
    public string Ward { get; set; } = string.Empty;
    public string PriceRange { get; set; } = string.Empty;
    public int TriggerRadius { get; set; } = 20;
    public int Priority { get; set; }
    public PoiPlaceTier PlaceTier { get; set; } = PoiPlaceTier.Basic;
    public List<string> Tags { get; set; } = [];
    public string? OwnerUserId { get; set; }
    public string UpdatedBy { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public DateTimeOffset? ApprovedAt { get; set; }
    public string? RejectionReason { get; set; }
    public DateTimeOffset? RejectedAt { get; set; }
}

public sealed class Translation
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
    public string? SourceLanguageCode { get; set; }
    public string? SourceHash { get; set; }
    public DateTimeOffset? SourceUpdatedAt { get; set; }
    public string UpdatedBy { get; set; } = string.Empty;
    public DateTimeOffset UpdatedAt { get; set; }
}

public sealed class AudioGuide
{
    public string Id { get; set; } = string.Empty;
    public string EntityType { get; set; } = "poi";
    public string EntityId { get; set; } = string.Empty;
    public string LanguageCode { get; set; } = "vi";
    public string TranscriptText { get; set; } = string.Empty;
    public string AudioUrl { get; set; } = string.Empty;
    public string AudioFilePath { get; set; } = string.Empty;
    public string AudioFileName { get; set; } = string.Empty;
    public string VoiceType { get; set; } = "standard";
    public string SourceType { get; set; } = "generated";
    public string Provider { get; set; } = "elevenlabs";
    public string VoiceId { get; set; } = string.Empty;
    public string ModelId { get; set; } = string.Empty;
    public string OutputFormat { get; set; } = "mp3_44100_128";
    public double? DurationInSeconds { get; set; }
    public long? FileSizeBytes { get; set; }
    public string TextHash { get; set; } = string.Empty;
    public string ContentVersion { get; set; } = string.Empty;
    public DateTimeOffset? GeneratedAt { get; set; }
    public string GenerationStatus { get; set; } = "none";
    public string? ErrorMessage { get; set; }
    public bool IsOutdated { get; set; }
    public string Status { get; set; } = "missing";
    public string UpdatedBy { get; set; } = string.Empty;
    public DateTimeOffset UpdatedAt { get; set; }
}

public sealed class MediaAsset
{
    public string Id { get; set; } = string.Empty;
    public string EntityType { get; set; } = "poi";
    public string EntityId { get; set; } = string.Empty;
    public string Type { get; set; } = "image";
    public string Url { get; set; } = string.Empty;
    public string AltText { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
}

public sealed class FoodItem
{
    public string Id { get; set; } = string.Empty;
    public string PoiId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string PriceRange { get; set; } = string.Empty;
    public string ImageUrl { get; set; } = string.Empty;
}

public sealed class ViewLog
{
    public string Id { get; set; } = string.Empty;
    public string PoiId { get; set; } = string.Empty;
    public string LanguageCode { get; set; } = "vi";
    public string DeviceType { get; set; } = "web";
    public DateTimeOffset ViewedAt { get; set; }
}

public sealed class AudioListenLog
{
    public string Id { get; set; } = string.Empty;
    public string PoiId { get; set; } = string.Empty;
    public string LanguageCode { get; set; } = "vi";
    public DateTimeOffset ListenedAt { get; set; }
    public int DurationInSeconds { get; set; }
}

public sealed class AppUsageEvent
{
    public string Id { get; set; } = string.Empty;
    public string EventType { get; set; } = string.Empty;
    public string? PoiId { get; set; }
    public string LanguageCode { get; set; } = "vi";
    public string Platform { get; set; } = "android";
    public string SessionId { get; set; } = string.Empty;
    public string Source { get; set; } = "mobile-app";
    public string Metadata { get; set; } = string.Empty;
    public int? DurationInSeconds { get; set; }
    public DateTimeOffset OccurredAt { get; set; }
    public string? IdempotencyKey { get; set; }
}

public sealed class TourRoute
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Theme { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public int DurationMinutes { get; set; }
    public string Difficulty { get; set; } = "custom";
    public string CoverImageUrl { get; set; } = string.Empty;
    public bool IsFeatured { get; set; }
    public List<string> StopPoiIds { get; set; } = [];
    public bool IsActive { get; set; }
    public bool IsSystemRoute { get; set; }
    public string? OwnerUserId { get; set; }
    public string UpdatedBy { get; set; } = string.Empty;
    public DateTimeOffset UpdatedAt { get; set; }
}

public sealed class Promotion
{
    public string Id { get; set; } = string.Empty;
    public string PoiId { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public DateTimeOffset StartAt { get; set; }
    public DateTimeOffset EndAt { get; set; }
    public string Status { get; set; } = "upcoming";
}

public sealed class AuditLog
{
    public string Id { get; set; } = string.Empty;
    public string ActorId { get; set; } = string.Empty;
    public string ActorName { get; set; } = string.Empty;
    public string ActorRole { get; set; } = string.Empty;
    public string ActorType { get; set; } = string.Empty;
    public string Action { get; set; } = string.Empty;
    public string Module { get; set; } = string.Empty;
    public string TargetId { get; set; } = string.Empty;
    public string TargetSummary { get; set; } = string.Empty;
    public string? BeforeSummary { get; set; }
    public string? AfterSummary { get; set; }
    public string SourceApp { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
}

public sealed class UserActivityLog
{
    public string Id { get; set; } = string.Empty;
    public string ActorId { get; set; } = string.Empty;
    public string ActorType { get; set; } = string.Empty;
    public string EventType { get; set; } = string.Empty;
    public string Metadata { get; set; } = string.Empty;
    public string SourceApp { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
}

public sealed class RefreshSession
{
    public string UserId { get; set; } = string.Empty;
    public string AccessToken { get; set; } = string.Empty;
    public string RefreshToken { get; set; } = string.Empty;
    public DateTimeOffset AccessTokenExpiresAt { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
}

public sealed class SystemSetting
{
    public string AppName { get; set; } = string.Empty;
    public string SupportEmail { get; set; } = string.Empty;
    public string DefaultLanguage { get; set; } = "vi";
    public string FallbackLanguage { get; set; } = "en";
    public List<string> SupportedLanguages { get; set; } = [];
    public string MapProvider { get; set; } = "openstreetmap";
    public string StorageProvider { get; set; } = "cloudinary";
    public string TtsProvider { get; set; } = "elevenlabs";
    public int GeofenceRadiusMeters { get; set; }
    public int AnalyticsRetentionDays { get; set; }
}
