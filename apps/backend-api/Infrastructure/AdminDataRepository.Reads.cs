using Microsoft.Data.SqlClient;
using VinhKhanh.BackendApi.Models;
using VinhKhanh.Core.Pois;

namespace VinhKhanh.BackendApi.Infrastructure;

public sealed partial class AdminDataRepository
{
    private IReadOnlyList<AdminUser> GetUsers(SqlConnection connection, SqlTransaction? transaction)
    {
        const string sql = """
            SELECT Id, Name, Email, Phone, Role, [Password], [Status], CreatedAt, LastLoginAt, AvatarColor, ManagedPoiId,
                   ApprovalStatus, RejectionReason, RegistrationSubmittedAt, RegistrationReviewedAt
            FROM dbo.AdminUsers
            ORDER BY CreatedAt DESC, Id DESC;
            """;

        using var command = CreateCommand(connection, transaction, sql);
        using var reader = command.ExecuteReader();

        var items = new List<AdminUser>();
        while (reader.Read())
        {
            items.Add(MapAdminUser(reader));
        }

        return items;
    }

    private IReadOnlyList<PoiCategory> GetCategories(SqlConnection connection, SqlTransaction? transaction)
    {
        const string sql = """
            SELECT Id, Name, Slug, Icon, Color
            FROM dbo.Categories
            ORDER BY Name, Id;
            """;

        using var command = CreateCommand(connection, transaction, sql);
        using var reader = command.ExecuteReader();

        var items = new List<PoiCategory>();
        while (reader.Read())
        {
            items.Add(new PoiCategory
            {
                Id = ReadString(reader, "Id"),
                Name = ReadString(reader, "Name"),
                Slug = ReadString(reader, "Slug"),
                Icon = ReadString(reader, "Icon"),
                Color = ReadString(reader, "Color")
            });
        }

        return items;
    }

    private IReadOnlyList<Poi> GetPois(SqlConnection connection, SqlTransaction? transaction)
    {
        const string poisSql = """
            SELECT
                Id,
                Slug,
                Title,
                ShortDescription,
                [Description],
                AudioScript,
                SourceLanguageCode,
                AddressLine,
                Latitude,
                Longitude,
                CategoryId,
                [Status],
                IsFeatured,
                IsActive,
                LockedBySuperAdmin,
                District,
                Ward,
                PriceRange,
                TriggerRadius,
                Priority,
                PlaceTier,
                OwnerUserId,
                ApprovedAt,
                RejectionReason,
                RejectedAt,
                UpdatedBy,
                CreatedAt,
                UpdatedAt
            FROM dbo.Pois
            ORDER BY UpdatedAt DESC, CreatedAt DESC, Id DESC;
            """;
        const string tagsSql = """
            SELECT PoiId, TagValue
            FROM dbo.PoiTags
            ORDER BY PoiId, TagValue;
            """;

        var tagMap = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        using (var tagsCommand = CreateCommand(connection, transaction, tagsSql))
        using (var tagsReader = tagsCommand.ExecuteReader())
        {
            while (tagsReader.Read())
            {
                var poiId = ReadString(tagsReader, "PoiId");
                if (!tagMap.TryGetValue(poiId, out var items))
                {
                    items = [];
                    tagMap[poiId] = items;
                }

                items.Add(ReadString(tagsReader, "TagValue"));
            }
        }

        var pois = new List<Poi>();
        using (var poisCommand = CreateCommand(connection, transaction, poisSql))
        using (var poisReader = poisCommand.ExecuteReader())
        {
            while (poisReader.Read())
            {
                var poiId = ReadString(poisReader, "Id");
                pois.Add(NormalizePoiForResponse(new Poi
                {
                    Id = poiId,
                    Slug = ReadString(poisReader, "Slug"),
                    Title = ReadString(poisReader, "Title"),
                    ShortDescription = ReadString(poisReader, "ShortDescription"),
                    Description = ReadString(poisReader, "Description"),
                    AudioScript = ReadString(poisReader, "AudioScript"),
                    SourceLanguageCode = PremiumAccessCatalog.NormalizeLanguageCode(ReadString(poisReader, "SourceLanguageCode")),
                    Address = ReadString(poisReader, "AddressLine"),
                    Lat = ReadDouble(poisReader, "Latitude"),
                    Lng = ReadDouble(poisReader, "Longitude"),
                    CategoryId = ReadString(poisReader, "CategoryId"),
                    Status = ReadString(poisReader, "Status"),
                    Featured = ReadBool(poisReader, "IsFeatured"),
                    IsActive = ReadBool(poisReader, "IsActive"),
                    LockedBySuperAdmin = ReadBool(poisReader, "LockedBySuperAdmin"),
                    District = ReadString(poisReader, "District"),
                    Ward = ReadString(poisReader, "Ward"),
                    PriceRange = ReadString(poisReader, "PriceRange"),
                    TriggerRadius = ReadInt(poisReader, "TriggerRadius"),
                    Priority = ReadInt(poisReader, "Priority"),
                    PlaceTier = PoiPlaceTierCatalog.FromInt(ReadInt(poisReader, "PlaceTier")),
                    Tags = tagMap.GetValueOrDefault(poiId, []),
                    OwnerUserId = ReadNullableString(poisReader, "OwnerUserId"),
                    ApprovedAt = ReadNullableDateTimeOffset(poisReader, "ApprovedAt"),
                    RejectionReason = ReadNullableString(poisReader, "RejectionReason"),
                    RejectedAt = ReadNullableDateTimeOffset(poisReader, "RejectedAt"),
                    UpdatedBy = ReadString(poisReader, "UpdatedBy"),
                    CreatedAt = ReadDateTimeOffset(poisReader, "CreatedAt"),
                    UpdatedAt = ReadDateTimeOffset(poisReader, "UpdatedAt")
                }));
            }
        }

        return pois;
    }

    private IReadOnlyList<Translation> GetTranslations(SqlConnection connection, SqlTransaction? transaction)
    {
        const string sql = """
            SELECT Id, EntityType, EntityId, LanguageCode, Title, ShortText, FullText, SeoTitle, SeoDescription,
                   SourceLanguageCode, SourceHash, SourceUpdatedAt, UpdatedBy, UpdatedAt
            FROM dbo.PoiTranslations
            ORDER BY UpdatedAt DESC, Id DESC;
            """;

        using var command = CreateCommand(connection, transaction, sql);
        using var reader = command.ExecuteReader();

        var items = new List<Translation>();
        while (reader.Read())
        {
            items.Add(MapTranslation(reader));
        }

        return items;
    }

    private IReadOnlyList<AudioGuide> GetAudioGuides(SqlConnection connection, SqlTransaction? transaction)
    {
        const string sql = """
            SELECT
                Id,
                EntityType,
                EntityId,
                LanguageCode,
                TranscriptText,
                AudioUrl,
                AudioFilePath,
                AudioFileName,
                VoiceType,
                SourceType,
                Provider,
                VoiceId,
                ModelId,
                OutputFormat,
                DurationInSeconds,
                FileSizeBytes,
                TextHash,
                ContentVersion,
                GeneratedAt,
                GenerationStatus,
                ErrorMessage,
                IsOutdated,
                [Status],
                UpdatedBy,
                UpdatedAt
            FROM dbo.AudioGuides
            ORDER BY UpdatedAt DESC, Id DESC;
            """;

        using var command = CreateCommand(connection, transaction, sql);
        using var reader = command.ExecuteReader();

        var items = new List<AudioGuide>();
        while (reader.Read())
        {
            items.Add(MapAudioGuide(reader));
        }

        return items;
    }

    private IReadOnlyList<MediaAsset> GetMediaAssets(SqlConnection connection, SqlTransaction? transaction)
    {
        const string sql = """
            SELECT Id, EntityType, EntityId, MediaType, Url, AltText, CreatedAt
            FROM dbo.MediaAssets
            ORDER BY CreatedAt DESC, Id DESC;
            """;

        using var command = CreateCommand(connection, transaction, sql);
        using var reader = command.ExecuteReader();

        var items = new List<MediaAsset>();
        while (reader.Read())
        {
            items.Add(MapMediaAsset(reader));
        }

        return items;
    }

    private IReadOnlyList<FoodItem> GetFoodItems(SqlConnection connection, SqlTransaction? transaction)
    {
        const string sql = """
            SELECT Id, PoiId, Name, [Description], PriceRange, ImageUrl
            FROM dbo.FoodItems
            ORDER BY Name, Id;
            """;

        using var command = CreateCommand(connection, transaction, sql);
        using var reader = command.ExecuteReader();

        var items = new List<FoodItem>();
        while (reader.Read())
        {
            items.Add(MapFoodItem(reader));
        }

        return items;
    }

}
