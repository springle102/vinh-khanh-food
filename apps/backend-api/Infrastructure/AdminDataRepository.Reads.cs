using Microsoft.Data.SqlClient;
using VinhKhanh.BackendApi.Models;

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

    private IReadOnlyList<CustomerUser> GetCustomerUsers(SqlConnection connection, SqlTransaction? transaction)
    {
        const string usersSql = """
            SELECT Id, Name, Email, Phone, [Password], PreferredLanguage, Username, Country, IsPremium, CreatedAt, LastActiveAt
            FROM dbo.CustomerUsers
            ORDER BY CreatedAt DESC, Id DESC;
            """;
        const string favoritesSql = """
            SELECT CustomerUserId, PoiId
            FROM dbo.CustomerFavoritePois
            ORDER BY CustomerUserId, PoiId;
            """;

        var favoriteMap = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        using (var favoritesCommand = CreateCommand(connection, transaction, favoritesSql))
        using (var favoritesReader = favoritesCommand.ExecuteReader())
        {
            while (favoritesReader.Read())
            {
                var customerUserId = ReadString(favoritesReader, "CustomerUserId");
                if (!favoriteMap.TryGetValue(customerUserId, out var items))
                {
                    items = [];
                    favoriteMap[customerUserId] = items;
                }

                items.Add(ReadString(favoritesReader, "PoiId"));
            }
        }

        var customers = new List<CustomerUser>();
        using (var usersCommand = CreateCommand(connection, transaction, usersSql))
        using (var usersReader = usersCommand.ExecuteReader())
        {
            while (usersReader.Read())
            {
                var customerId = ReadString(usersReader, "Id");
                customers.Add(new CustomerUser
                {
                    Id = customerId,
                    Name = ReadString(usersReader, "Name"),
                    Email = ReadString(usersReader, "Email"),
                    Phone = ReadString(usersReader, "Phone"),
                    Password = ReadString(usersReader, "Password"),
                    PreferredLanguage = ReadString(usersReader, "PreferredLanguage"),
                    Username = ReadNullableString(usersReader, "Username"),
                    Country = ReadString(usersReader, "Country"),
                    IsPremium = ReadBool(usersReader, "IsPremium"),
                    FavoritePoiIds = favoriteMap.GetValueOrDefault(customerId, []),
                    CreatedAt = ReadDateTimeOffset(usersReader, "CreatedAt"),
                    LastActiveAt = ReadNullableDateTimeOffset(usersReader, "LastActiveAt")
                });
            }
        }

        return customers;
    }

    private IReadOnlyList<EndUser> GetEndUsers(SqlConnection connection, SqlTransaction? transaction)
    {
        const string sql = """
            SELECT Id, Name, Email, Phone, [Password], Username, PreferredLanguage, Country, CreatedAt, LastActiveAt
            FROM dbo.CustomerUsers
            ORDER BY CreatedAt DESC, Id DESC;
            """;

        using var command = CreateCommand(connection, transaction, sql);
        using var reader = command.ExecuteReader();

        var items = new List<EndUser>();
        while (reader.Read())
        {
            items.Add(MapEndUser(reader));
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
            SELECT Id, EntityType, EntityId, LanguageCode, Title, ShortText, FullText, SeoTitle, SeoDescription, IsPremium,
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
            SELECT Id, EntityType, EntityId, LanguageCode, AudioUrl, VoiceType, SourceType, [Status], UpdatedBy, UpdatedAt
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
            SELECT Id, PoiId, Name, [Description], PriceRange, ImageUrl, SpicyLevel
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
