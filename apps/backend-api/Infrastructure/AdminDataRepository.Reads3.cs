using Microsoft.Data.SqlClient;
using VinhKhanh.BackendApi.Models;

namespace VinhKhanh.BackendApi.Infrastructure;

public sealed partial class AdminDataRepository
{
    private MediaAsset? GetMediaAssetById(SqlConnection connection, SqlTransaction? transaction, string id)
    {
        const string sql = """
            SELECT TOP 1 Id, EntityType, EntityId, MediaType, Url, AltText, CreatedAt
            FROM dbo.MediaAssets
            WHERE Id = ?;
            """;

        using var command = CreateCommand(connection, transaction, sql, id);
        using var reader = command.ExecuteReader();
        return reader.Read() ? MapMediaAsset(reader) : null;
    }

    private FoodItem? GetFoodItemById(SqlConnection connection, SqlTransaction? transaction, string id)
    {
        const string sql = """
            SELECT TOP 1 Id, PoiId, Name, [Description], PriceRange, ImageUrl
            FROM dbo.FoodItems
            WHERE Id = ?;
            """;

        using var command = CreateCommand(connection, transaction, sql, id);
        using var reader = command.ExecuteReader();
        return reader.Read() ? MapFoodItem(reader) : null;
    }

    private TourRoute? GetRouteById(SqlConnection connection, SqlTransaction? transaction, string id)
    {
        return GetRoutes(connection, transaction).FirstOrDefault(item => item.Id == id);
    }

    private Promotion? GetPromotionById(SqlConnection connection, SqlTransaction? transaction, string id)
    {
        const string sql = """
            SELECT TOP 1 Id, PoiId, Title, [Description], StartAt, EndAt, [Status], VisibleFrom, CreatedByUserId, OwnerUserId, IsDeleted
            FROM dbo.Promotions
            WHERE Id = ?
              AND COALESCE(IsDeleted, CAST(0 AS bit)) = CAST(0 AS bit);
            """;

        using var command = CreateCommand(connection, transaction, sql, id);
        using var reader = command.ExecuteReader();
        return reader.Read() ? MapPromotion(reader) : null;
    }

    private static AdminUser MapAdminUser(SqlDataReader reader)
    {
        return new AdminUser
        {
            Id = ReadString(reader, "Id"),
            Name = ReadString(reader, "Name"),
            Email = ReadString(reader, "Email"),
            Phone = ReadString(reader, "Phone"),
            Role = AdminRoleCatalog.NormalizeKnownRoleOrOriginal(ReadString(reader, "Role")),
            Password = ReadString(reader, "Password"),
            Status = ReadString(reader, "Status"),
            CreatedAt = ReadDateTimeOffset(reader, "CreatedAt"),
            LastLoginAt = ReadNullableDateTimeOffset(reader, "LastLoginAt"),
            AvatarColor = ReadString(reader, "AvatarColor"),
            ManagedPoiId = ReadNullableString(reader, "ManagedPoiId"),
            ApprovalStatus = AdminApprovalCatalog.NormalizeKnownOrDefault(ReadNullableString(reader, "ApprovalStatus")),
            RejectionReason = ReadNullableString(reader, "RejectionReason"),
            RegistrationSubmittedAt = ReadNullableDateTimeOffset(reader, "RegistrationSubmittedAt") ?? ReadDateTimeOffset(reader, "CreatedAt"),
            RegistrationReviewedAt = ReadNullableDateTimeOffset(reader, "RegistrationReviewedAt")
        };
    }

    private static Translation MapTranslation(SqlDataReader reader)
    {
        return new Translation
        {
            Id = ReadString(reader, "Id"),
            EntityType = NormalizeStoredEntityType(ReadString(reader, "EntityType")),
            EntityId = ReadString(reader, "EntityId"),
            LanguageCode = ReadString(reader, "LanguageCode"),
            Title = ReadString(reader, "Title"),
            ShortText = ReadString(reader, "ShortText"),
            FullText = ReadString(reader, "FullText"),
            SeoTitle = ReadString(reader, "SeoTitle"),
            SeoDescription = ReadString(reader, "SeoDescription"),
            SourceLanguageCode = ReadNullableString(reader, "SourceLanguageCode"),
            SourceHash = ReadNullableString(reader, "SourceHash"),
            SourceUpdatedAt = ReadNullableDateTimeOffset(reader, "SourceUpdatedAt"),
            UpdatedBy = ReadString(reader, "UpdatedBy"),
            UpdatedAt = ReadDateTimeOffset(reader, "UpdatedAt")
        };
    }

    private static AudioGuide MapAudioGuide(SqlDataReader reader)
    {
        var audioUrl = ReadString(reader, "AudioUrl");
        var audioFilePath = ReadString(reader, "AudioFilePath");
        var sourceType = AudioGuideCatalog.NormalizeSourceType(ReadString(reader, "SourceType"));
        var generationStatus = AudioGuideCatalog.NormalizeGenerationStatus(ReadNullableString(reader, "GenerationStatus"));
        var isOutdated = ReadNullableBool(reader, "IsOutdated") ?? false;

        var normalizedStatus = AudioGuideCatalog.ResolvePublicStatus(
            generationStatus,
            !string.IsNullOrWhiteSpace(audioUrl) || !string.IsNullOrWhiteSpace(audioFilePath),
            isOutdated);

        return new AudioGuide
        {
            Id = ReadString(reader, "Id"),
            EntityType = NormalizeStoredEntityType(ReadString(reader, "EntityType")),
            EntityId = ReadString(reader, "EntityId"),
            LanguageCode = PremiumAccessCatalog.NormalizeLanguageCode(ReadString(reader, "LanguageCode")),
            TranscriptText = ReadString(reader, "TranscriptText"),
            AudioUrl = audioUrl,
            AudioFilePath = audioFilePath,
            AudioFileName = ReadString(reader, "AudioFileName"),
            VoiceType = ReadString(reader, "VoiceType"),
            SourceType = sourceType,
            Provider = AudioGuideCatalog.NormalizeProvider(ReadNullableString(reader, "Provider")),
            VoiceId = ReadString(reader, "VoiceId"),
            ModelId = ReadString(reader, "ModelId"),
            OutputFormat = ReadString(reader, "OutputFormat"),
            DurationInSeconds = ReadNullableDouble(reader, "DurationInSeconds"),
            FileSizeBytes = ReadNullableLong(reader, "FileSizeBytes"),
            TextHash = ReadString(reader, "TextHash"),
            ContentVersion = ReadString(reader, "ContentVersion"),
            GeneratedAt = ReadNullableDateTimeOffset(reader, "GeneratedAt"),
            GenerationStatus = generationStatus,
            ErrorMessage = ReadNullableString(reader, "ErrorMessage"),
            IsOutdated = isOutdated,
            Status = normalizedStatus,
            UpdatedBy = ReadString(reader, "UpdatedBy"),
            UpdatedAt = ReadDateTimeOffset(reader, "UpdatedAt")
        };
    }

    private static MediaAsset MapMediaAsset(SqlDataReader reader)
    {
        return new MediaAsset
        {
            Id = ReadString(reader, "Id"),
            EntityType = NormalizeStoredEntityType(ReadString(reader, "EntityType")),
            EntityId = ReadString(reader, "EntityId"),
            Type = ReadString(reader, "MediaType"),
            Url = ReadString(reader, "Url"),
            AltText = ReadString(reader, "AltText"),
            CreatedAt = ReadDateTimeOffset(reader, "CreatedAt")
        };
    }

    private static FoodItem MapFoodItem(SqlDataReader reader)
    {
        return new FoodItem
        {
            Id = ReadString(reader, "Id"),
            PoiId = ReadString(reader, "PoiId"),
            Name = ReadString(reader, "Name"),
            Description = ReadString(reader, "Description"),
            PriceRange = ReadString(reader, "PriceRange"),
            ImageUrl = ReadString(reader, "ImageUrl")
        };
    }

    private static Promotion MapPromotion(SqlDataReader reader)
    {
        return new Promotion
        {
            Id = ReadString(reader, "Id"),
            PoiId = ReadString(reader, "PoiId"),
            Title = ReadString(reader, "Title"),
            Description = ReadString(reader, "Description"),
            StartAt = ReadDateTimeOffset(reader, "StartAt"),
            EndAt = ReadDateTimeOffset(reader, "EndAt"),
            Status = ReadString(reader, "Status"),
            VisibleFrom = ReadNullableDateTimeOffset(reader, "VisibleFrom"),
            CreatedByUserId = ReadString(reader, "CreatedByUserId"),
            OwnerUserId = ReadNullableString(reader, "OwnerUserId"),
            IsDeleted = ReadBool(reader, "IsDeleted")
        };
    }

    private static string NormalizeStoredEntityType(string entityType)
        => string.Equals(entityType.Trim(), "place", StringComparison.OrdinalIgnoreCase)
            ? "poi"
            : string.Equals(entityType.Trim(), "food-item", StringComparison.OrdinalIgnoreCase)
                ? "food_item"
                : entityType.Trim().ToLowerInvariant();

}
