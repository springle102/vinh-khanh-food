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
            SELECT TOP 1 Id, PlaceId, Name, [Description], PriceRange, ImageUrl, SpicyLevel
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
            SELECT TOP 1 Id, PlaceId, Title, [Description], StartAt, EndAt, [Status]
            FROM dbo.Promotions
            WHERE Id = ?;
            """;

        using var command = CreateCommand(connection, transaction, sql, id);
        using var reader = command.ExecuteReader();
        return reader.Read() ? MapPromotion(reader) : null;
    }

    private Review? GetReviewById(SqlConnection connection, SqlTransaction? transaction, string id)
    {
        const string sql = """
            SELECT TOP 1 Id, PlaceId, UserName, Rating, CommentText, LanguageCode, CreatedAt, [Status]
            FROM dbo.Reviews
            WHERE Id = ?;
            """;

        using var command = CreateCommand(connection, transaction, sql, id);
        using var reader = command.ExecuteReader();
        return reader.Read() ? MapReview(reader) : null;
    }

    private QRCodeRecord? GetQrCodeById(SqlConnection connection, SqlTransaction? transaction, string id)
    {
        const string sql = """
            SELECT TOP 1 Id, EntityType, EntityId, QrValue, QrImageUrl, IsActive, LastScanAt
            FROM dbo.QRCodes
            WHERE Id = ?;
            """;

        using var command = CreateCommand(connection, transaction, sql, id);
        using var reader = command.ExecuteReader();
        return reader.Read() ? MapQrCode(reader) : null;
    }

    private QRCodeRecord? GetQrCodeByEntity(SqlConnection connection, SqlTransaction? transaction, string entityType, string entityId)
    {
        const string sql = """
            SELECT TOP 1 Id, EntityType, EntityId, QrValue, QrImageUrl, IsActive, LastScanAt
            FROM dbo.QRCodes
            WHERE EntityType = ? AND EntityId = ?;
            """;

        using var command = CreateCommand(connection, transaction, sql, entityType, entityId);
        using var reader = command.ExecuteReader();
        return reader.Read() ? MapQrCode(reader) : null;
    }

    private static AdminUser MapAdminUser(SqlDataReader reader)
    {
        return new AdminUser
        {
            Id = ReadString(reader, "Id"),
            Name = ReadString(reader, "Name"),
            Email = ReadString(reader, "Email"),
            Phone = ReadString(reader, "Phone"),
            Role = ReadString(reader, "Role"),
            Password = ReadString(reader, "Password"),
            Status = ReadString(reader, "Status"),
            CreatedAt = ReadDateTimeOffset(reader, "CreatedAt"),
            LastLoginAt = ReadNullableDateTimeOffset(reader, "LastLoginAt"),
            AvatarColor = ReadString(reader, "AvatarColor"),
            ManagedPlaceId = ReadNullableString(reader, "ManagedPlaceId")
        };
    }

    private static Translation MapTranslation(SqlDataReader reader)
    {
        return new Translation
        {
            Id = ReadString(reader, "Id"),
            EntityType = ReadString(reader, "EntityType"),
            EntityId = ReadString(reader, "EntityId"),
            LanguageCode = ReadString(reader, "LanguageCode"),
            Title = ReadString(reader, "Title"),
            ShortText = ReadString(reader, "ShortText"),
            FullText = ReadString(reader, "FullText"),
            SeoTitle = ReadString(reader, "SeoTitle"),
            SeoDescription = ReadString(reader, "SeoDescription"),
            IsPremium = ReadBool(reader, "IsPremium"),
            UpdatedBy = ReadString(reader, "UpdatedBy"),
            UpdatedAt = ReadDateTimeOffset(reader, "UpdatedAt")
        };
    }

    private static AudioGuide MapAudioGuide(SqlDataReader reader)
    {
        return new AudioGuide
        {
            Id = ReadString(reader, "Id"),
            EntityType = ReadString(reader, "EntityType"),
            EntityId = ReadString(reader, "EntityId"),
            LanguageCode = ReadString(reader, "LanguageCode"),
            AudioUrl = ReadString(reader, "AudioUrl"),
            VoiceType = ReadString(reader, "VoiceType"),
            SourceType = ReadString(reader, "SourceType"),
            Status = ReadString(reader, "Status"),
            UpdatedBy = ReadString(reader, "UpdatedBy"),
            UpdatedAt = ReadDateTimeOffset(reader, "UpdatedAt")
        };
    }

    private static MediaAsset MapMediaAsset(SqlDataReader reader)
    {
        return new MediaAsset
        {
            Id = ReadString(reader, "Id"),
            EntityType = ReadString(reader, "EntityType"),
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
            PlaceId = ReadString(reader, "PlaceId"),
            Name = ReadString(reader, "Name"),
            Description = ReadString(reader, "Description"),
            PriceRange = ReadString(reader, "PriceRange"),
            ImageUrl = ReadString(reader, "ImageUrl"),
            SpicyLevel = ReadString(reader, "SpicyLevel")
        };
    }

    private static Promotion MapPromotion(SqlDataReader reader)
    {
        return new Promotion
        {
            Id = ReadString(reader, "Id"),
            PlaceId = ReadString(reader, "PlaceId"),
            Title = ReadString(reader, "Title"),
            Description = ReadString(reader, "Description"),
            StartAt = ReadDateTimeOffset(reader, "StartAt"),
            EndAt = ReadDateTimeOffset(reader, "EndAt"),
            Status = ReadString(reader, "Status")
        };
    }

    private static Review MapReview(SqlDataReader reader)
    {
        return new Review
        {
            Id = ReadString(reader, "Id"),
            PlaceId = ReadString(reader, "PlaceId"),
            UserName = ReadString(reader, "UserName"),
            Rating = ReadInt(reader, "Rating"),
            Comment = ReadString(reader, "CommentText"),
            LanguageCode = ReadString(reader, "LanguageCode"),
            CreatedAt = ReadDateTimeOffset(reader, "CreatedAt"),
            Status = ReadString(reader, "Status")
        };
    }

    private static QRCodeRecord MapQrCode(SqlDataReader reader)
    {
        return new QRCodeRecord
        {
            Id = ReadString(reader, "Id"),
            EntityType = ReadString(reader, "EntityType"),
            EntityId = ReadString(reader, "EntityId"),
            QrValue = ReadString(reader, "QrValue"),
            QrImageUrl = ReadString(reader, "QrImageUrl"),
            IsActive = ReadBool(reader, "IsActive"),
            LastScanAt = ReadNullableDateTimeOffset(reader, "LastScanAt")
        };
    }
}
