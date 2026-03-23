using Microsoft.Data.SqlClient;
using VinhKhanh.BackendApi.Models;

namespace VinhKhanh.BackendApi.Infrastructure;

public sealed partial class AdminDataRepository
{
    private IReadOnlyList<AdminUser> GetUsers(SqlConnection connection, SqlTransaction? transaction)
    {
        const string sql = """
            SELECT Id, Name, Email, Phone, Role, [Password], [Status], CreatedAt, LastLoginAt, AvatarColor, ManagedPlaceId
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
            SELECT Id, Name, Email, Phone, [Status], PreferredLanguage, IsPremium, TotalScans, CreatedAt, LastActiveAt
            FROM dbo.CustomerUsers
            ORDER BY CreatedAt DESC, Id DESC;
            """;
        const string favoritesSql = """
            SELECT CustomerUserId, PlaceId
            FROM dbo.CustomerFavoritePlaces
            ORDER BY CustomerUserId, PlaceId;
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

                items.Add(ReadString(favoritesReader, "PlaceId"));
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
                    Status = ReadString(usersReader, "Status"),
                    PreferredLanguage = ReadString(usersReader, "PreferredLanguage"),
                    IsPremium = ReadBool(usersReader, "IsPremium"),
                    TotalScans = ReadInt(usersReader, "TotalScans"),
                    FavoritePlaceIds = favoriteMap.GetValueOrDefault(customerId, []),
                    CreatedAt = ReadDateTimeOffset(usersReader, "CreatedAt"),
                    LastActiveAt = ReadNullableDateTimeOffset(usersReader, "LastActiveAt")
                });
            }
        }

        return customers;
    }

    private IReadOnlyList<PlaceCategory> GetCategories(SqlConnection connection, SqlTransaction? transaction)
    {
        const string sql = """
            SELECT Id, Name, Slug, Icon, Color
            FROM dbo.Categories
            ORDER BY Name, Id;
            """;

        using var command = CreateCommand(connection, transaction, sql);
        using var reader = command.ExecuteReader();

        var items = new List<PlaceCategory>();
        while (reader.Read())
        {
            items.Add(new PlaceCategory
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

    private IReadOnlyList<Place> GetPlaces(SqlConnection connection, SqlTransaction? transaction)
    {
        const string placesSql = """
            SELECT
                Id,
                Slug,
                AddressLine,
                Latitude,
                Longitude,
                CategoryId,
                [Status],
                IsFeatured,
                DefaultLanguageCode,
                District,
                Ward,
                PriceRange,
                AverageVisitDurationMinutes,
                PopularityScore,
                OwnerUserId,
                UpdatedBy,
                CreatedAt,
                UpdatedAt
            FROM dbo.Places
            ORDER BY UpdatedAt DESC, CreatedAt DESC, Id DESC;
            """;
        const string tagsSql = """
            SELECT PlaceId, TagValue
            FROM dbo.PlaceTags
            ORDER BY PlaceId, TagValue;
            """;

        var tagMap = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        using (var tagsCommand = CreateCommand(connection, transaction, tagsSql))
        using (var tagsReader = tagsCommand.ExecuteReader())
        {
            while (tagsReader.Read())
            {
                var placeId = ReadString(tagsReader, "PlaceId");
                if (!tagMap.TryGetValue(placeId, out var items))
                {
                    items = [];
                    tagMap[placeId] = items;
                }

                items.Add(ReadString(tagsReader, "TagValue"));
            }
        }

        var places = new List<Place>();
        using (var placesCommand = CreateCommand(connection, transaction, placesSql))
        using (var placesReader = placesCommand.ExecuteReader())
        {
            while (placesReader.Read())
            {
                var placeId = ReadString(placesReader, "Id");
                places.Add(new Place
                {
                    Id = placeId,
                    Slug = ReadString(placesReader, "Slug"),
                    Address = ReadString(placesReader, "AddressLine"),
                    Lat = ReadDouble(placesReader, "Latitude"),
                    Lng = ReadDouble(placesReader, "Longitude"),
                    CategoryId = ReadString(placesReader, "CategoryId"),
                    Status = ReadString(placesReader, "Status"),
                    Featured = ReadBool(placesReader, "IsFeatured"),
                    DefaultLanguageCode = ReadString(placesReader, "DefaultLanguageCode"),
                    District = ReadString(placesReader, "District"),
                    Ward = ReadString(placesReader, "Ward"),
                    PriceRange = ReadString(placesReader, "PriceRange"),
                    AverageVisitDuration = ReadInt(placesReader, "AverageVisitDurationMinutes"),
                    PopularityScore = ReadInt(placesReader, "PopularityScore"),
                    Tags = tagMap.GetValueOrDefault(placeId, []),
                    OwnerUserId = ReadNullableString(placesReader, "OwnerUserId"),
                    UpdatedBy = ReadString(placesReader, "UpdatedBy"),
                    CreatedAt = ReadDateTimeOffset(placesReader, "CreatedAt"),
                    UpdatedAt = ReadDateTimeOffset(placesReader, "UpdatedAt")
                });
            }
        }

        return places;
    }

    private IReadOnlyList<Translation> GetTranslations(SqlConnection connection, SqlTransaction? transaction)
    {
        const string sql = """
            SELECT Id, EntityType, EntityId, LanguageCode, Title, ShortText, FullText, SeoTitle, SeoDescription, IsPremium, UpdatedBy, UpdatedAt
            FROM dbo.PlaceTranslations
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
            SELECT Id, PlaceId, Name, [Description], PriceRange, ImageUrl, SpicyLevel
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

    private IReadOnlyList<QRCodeRecord> GetQrCodes(SqlConnection connection, SqlTransaction? transaction)
    {
        const string sql = """
            SELECT Id, EntityType, EntityId, QrValue, QrImageUrl, IsActive, LastScanAt
            FROM dbo.QRCodes
            ORDER BY EntityId, Id;
            """;

        using var command = CreateCommand(connection, transaction, sql);
        using var reader = command.ExecuteReader();

        var items = new List<QRCodeRecord>();
        while (reader.Read())
        {
            items.Add(MapQrCode(reader));
        }

        return items;
    }
}
