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
}

public sealed class CustomerUser
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string Phone { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public string PreferredLanguage { get; set; } = "vi";
    public string? Username { get; set; }
    public string Country { get; set; } = string.Empty;
    public bool IsPremium { get; set; }
    public List<string> FavoritePoiIds { get; set; } = [];
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? LastActiveAt { get; set; }
}

public sealed class EndUser
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string Phone { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public string? Username { get; set; }
    public string DefaultLanguage { get; set; } = "vi";
    public string Country { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? LastActiveAt { get; set; }
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
    public string Address { get; set; } = string.Empty;
    public double Lat { get; set; }
    public double Lng { get; set; }
    public string CategoryId { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public bool Featured { get; set; }
    public string District { get; set; } = string.Empty;
    public string Ward { get; set; } = string.Empty;
    public string PriceRange { get; set; } = string.Empty;
    public int AverageVisitDuration { get; set; }
    public int PopularityScore { get; set; }
    public List<string> Tags { get; set; } = [];
    public string? OwnerUserId { get; set; }
    public string UpdatedBy { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
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
    public bool IsPremium { get; set; }
    public string UpdatedBy { get; set; } = string.Empty;
    public DateTimeOffset UpdatedAt { get; set; }
}

public sealed class AudioGuide
{
    public string Id { get; set; } = string.Empty;
    public string EntityType { get; set; } = "poi";
    public string EntityId { get; set; } = string.Empty;
    public string LanguageCode { get; set; } = "vi";
    public string AudioUrl { get; set; } = string.Empty;
    public string VoiceType { get; set; } = "standard";
    public string SourceType { get; set; } = "uploaded";
    public string Status { get; set; } = "ready";
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
    public string SpicyLevel { get; set; } = "mild";
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

public sealed class Review
{
    public string Id { get; set; } = string.Empty;
    public string PoiId { get; set; } = string.Empty;
    public string UserName { get; set; } = string.Empty;
    public int Rating { get; set; }
    public string Comment { get; set; } = string.Empty;
    public string LanguageCode { get; set; } = "vi";
    public DateTimeOffset CreatedAt { get; set; }
    public string Status { get; set; } = "pending";
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

public sealed class PremiumPurchaseTransaction
{
    public string Id { get; set; } = string.Empty;
    public string CustomerUserId { get; set; } = string.Empty;
    public int AmountUsd { get; set; }
    public string CurrencyCode { get; set; } = "USD";
    public string PaymentProvider { get; set; } = "mock";
    public string PaymentMethod { get; set; } = string.Empty;
    public string? PaymentReference { get; set; }
    public string? MaskedAccount { get; set; }
    public string IdempotencyKey { get; set; } = string.Empty;
    public string Status { get; set; } = "pending";
    public string? FailureMessage { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? ProcessedAt { get; set; }
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
    public List<string> FreeLanguages { get; set; } = [];
    public List<string> PremiumLanguages { get; set; } = [];
    public int PremiumUnlockPriceUsd { get; set; }
    public string MapProvider { get; set; } = "openstreetmap";
    public string StorageProvider { get; set; } = "cloudinary";
    public string TtsProvider { get; set; } = "elevenlabs";
    public int GeofenceRadiusMeters { get; set; }
    public bool GuestReviewEnabled { get; set; }
    public int AnalyticsRetentionDays { get; set; }
}
