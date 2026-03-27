using Microsoft.Data.SqlClient;
using System.Linq;
using VinhKhanh.BackendApi.Contracts;
using VinhKhanh.BackendApi.Models;

namespace VinhKhanh.BackendApi.Infrastructure;

public sealed partial class AdminDataRepository
{
    private const int MaxAuditLogs = 120;
    private readonly string _connectionString;
    private readonly string _seedSqlServerPath;

    public AdminDataRepository(IConfiguration configuration, IWebHostEnvironment environment)
    {
        _connectionString = ResolveConnectionString(configuration);
        _seedSqlServerPath = ResolveSeedSqlPath(configuration, environment);
        InitializeDatabase();
    }

    public IReadOnlyList<AdminUser> GetUsers()
    {
        using var connection = OpenConnection();
        return GetUsers(connection, null);
    }

    public IReadOnlyList<CustomerUser> GetCustomerUsers()
    {
        using var connection = OpenConnection();
        return GetCustomerUsers(connection, null);
    }

    public IReadOnlyList<EndUser> GetEndUsers(string? scopeUserId = null, string? scopeRole = null)
    {
        using var connection = OpenConnection();
        var items = GetEndUsers(connection, null);
        if (!IsOwnerScopeRequest(scopeUserId, scopeRole))
        {
            return items;
        }

        var ownerPoiIds = GetOwnerPoiIds(connection, null, scopeUserId);
        var allowedUserIds = GetUserPoiVisitLinks(connection, null)
            .Where((link) => ownerPoiIds.Contains(link.PoiId))
            .Select((link) => link.UserId)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        return items
            .Where((item) => allowedUserIds.Contains(item.Id))
            .ToList();
    }

    public EndUser? GetEndUserById(string id, string? scopeUserId = null, string? scopeRole = null)
    {
        using var connection = OpenConnection();
        if (IsOwnerScopeRequest(scopeUserId, scopeRole) && !CanOwnerAccessEndUser(connection, null, scopeUserId, id))
        {
            return null;
        }

        return GetEndUserById(connection, null, id);
    }

    public IReadOnlyList<EndUserPoiVisit> GetEndUserHistory(string id, string? scopeUserId = null, string? scopeRole = null)
    {
        using var connection = OpenConnection();
        if (IsOwnerScopeRequest(scopeUserId, scopeRole))
        {
            var ownerPoiIds = GetOwnerPoiIds(connection, null, scopeUserId);
            return GetEndUserHistory(connection, null, id)
                .Where((item) => ownerPoiIds.Contains(item.PoiId, StringComparer.OrdinalIgnoreCase))
                .ToList();
        }

        return GetEndUserHistory(connection, null, id);
    }

    public IReadOnlyList<PoiCategory> GetCategories()
    {
        using var connection = OpenConnection();
        return GetCategories(connection, null);
    }

    public IReadOnlyList<Poi> GetPois(string? scopeUserId = null, string? scopeRole = null)
    {
        using var connection = OpenConnection();
        var items = GetPois(connection, null);
        if (!IsOwnerScopeRequest(scopeUserId, scopeRole))
        {
            return items;
        }

        var ownerPoiIds = GetOwnerPoiIds(connection, null, scopeUserId);
        return items
            .Where((item) => ownerPoiIds.Contains(item.Id))
            .ToList();
    }

    public IReadOnlyList<Translation> GetTranslations()
    {
        using var connection = OpenConnection();
        return GetTranslations(connection, null);
    }

    public IReadOnlyList<AudioGuide> GetAudioGuides()
    {
        using var connection = OpenConnection();
        return GetAudioGuides(connection, null);
    }

    public IReadOnlyList<MediaAsset> GetMediaAssets()
    {
        using var connection = OpenConnection();
        return GetMediaAssets(connection, null);
    }

    public IReadOnlyList<FoodItem> GetFoodItems()
    {
        using var connection = OpenConnection();
        return GetFoodItems(connection, null);
    }

    public IReadOnlyList<TourRoute> GetRoutes()
    {
        using var connection = OpenConnection();
        return GetRoutes(connection, null);
    }

    public IReadOnlyList<Promotion> GetPromotions()
    {
        using var connection = OpenConnection();
        return GetPromotions(connection, null);
    }

    public IReadOnlyList<Review> GetReviews()
    {
        using var connection = OpenConnection();
        return GetReviews(connection, null);
    }

    public IReadOnlyList<ViewLog> GetViewLogs()
    {
        using var connection = OpenConnection();
        return GetViewLogs(connection, null);
    }

    public IReadOnlyList<AudioListenLog> GetAudioListenLogs()
    {
        using var connection = OpenConnection();
        return GetAudioListenLogs(connection, null);
    }

    public IReadOnlyList<AuditLog> GetAuditLogs()
    {
        using var connection = OpenConnection();
        return GetAuditLogs(connection, null);
    }

    public SystemSetting GetSettings()
    {
        using var connection = OpenConnection();
        return GetSettings(connection, null);
    }

    public AdminBootstrapResponse GetBootstrap(string? scopeUserId = null, string? scopeRole = null)
    {
        using var connection = OpenConnection();

        var users = GetUsers(connection, null);
        var customerUsers = GetCustomerUsers(connection, null);
        var categories = GetCategories(connection, null);
        var pois = GetPois(connection, null);
        var translations = GetTranslations(connection, null);
        var audioGuides = GetAudioGuides(connection, null);
        var mediaAssets = GetMediaAssets(connection, null);
        var foodItems = GetFoodItems(connection, null);
        var routes = GetRoutes(connection, null);
        var promotions = GetPromotions(connection, null);
        var reviews = GetReviews(connection, null);
        var viewLogs = GetViewLogs(connection, null);
        var audioListenLogs = GetAudioListenLogs(connection, null);
        var auditLogs = GetAuditLogs(connection, null);
        var settings = GetSettings(connection, null);

        if (IsOwnerScopeRequest(scopeUserId, scopeRole))
        {
            var ownerPoiIds = GetOwnerPoiIds(connection, null, scopeUserId);
            var ownerPoiIdSet = ownerPoiIds.ToHashSet(StringComparer.OrdinalIgnoreCase);
            var routePoiSet = routes
                .Where((route) => route.StopPoiIds.Any(ownerPoiIdSet.Contains))
                .Select((route) => route.Id)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            pois = pois.Where((poi) => ownerPoiIdSet.Contains(poi.Id)).ToList();
            users = users.Where((user) => string.Equals(user.Id, scopeUserId, StringComparison.OrdinalIgnoreCase)).ToList();
            categories = categories.Where((category) => pois.Any((poi) => poi.CategoryId == category.Id)).ToList();
            foodItems = foodItems.Where((item) => ownerPoiIdSet.Contains(item.PoiId)).ToList();

            var foodItemIdSet = foodItems.Select((item) => item.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var customerPoiLinks = GetUserPoiVisitLinks(connection, null);
            var allowedCustomerIds = customerPoiLinks
                .Where((link) => ownerPoiIdSet.Contains(link.PoiId))
                .Select((link) => link.UserId)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            customerUsers = customerUsers
                .Where((customer) =>
                    customer.FavoritePoiIds.Any(ownerPoiIdSet.Contains) ||
                    allowedCustomerIds.Contains(customer.Id))
                .ToList();

            routes = routes.Where((route) => route.StopPoiIds.Any(ownerPoiIdSet.Contains)).ToList();
            promotions = promotions.Where((promotion) => ownerPoiIdSet.Contains(promotion.PoiId)).ToList();
            reviews = reviews.Where((review) => ownerPoiIdSet.Contains(review.PoiId)).ToList();
            viewLogs = viewLogs.Where((log) => ownerPoiIdSet.Contains(log.PoiId)).ToList();
            audioListenLogs = audioListenLogs.Where((log) => ownerPoiIdSet.Contains(log.PoiId)).ToList();

            translations = translations
                .Where((translation) =>
                    (string.Equals(translation.EntityType, "poi", StringComparison.OrdinalIgnoreCase) &&
                     ownerPoiIdSet.Contains(translation.EntityId)) ||
                    (string.Equals(translation.EntityType, "food_item", StringComparison.OrdinalIgnoreCase) &&
                     foodItemIdSet.Contains(translation.EntityId)) ||
                    (string.Equals(translation.EntityType, "route", StringComparison.OrdinalIgnoreCase) &&
                     routePoiSet.Contains(translation.EntityId)))
                .ToList();

            audioGuides = audioGuides
                .Where((audioGuide) =>
                    (string.Equals(audioGuide.EntityType, "poi", StringComparison.OrdinalIgnoreCase) &&
                     ownerPoiIdSet.Contains(audioGuide.EntityId)) ||
                    (string.Equals(audioGuide.EntityType, "food_item", StringComparison.OrdinalIgnoreCase) &&
                     foodItemIdSet.Contains(audioGuide.EntityId)) ||
                    (string.Equals(audioGuide.EntityType, "route", StringComparison.OrdinalIgnoreCase) &&
                     routePoiSet.Contains(audioGuide.EntityId)))
                .ToList();

            mediaAssets = mediaAssets
                .Where((asset) =>
                    (string.Equals(asset.EntityType, "poi", StringComparison.OrdinalIgnoreCase) &&
                     ownerPoiIdSet.Contains(asset.EntityId)) ||
                    (string.Equals(asset.EntityType, "food_item", StringComparison.OrdinalIgnoreCase) &&
                     foodItemIdSet.Contains(asset.EntityId)) ||
                    (string.Equals(asset.EntityType, "route", StringComparison.OrdinalIgnoreCase) &&
                     routePoiSet.Contains(asset.EntityId)))
                .ToList();

            var auditTargets = pois
                .SelectMany((poi) => new[] { poi.Id, poi.Slug })
                .Concat(foodItems.Select((item) => item.Id))
                .Concat(routes.Select((route) => route.Id))
                .Concat(promotions.Select((promotion) => promotion.Id))
                .Concat(reviews.Select((review) => review.Id))
                .Append(scopeUserId ?? string.Empty)
                .Where((value) => !string.IsNullOrWhiteSpace(value))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            auditLogs = auditLogs
                .Where((log) =>
                    auditTargets.Any((target) =>
                        string.Equals(log.Target, target, StringComparison.OrdinalIgnoreCase) ||
                        log.Target.Contains(target, StringComparison.OrdinalIgnoreCase)))
                .ToList();
        }

        return new AdminBootstrapResponse(
            users,
            customerUsers,
            categories,
            pois,
            translations,
            audioGuides,
            mediaAssets,
            foodItems,
            routes,
            promotions,
            reviews,
            viewLogs,
            audioListenLogs,
            auditLogs,
            settings);
    }

    public DashboardSummaryResponse GetDashboardSummary()
    {
        using var connection = OpenConnection();

        return new DashboardSummaryResponse(
            ExecuteScalarInt(connection, null, "SELECT COUNT(*) FROM dbo.ViewLogs;"),
            ExecuteScalarInt(connection, null, "SELECT COUNT(*) FROM dbo.AudioListenLogs;"),
            ExecuteScalarInt(connection, null, "SELECT COUNT(*) FROM dbo.Pois WHERE [Status] = ?;", "published"),
            ExecuteScalarInt(connection, null, "SELECT COUNT(*) FROM dbo.Pois WHERE IsFeatured = ?;", true),
            ExecuteScalarInt(connection, null, "SELECT COUNT(*) FROM dbo.AudioGuides WHERE [Status] <> ?;", "ready"),
            ExecuteScalarInt(connection, null, "SELECT COUNT(*) FROM dbo.Reviews WHERE [Status] = ?;", "pending"),
            ExecuteScalarInt(connection, null, "SELECT COUNT(*) FROM dbo.SystemSettingLanguages WHERE LanguageType = ?;", "premium"));
    }

    private void InitializeDatabase()
    {
        try
        {
            using var connection = OpenConnection();
            var hasCoreTables = HasCoreTables(connection);

            if (!hasCoreTables)
            {
                connection.Close();
                EnsureDatabaseSeeded();
            }

            using var verifiedConnection = OpenConnection();
            EnsureRefreshSessionsTable(verifiedConnection);
            EnsureEndUserManagementSchema(verifiedConnection);
        }
        catch (SqlException exception)
        {
            throw new InvalidOperationException(
                BuildSqlConnectionFailureMessage(exception),
                exception);
        }
    }

    private bool HasCoreTables(SqlConnection connection)
    {
        return TableExists(connection, "AdminUsers") &&
            TableExists(connection, "Pois") &&
            TableExists(connection, "SystemSettings");
    }

    private void EnsureRefreshSessionsTable(SqlConnection connection)
    {
        ExecuteNonQuery(
            connection,
            null,
            """
            IF OBJECT_ID(N'dbo.RefreshSessions', N'U') IS NULL
            BEGIN
                CREATE TABLE dbo.RefreshSessions (
                    RefreshToken NVARCHAR(200) NOT NULL PRIMARY KEY,
                    UserId NVARCHAR(50) NOT NULL,
                    ExpiresAt DATETIMEOFFSET(7) NOT NULL,
                    CONSTRAINT FK_RefreshSessions_AdminUsers FOREIGN KEY (UserId) REFERENCES dbo.AdminUsers(Id)
                );
            END;
            """);
    }

    private void EnsureEndUserManagementSchema(SqlConnection connection)
    {
        ExecuteNonQuery(
            connection,
            null,
            """
            IF OBJECT_ID(N'dbo.CustomerUsers', N'U') IS NULL
            BEGIN
                CREATE TABLE dbo.CustomerUsers (
                    Id NVARCHAR(50) NOT NULL PRIMARY KEY,
                    Name NVARCHAR(120) NOT NULL,
                    Email NVARCHAR(200) NOT NULL,
                    Phone NVARCHAR(30) NOT NULL,
                    [Status] NVARCHAR(30) NOT NULL,
                    IsActive BIT NOT NULL,
                    IsBanned BIT NOT NULL,
                    PreferredLanguage NVARCHAR(20) NOT NULL,
                    IsPremium BIT NOT NULL,
                    TotalScans INT NOT NULL,
                    CreatedAt DATETIMEOFFSET(7) NOT NULL,
                    LastActiveAt DATETIMEOFFSET(7) NULL,
                    Username NVARCHAR(120) NULL,
                    DeviceId NVARCHAR(200) NULL,
                    Country NVARCHAR(20) NOT NULL,
                    DeviceType NVARCHAR(20) NOT NULL
                );
            END;

            IF COL_LENGTH(N'dbo.CustomerUsers', N'Username') IS NULL
                ALTER TABLE dbo.CustomerUsers ADD Username NVARCHAR(120) NULL;

            IF COL_LENGTH(N'dbo.CustomerUsers', N'DeviceId') IS NULL
                ALTER TABLE dbo.CustomerUsers ADD DeviceId NVARCHAR(200) NULL;

            IF COL_LENGTH(N'dbo.CustomerUsers', N'Country') IS NULL
                ALTER TABLE dbo.CustomerUsers ADD Country NVARCHAR(20) NULL;

            IF COL_LENGTH(N'dbo.CustomerUsers', N'DeviceType') IS NULL
                ALTER TABLE dbo.CustomerUsers ADD DeviceType NVARCHAR(20) NULL;

            IF COL_LENGTH(N'dbo.CustomerUsers', N'IsActive') IS NULL
                ALTER TABLE dbo.CustomerUsers ADD IsActive BIT NULL;

            IF COL_LENGTH(N'dbo.CustomerUsers', N'IsBanned') IS NULL
                ALTER TABLE dbo.CustomerUsers ADD IsBanned BIT NULL;
            """);

        ExecuteNonQuery(
            connection,
            null,
            """

            UPDATE dbo.CustomerUsers
            SET Username = NULLIF(LTRIM(RTRIM(Username)), N''),
                DeviceId = NULLIF(LTRIM(RTRIM(DeviceId)), N''),
                Country = NULLIF(UPPER(LTRIM(RTRIM(Country))), N''),
                DeviceType = LOWER(NULLIF(LTRIM(RTRIM(DeviceType)), N''));

            UPDATE dbo.CustomerUsers
            SET Username = COALESCE(
                    NULLIF(LTRIM(RTRIM(Username)), N''),
                    CASE
                        WHEN NULLIF(LTRIM(RTRIM(Email)), N'') IS NOT NULL AND CHARINDEX(N'@', Email) > 1
                            THEN LEFT(Email, CHARINDEX(N'@', Email) - 1)
                        ELSE NULL
                    END,
                    NULLIF(LTRIM(RTRIM(Name)), N''),
                    Id),
                Country = COALESCE(NULLIF(UPPER(LTRIM(RTRIM(Country))), N''), N'VN'),
                DeviceType = CASE
                    WHEN LOWER(COALESCE(DeviceType, N'')) IN (N'android', N'ios')
                        THEN LOWER(DeviceType)
                    ELSE N'android'
                END,
                IsBanned = COALESCE(
                    IsBanned,
                    CASE
                        WHEN LOWER(COALESCE([Status], N'')) IN (N'blocked', N'banned') THEN CAST(1 AS bit)
                        ELSE CAST(0 AS bit)
                    END),
                IsActive = COALESCE(
                    IsActive,
                    CASE
                        WHEN LOWER(COALESCE([Status], N'')) IN (N'inactive', N'idle') THEN CAST(0 AS bit)
                        ELSE CAST(1 AS bit)
                    END),
                PreferredLanguage = COALESCE(NULLIF(LTRIM(RTRIM(PreferredLanguage)), N''), N'vi'),
                [Status] = CASE
                    WHEN COALESCE(IsBanned,
                        CASE
                            WHEN LOWER(COALESCE([Status], N'')) IN (N'blocked', N'banned') THEN CAST(1 AS bit)
                            ELSE CAST(0 AS bit)
                        END) = CAST(1 AS bit)
                        THEN N'banned'
                    WHEN COALESCE(IsActive,
                        CASE
                            WHEN LOWER(COALESCE([Status], N'')) IN (N'inactive', N'idle') THEN CAST(0 AS bit)
                            ELSE CAST(1 AS bit)
                        END) = CAST(1 AS bit)
                        THEN N'active'
                    ELSE N'inactive'
                END
            WHERE NULLIF(LTRIM(RTRIM(Username)), N'') IS NULL
               OR NULLIF(LTRIM(RTRIM(Country)), N'') IS NULL
               OR LOWER(COALESCE(DeviceType, N'')) NOT IN (N'android', N'ios')
               OR LOWER(COALESCE([Status], N'')) NOT IN (N'active', N'banned', N'inactive')
               OR NULLIF(LTRIM(RTRIM(PreferredLanguage)), N'') IS NULL
               OR IsActive IS NULL
               OR IsBanned IS NULL;

            UPDATE dbo.CustomerUsers
            SET [Status] = CASE
                    WHEN IsBanned = CAST(1 AS bit) THEN N'banned'
                    WHEN IsActive = CAST(1 AS bit) THEN N'active'
                    ELSE N'inactive'
                END
            WHERE IsBanned IS NOT NULL AND IsActive IS NOT NULL;

            IF EXISTS (
                SELECT 1
                FROM sys.columns
                WHERE object_id = OBJECT_ID(N'dbo.CustomerUsers')
                    AND name = N'IsActive'
                    AND is_nullable = 1
            )
            BEGIN
                ALTER TABLE dbo.CustomerUsers ALTER COLUMN IsActive BIT NOT NULL;
            END;

            IF EXISTS (
                SELECT 1
                FROM sys.columns
                WHERE object_id = OBJECT_ID(N'dbo.CustomerUsers')
                    AND name = N'IsBanned'
                    AND is_nullable = 1
            )
            BEGIN
                ALTER TABLE dbo.CustomerUsers ALTER COLUMN IsBanned BIT NOT NULL;
            END;

            IF EXISTS (
                SELECT 1
                FROM sys.columns
                WHERE object_id = OBJECT_ID(N'dbo.CustomerUsers')
                    AND name = N'Country'
                    AND is_nullable = 1
            )
            BEGIN
                ALTER TABLE dbo.CustomerUsers ALTER COLUMN Country NVARCHAR(20) NOT NULL;
            END;

            IF EXISTS (
                SELECT 1
                FROM sys.columns
                WHERE object_id = OBJECT_ID(N'dbo.CustomerUsers')
                    AND name = N'DeviceType'
                    AND is_nullable = 1
            )
            BEGIN
                ALTER TABLE dbo.CustomerUsers ALTER COLUMN DeviceType NVARCHAR(20) NOT NULL;
            END;

            IF EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = N'CK_CustomerUsers_Status')
            BEGIN
                ALTER TABLE dbo.CustomerUsers DROP CONSTRAINT CK_CustomerUsers_Status;
            END;

            ALTER TABLE dbo.CustomerUsers
            ADD CONSTRAINT CK_CustomerUsers_Status
            CHECK ([Status] IN (N'active', N'banned', N'inactive'));

            IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = N'CK_CustomerUsers_DeviceType')
            BEGIN
                ALTER TABLE dbo.CustomerUsers
                ADD CONSTRAINT CK_CustomerUsers_DeviceType
                CHECK (DeviceType IN (N'android', N'ios'));
            END;

            IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = N'CK_CustomerUsers_Identity')
            BEGIN
                ALTER TABLE dbo.CustomerUsers
                ADD CONSTRAINT CK_CustomerUsers_Identity
                CHECK (
                    NULLIF(LTRIM(RTRIM(Username)), N'') IS NOT NULL OR
                    NULLIF(LTRIM(RTRIM(DeviceId)), N'') IS NOT NULL
                );
            END;

            IF OBJECT_ID(N'dbo.UserPoiVisits', N'U') IS NULL
            BEGIN
                CREATE TABLE dbo.UserPoiVisits (
                    Id NVARCHAR(50) NOT NULL PRIMARY KEY,
                    UserId NVARCHAR(50) NOT NULL,
                    PoiId NVARCHAR(50) NOT NULL,
                    VisitedAt DATETIMEOFFSET(7) NOT NULL,
                    TranslatedLanguage NVARCHAR(20) NOT NULL,
                    CONSTRAINT FK_UserPoiVisits_CustomerUsers FOREIGN KEY (UserId) REFERENCES dbo.CustomerUsers(Id),
                    CONSTRAINT FK_UserPoiVisits_Pois FOREIGN KEY (PoiId) REFERENCES dbo.Pois(Id)
                );
            END;

            IF NOT EXISTS (
                SELECT 1
                FROM sys.indexes
                WHERE name = N'IX_UserPoiVisits_UserId_VisitedAt'
                    AND object_id = OBJECT_ID(N'dbo.UserPoiVisits')
            )
            BEGIN
                CREATE INDEX IX_UserPoiVisits_UserId_VisitedAt
                ON dbo.UserPoiVisits (UserId, VisitedAt DESC);
            END;
            """);
    }

    private static bool IsOwnerScopeRequest(string? scopeUserId, string? scopeRole) =>
        !string.IsNullOrWhiteSpace(scopeUserId) &&
        string.Equals(scopeRole, "PLACE_OWNER", StringComparison.OrdinalIgnoreCase);

    private HashSet<string> GetOwnerPoiIds(SqlConnection connection, SqlTransaction? transaction, string? ownerUserId)
    {
        if (string.IsNullOrWhiteSpace(ownerUserId))
        {
            return [];
        }

        var owner = GetUserById(connection, transaction, ownerUserId);
        if (owner is null)
        {
            return [];
        }

        var poiIds = GetPois(connection, transaction)
            .Where((poi) =>
                string.Equals(poi.OwnerUserId, ownerUserId, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(poi.Id, owner.ManagedPoiId, StringComparison.OrdinalIgnoreCase))
            .Select((poi) => poi.Id)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        if (!string.IsNullOrWhiteSpace(owner.ManagedPoiId))
        {
            poiIds.Add(owner.ManagedPoiId);
        }

        return poiIds;
    }

    private IReadOnlyList<(string UserId, string PoiId)> GetUserPoiVisitLinks(
        SqlConnection connection,
        SqlTransaction? transaction)
    {
        const string sql = """
            SELECT UserId, PoiId
            FROM dbo.UserPoiVisits;
            """;

        using var command = CreateCommand(connection, transaction, sql);
        using var reader = command.ExecuteReader();

        var items = new List<(string UserId, string PoiId)>();
        while (reader.Read())
        {
            items.Add((ReadString(reader, "UserId"), ReadString(reader, "PoiId")));
        }

        return items;
    }

    private bool CanOwnerAccessEndUser(
        SqlConnection connection,
        SqlTransaction? transaction,
        string? ownerUserId,
        string endUserId)
    {
        var ownerPoiIds = GetOwnerPoiIds(connection, transaction, ownerUserId);
        if (ownerPoiIds.Count == 0)
        {
            return false;
        }

        var customer = GetCustomerUsers(connection, transaction)
            .FirstOrDefault((item) => string.Equals(item.Id, endUserId, StringComparison.OrdinalIgnoreCase));
        if (customer is not null && customer.FavoritePoiIds.Any(ownerPoiIds.Contains))
        {
            return true;
        }

        return GetUserPoiVisitLinks(connection, transaction)
            .Any((link) =>
                string.Equals(link.UserId, endUserId, StringComparison.OrdinalIgnoreCase) &&
                ownerPoiIds.Contains(link.PoiId));
    }
}
