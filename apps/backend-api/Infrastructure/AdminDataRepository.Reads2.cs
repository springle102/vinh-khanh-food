using Microsoft.Data.SqlClient;
using VinhKhanh.BackendApi.Models;

namespace VinhKhanh.BackendApi.Infrastructure;

public sealed partial class AdminDataRepository
{
    private IReadOnlyList<TourRoute> GetRoutes(SqlConnection connection, SqlTransaction? transaction)
    {
        const string routesSql = """
            SELECT Id, Name, Theme, [Description], DurationMinutes, Difficulty, CoverImageUrl, IsFeatured, IsActive, IsSystemRoute, OwnerUserId, UpdatedBy, UpdatedAt
            FROM dbo.Routes
            ORDER BY IsFeatured DESC, IsActive DESC, UpdatedAt DESC, Name, Id;
            """;
        const string stopsSql = """
            SELECT RouteId, StopOrder, PoiId
            FROM dbo.RouteStops
            ORDER BY RouteId, StopOrder;
            """;

        var stopMap = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        using (var stopsCommand = CreateCommand(connection, transaction, stopsSql))
        using (var stopsReader = stopsCommand.ExecuteReader())
        {
            while (stopsReader.Read())
            {
                var routeId = ReadString(stopsReader, "RouteId");
                if (!stopMap.TryGetValue(routeId, out var items))
                {
                    items = [];
                    stopMap[routeId] = items;
                }

                items.Add(ReadString(stopsReader, "PoiId"));
            }
        }

        var routes = new List<TourRoute>();
        using (var routesCommand = CreateCommand(connection, transaction, routesSql))
        using (var routesReader = routesCommand.ExecuteReader())
        {
            while (routesReader.Read())
            {
                var routeId = ReadString(routesReader, "Id");
                routes.Add(new TourRoute
                {
                    Id = routeId,
                    Name = ReadString(routesReader, "Name"),
                    Theme = ReadString(routesReader, "Theme"),
                    Description = ReadString(routesReader, "Description"),
                    DurationMinutes = ReadInt(routesReader, "DurationMinutes"),
                    Difficulty = ReadString(routesReader, "Difficulty"),
                    CoverImageUrl = ReadString(routesReader, "CoverImageUrl"),
                    IsFeatured = ReadBool(routesReader, "IsFeatured"),
                    StopPoiIds = stopMap.GetValueOrDefault(routeId, []),
                    IsActive = ReadBool(routesReader, "IsActive"),
                    IsSystemRoute = ReadBool(routesReader, "IsSystemRoute"),
                    OwnerUserId = ReadNullableString(routesReader, "OwnerUserId"),
                    UpdatedBy = ReadString(routesReader, "UpdatedBy"),
                    UpdatedAt = ReadDateTimeOffset(routesReader, "UpdatedAt")
                });
            }
        }

        return routes;
    }

    private IReadOnlyList<Promotion> GetPromotions(SqlConnection connection, SqlTransaction? transaction)
    {
        const string sql = """
            SELECT Id, PoiId, Title, [Description], StartAt, EndAt, [Status]
            FROM dbo.Promotions
            ORDER BY StartAt DESC, Id DESC;
            """;

        using var command = CreateCommand(connection, transaction, sql);
        using var reader = command.ExecuteReader();

        var items = new List<Promotion>();
        while (reader.Read())
        {
            items.Add(MapPromotion(reader));
        }

        return items;
    }

    private IReadOnlyList<Review> GetReviews(SqlConnection connection, SqlTransaction? transaction)
    {
        const string sql = """
            SELECT Id, PoiId, UserName, Rating, CommentText, LanguageCode, CreatedAt, [Status]
            FROM dbo.Reviews
            ORDER BY CreatedAt DESC, Id DESC;
            """;

        using var command = CreateCommand(connection, transaction, sql);
        using var reader = command.ExecuteReader();

        var items = new List<Review>();
        while (reader.Read())
        {
            items.Add(MapReview(reader));
        }

        return items;
    }

    private IReadOnlyList<ViewLog> GetViewLogs(SqlConnection connection, SqlTransaction? transaction)
    {
        const string sql = """
            SELECT Id, PoiId, LanguageCode, DeviceType, ViewedAt
            FROM dbo.ViewLogs
            ORDER BY ViewedAt DESC, Id DESC;
            """;

        using var command = CreateCommand(connection, transaction, sql);
        using var reader = command.ExecuteReader();

        var items = new List<ViewLog>();
        while (reader.Read())
        {
            items.Add(new ViewLog
            {
                Id = ReadString(reader, "Id"),
                PoiId = ReadString(reader, "PoiId"),
                LanguageCode = ReadString(reader, "LanguageCode"),
                DeviceType = ReadString(reader, "DeviceType"),
                ViewedAt = ReadDateTimeOffset(reader, "ViewedAt")
            });
        }

        return items;
    }

    private IReadOnlyList<AudioListenLog> GetAudioListenLogs(SqlConnection connection, SqlTransaction? transaction)
    {
        const string sql = """
            SELECT Id, PoiId, LanguageCode, ListenedAt, DurationInSeconds
            FROM dbo.AudioListenLogs
            ORDER BY ListenedAt DESC, Id DESC;
            """;

        using var command = CreateCommand(connection, transaction, sql);
        using var reader = command.ExecuteReader();

        var items = new List<AudioListenLog>();
        while (reader.Read())
        {
            items.Add(new AudioListenLog
            {
                Id = ReadString(reader, "Id"),
                PoiId = ReadString(reader, "PoiId"),
                LanguageCode = ReadString(reader, "LanguageCode"),
                ListenedAt = ReadDateTimeOffset(reader, "ListenedAt"),
                DurationInSeconds = ReadInt(reader, "DurationInSeconds")
            });
        }

        return items;
    }

    private IReadOnlyList<AuditLog> GetAuditLogs(SqlConnection connection, SqlTransaction? transaction)
    {
        const string separatedSql = """
            SELECT Id, ActorId, ActorName, ActorRole, ActorType, [Action], [Module], TargetId, TargetSummary, BeforeSummary, AfterSummary, SourceApp, CreatedAt
            FROM dbo.AdminAuditLogs
            ORDER BY CreatedAt DESC, Id DESC;
            """;

        const string legacySql = """
            SELECT
                legacy.Id,
                COALESCE(adminUser.Id, legacy.TargetValue, legacy.ActorName, N'legacy-admin') AS ActorId,
                legacy.ActorName,
                legacy.ActorRole,
                N'ADMIN' AS ActorType,
                legacy.[Action],
                legacy.TargetValue AS TargetId,
                legacy.TargetValue AS TargetSummary,
                CAST(NULL AS NVARCHAR(MAX)) AS BeforeSummary,
                CAST(NULL AS NVARCHAR(MAX)) AS AfterSummary,
                N'ADMIN_WEB' AS SourceApp,
                legacy.CreatedAt
            FROM dbo.AuditLogs legacy
            LEFT JOIN dbo.AdminUsers adminUser
                ON adminUser.Email = legacy.TargetValue
            WHERE UPPER(COALESCE(legacy.ActorRole, N'')) IN (N'SUPER_ADMIN', N'PLACE_OWNER', N'SYSTEM')
            ORDER BY legacy.CreatedAt DESC, legacy.Id DESC;
            """;

        var useSeparatedAuditLogs = HasAdminAuditLogTable(connection, transaction);
        var sql = useSeparatedAuditLogs
            ? separatedSql
            : HasLegacyAuditLogTable(connection, transaction)
                ? legacySql
                : null;

        if (sql is null)
        {
            return [];
        }

        using var command = CreateCommand(connection, transaction, sql);
        using var reader = command.ExecuteReader();

        var items = new List<AuditLog>();
        while (reader.Read())
        {
            var action = ReadString(reader, "Action");
            items.Add(new AuditLog
            {
                Id = ReadString(reader, "Id"),
                ActorId = ReadString(reader, "ActorId"),
                ActorName = ReadString(reader, "ActorName"),
                ActorRole = ReadString(reader, "ActorRole"),
                ActorType = ReadString(reader, "ActorType"),
                Action = action,
                Module = useSeparatedAuditLogs ? ReadString(reader, "Module") : GuessLegacyAuditModule(action),
                TargetId = ReadString(reader, "TargetId"),
                TargetSummary = ReadString(reader, "TargetSummary"),
                BeforeSummary = ReadNullableString(reader, "BeforeSummary"),
                AfterSummary = ReadNullableString(reader, "AfterSummary"),
                SourceApp = ReadString(reader, "SourceApp"),
                CreatedAt = ReadDateTimeOffset(reader, "CreatedAt")
            });
        }

        return items;
    }

    private SystemSetting GetSettings(SqlConnection connection, SqlTransaction? transaction)
    {
        const string settingSql = """
            SELECT Id, AppName, SupportEmail, DefaultLanguage, FallbackLanguage, PremiumUnlockPriceUsd, MapProvider,
                   StorageProvider, TtsProvider, GeofenceRadiusMeters, GuestReviewEnabled, AnalyticsRetentionDays
            FROM dbo.SystemSettings
            WHERE Id = 1;
            """;
        const string languagesSql = """
            SELECT LanguageType, LanguageCode
            FROM dbo.SystemSettingLanguages
            WHERE SettingId = 1
            ORDER BY LanguageType, LanguageCode;
            """;

        var setting = new SystemSetting();
        using (var settingCommand = CreateCommand(connection, transaction, settingSql))
        using (var settingReader = settingCommand.ExecuteReader())
        {
            if (settingReader.Read())
            {
                setting = new SystemSetting
                {
                    AppName = ReadString(settingReader, "AppName"),
                    SupportEmail = ReadString(settingReader, "SupportEmail"),
                    DefaultLanguage = ReadString(settingReader, "DefaultLanguage"),
                    FallbackLanguage = ReadString(settingReader, "FallbackLanguage"),
                    PremiumUnlockPriceUsd = ReadNullableInt(settingReader, "PremiumUnlockPriceUsd")
                        ?? PremiumAccessCatalog.DefaultPremiumPriceUsd,
                    MapProvider = ReadString(settingReader, "MapProvider"),
                    StorageProvider = ReadString(settingReader, "StorageProvider"),
                    TtsProvider = ReadString(settingReader, "TtsProvider"),
                    GeofenceRadiusMeters = ReadInt(settingReader, "GeofenceRadiusMeters"),
                    GuestReviewEnabled = ReadBool(settingReader, "GuestReviewEnabled"),
                    AnalyticsRetentionDays = ReadInt(settingReader, "AnalyticsRetentionDays")
                };
            }
        }

        using (var languagesCommand = CreateCommand(connection, transaction, languagesSql))
        using (var languagesReader = languagesCommand.ExecuteReader())
        {
            while (languagesReader.Read())
            {
                var type = ReadString(languagesReader, "LanguageType");
                var languageCode = PremiumAccessCatalog.NormalizeLanguageCode(ReadString(languagesReader, "LanguageCode"));

                if (string.Equals(type, "free", StringComparison.OrdinalIgnoreCase))
                {
                    setting.FreeLanguages.Add(languageCode);
                }
                else if (string.Equals(type, "premium", StringComparison.OrdinalIgnoreCase))
                {
                    setting.PremiumLanguages.Add(languageCode);
                }
            }
        }

        return NormalizeSystemSetting(setting, logWarnings: true);
    }

    private AdminUser? GetUserByCredentials(SqlConnection connection, SqlTransaction? transaction, string email, string password)
    {
        const string sql = """
            SELECT TOP 1 Id, Name, Email, Phone, Role, [Password], [Status], CreatedAt, LastLoginAt, AvatarColor, ManagedPoiId
            FROM dbo.AdminUsers
            WHERE LOWER(Email) = LOWER(?) AND [Password] = ? AND [Status] = ?
            ORDER BY CreatedAt DESC;
            """;

        using var command = CreateCommand(connection, transaction, sql, email, password, "active");
        using var reader = command.ExecuteReader();
        return reader.Read() ? MapAdminUser(reader) : null;
    }

    private AdminUser? GetUserById(SqlConnection connection, SqlTransaction? transaction, string id)
    {
        const string sql = """
            SELECT TOP 1 Id, Name, Email, Phone, Role, [Password], [Status], CreatedAt, LastLoginAt, AvatarColor, ManagedPoiId
            FROM dbo.AdminUsers
            WHERE Id = ?;
            """;

        using var command = CreateCommand(connection, transaction, sql, id);
        using var reader = command.ExecuteReader();
        return reader.Read() ? MapAdminUser(reader) : null;
    }

    private AdminRequestContext? GetAdminRequestContext(
        SqlConnection connection,
        SqlTransaction? transaction,
        string accessToken,
        DateTimeOffset now)
    {
        const string sql = """
            SELECT TOP 1
                userAccount.Id,
                userAccount.Name,
                userAccount.Email,
                userAccount.Role,
                userAccount.[Status],
                userAccount.ManagedPoiId
            FROM dbo.RefreshSessions sessionToken
            INNER JOIN dbo.AdminUsers userAccount
                ON userAccount.Id = sessionToken.UserId
            WHERE sessionToken.AccessToken = ?
              AND sessionToken.AccessTokenExpiresAt > ?
              AND sessionToken.ExpiresAt > ?
              AND userAccount.[Status] = ?;
            """;

        using var command = CreateCommand(connection, transaction, sql, accessToken, now, now, "active");
        using var reader = command.ExecuteReader();
        if (!reader.Read())
        {
            return null;
        }

        return new AdminRequestContext(
            ReadString(reader, "Id"),
            ReadString(reader, "Name"),
            ReadString(reader, "Email"),
            ReadString(reader, "Role"),
            ReadString(reader, "Status"),
            ReadNullableString(reader, "ManagedPoiId"));
    }

    public CustomerUser? GetCustomerUserById(string id)
    {
        using var connection = OpenConnection();
        return GetCustomerUserById(connection, null, id);
    }

    private CustomerUser? GetCustomerUserByCredentials(
        SqlConnection connection,
        SqlTransaction? transaction,
        string identifier,
        string password)
    {
        var normalizedIdentifier = identifier?.Trim() ?? string.Empty;
        var normalizedPassword = password?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(normalizedIdentifier) || string.IsNullOrWhiteSpace(normalizedPassword))
        {
            return null;
        }

        var normalizedPhone = NormalizePhoneForComparison(normalizedIdentifier);

        return GetCustomerUsers(connection, transaction)
            .OrderByDescending(item => item.LastActiveAt ?? item.CreatedAt)
            .FirstOrDefault(item =>
                string.Equals(item.Password, normalizedPassword, StringComparison.Ordinal) &&
                (
                    string.Equals(item.Email, normalizedIdentifier, StringComparison.OrdinalIgnoreCase) ||
                    (!string.IsNullOrWhiteSpace(item.Username) &&
                     string.Equals(item.Username, normalizedIdentifier, StringComparison.OrdinalIgnoreCase)) ||
                    (normalizedPhone.Length > 0 &&
                     !string.IsNullOrWhiteSpace(item.Phone) &&
                     string.Equals(NormalizePhoneForComparison(item.Phone), normalizedPhone, StringComparison.Ordinal))
                ));
    }

    private CustomerUser? GetCustomerUserById(SqlConnection connection, SqlTransaction? transaction, string id)
        => GetCustomerUsers(connection, transaction)
            .FirstOrDefault(item => string.Equals(item.Id, id, StringComparison.OrdinalIgnoreCase));

    private EndUser? GetEndUserById(SqlConnection connection, SqlTransaction? transaction, string id)
    {
        const string sql = """
            SELECT TOP 1 Id, Name, Email, Phone, [Password], Username, PreferredLanguage, Country, CreatedAt, LastActiveAt
            FROM dbo.CustomerUsers
            WHERE Id = ?;
            """;

        using var command = CreateCommand(connection, transaction, sql, id);
        using var reader = command.ExecuteReader();
        return reader.Read() ? MapEndUser(reader) : null;
    }

    private RefreshSession? GetRefreshSession(
        SqlConnection connection,
        SqlTransaction? transaction,
        string refreshToken,
        DateTimeOffset now)
    {
        const string sql = """
            SELECT TOP 1 AccessToken, RefreshToken, UserId, AccessTokenExpiresAt, ExpiresAt
            FROM dbo.RefreshSessions
            WHERE RefreshToken = ? AND ExpiresAt > ?;
            """;

        using var command = CreateCommand(connection, transaction, sql, refreshToken, now);
        using var reader = command.ExecuteReader();

        if (!reader.Read())
        {
            return null;
        }

        return new RefreshSession
        {
            AccessToken = ReadString(reader, "AccessToken"),
            RefreshToken = ReadString(reader, "RefreshToken"),
            UserId = ReadString(reader, "UserId"),
            AccessTokenExpiresAt = ReadDateTimeOffset(reader, "AccessTokenExpiresAt"),
            ExpiresAt = ReadDateTimeOffset(reader, "ExpiresAt")
        };
    }

    private Poi? GetPoiById(SqlConnection connection, SqlTransaction? transaction, string id)
    {
        return GetPois(connection, transaction).FirstOrDefault(item => item.Id == id);
    }

    private Translation? GetTranslationById(SqlConnection connection, SqlTransaction? transaction, string id)
    {
        const string sql = """
            SELECT TOP 1 Id, EntityType, EntityId, LanguageCode, Title, ShortText, FullText, SeoTitle, SeoDescription, IsPremium, UpdatedBy, UpdatedAt
            FROM dbo.PoiTranslations
            WHERE Id = ?;
            """;

        using var command = CreateCommand(connection, transaction, sql, id);
        using var reader = command.ExecuteReader();
        return reader.Read() ? MapTranslation(reader) : null;
    }

    private Translation? GetTranslationByKey(
        SqlConnection connection,
        SqlTransaction? transaction,
        string entityType,
        string entityId,
        string languageCode)
    {
        const string sql = """
            SELECT TOP 1 Id, EntityType, EntityId, LanguageCode, Title, ShortText, FullText, SeoTitle, SeoDescription, IsPremium, UpdatedBy, UpdatedAt
            FROM dbo.PoiTranslations
            WHERE EntityType = ? AND EntityId = ? AND LanguageCode = ?;
            """;

        using var command = CreateCommand(connection, transaction, sql, entityType, entityId, languageCode);
        using var reader = command.ExecuteReader();
        return reader.Read() ? MapTranslation(reader) : null;
    }

    private AudioGuide? GetAudioGuideById(SqlConnection connection, SqlTransaction? transaction, string id)
    {
        const string sql = """
            SELECT TOP 1 Id, EntityType, EntityId, LanguageCode, AudioUrl, VoiceType, SourceType, [Status], UpdatedBy, UpdatedAt
            FROM dbo.AudioGuides
            WHERE Id = ?;
            """;

        using var command = CreateCommand(connection, transaction, sql, id);
        using var reader = command.ExecuteReader();
        return reader.Read() ? MapAudioGuide(reader) : null;
    }

    private AudioGuide? GetAudioGuideByKey(
        SqlConnection connection,
        SqlTransaction? transaction,
        string entityType,
        string entityId,
        string languageCode)
    {
        const string sql = """
            SELECT TOP 1 Id, EntityType, EntityId, LanguageCode, AudioUrl, VoiceType, SourceType, [Status], UpdatedBy, UpdatedAt
            FROM dbo.AudioGuides
            WHERE EntityType = ? AND EntityId = ? AND LanguageCode = ?;
            """;

        using var command = CreateCommand(connection, transaction, sql, entityType, entityId, languageCode);
        using var reader = command.ExecuteReader();
        return reader.Read() ? MapAudioGuide(reader) : null;
    }
}
