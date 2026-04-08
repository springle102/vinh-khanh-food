using Microsoft.Data.SqlClient;
using System.Globalization;
using System.Linq;
using VinhKhanh.BackendApi.Contracts;
using VinhKhanh.BackendApi.Models;

namespace VinhKhanh.BackendApi.Infrastructure;

public sealed partial class AdminDataRepository
{
    private const int MaxAuditLogs = 120;
    private readonly string _connectionString;
    private readonly string _seedSqlServerPath;
    private readonly bool _allowCreateDatabase;
    private readonly bool _allowSeedDatabase;
    private readonly bool _allowSchemaUpdates;
    private readonly ILogger<AdminDataRepository> _logger;

    public AdminDataRepository(
        IConfiguration configuration,
        IWebHostEnvironment environment,
        ILogger<AdminDataRepository> logger)
    {
        _logger = logger;
        _connectionString = ResolveConnectionString(configuration);
        _seedSqlServerPath = ResolveSeedSqlPath(configuration, environment);
        _allowCreateDatabase = ResolveDatabaseInitializationFlag(configuration, "AllowCreateDatabase");
        _allowSeedDatabase = ResolveDatabaseInitializationFlag(configuration, "AllowSeedDatabase");
        _allowSchemaUpdates = ResolveDatabaseInitializationFlag(configuration, "AllowSchemaUpdates");
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
        var allowedUserIds = GetCustomerUsers(connection, null)
            .Where((customer) => customer.FavoritePoiIds.Any(ownerPoiIds.Contains))
            .Select((customer) => customer.Id)
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

    public AdminBootstrapResponse GetBootstrap(
        string? scopeUserId = null,
        string? scopeRole = null,
        string? customerUserId = null)
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

            customerUsers = customerUsers
                .Where((customer) => customer.FavoritePoiIds.Any(ownerPoiIdSet.Contains))
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
        else if (!string.IsNullOrWhiteSpace(customerUserId))
        {
            var customer = GetCustomerUserById(connection, null, customerUserId);
            var allowedLanguages = GetAllowedLanguageCodesForCustomer(connection, null, customerUserId, settings);

            users = [];
            customerUsers = customer is null ? [] : [customer];
            auditLogs = [];
            translations = translations
                .Where((translation) => allowedLanguages.Contains(PremiumAccessCatalog.NormalizeLanguageCode(translation.LanguageCode)))
                .ToList();
            audioGuides = audioGuides
                .Where((audioGuide) => allowedLanguages.Contains(PremiumAccessCatalog.NormalizeLanguageCode(audioGuide.LanguageCode)))
                .ToList();
        }

        var syncState = GetSyncState(connection, null);

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
            settings,
            syncState);
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
                if (!_allowSeedDatabase)
                {
                    throw new InvalidOperationException(
                        "Database hiện tại không có đủ bảng lõi và chế độ tự khởi tạo đang tắt. Hãy trỏ ConnectionStrings:AdminSqlServer tới đúng shared database hiện có hoặc chỉ bật DatabaseInitialization:AllowSeedDatabase khi thực sự cần.");
                }

                connection.Close();
                EnsureDatabaseSeeded();
            }

            if (!_allowSchemaUpdates)
            {
                _logger.LogInformation(
                    "Automatic database schema updates are disabled. Backend will use the existing shared database schema as-is.");
                return;
            }

            using var verifiedConnection = OpenConnection();
            EnsureSystemSettingsSchema(verifiedConnection);
            RemoveLegacyPoiDefaultLanguageColumn(verifiedConnection);
            EnsureRefreshSessionsTable(verifiedConnection);
            EnsureEndUserManagementSchema(verifiedConnection);
            EnsurePremiumPurchaseSchema(verifiedConnection);
            EnsureTourManagementSchema(verifiedConnection);
            NormalizeLegacyEntityTypes(verifiedConnection);
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

    private void EnsureSystemSettingsSchema(SqlConnection connection)
    {
        ExecuteNonQuery(
            connection,
            null,
            """
            IF OBJECT_ID(N'dbo.SystemSettings', N'U') IS NULL
            BEGIN
                CREATE TABLE dbo.SystemSettings (
                    Id INT NOT NULL PRIMARY KEY,
                    AppName NVARCHAR(150) NOT NULL,
                    SupportEmail NVARCHAR(200) NOT NULL,
                    DefaultLanguage NVARCHAR(20) NOT NULL,
                    FallbackLanguage NVARCHAR(20) NOT NULL,
                    PremiumUnlockPriceUsd INT NOT NULL,
                    MapProvider NVARCHAR(50) NOT NULL,
                    StorageProvider NVARCHAR(50) NOT NULL,
                    TtsProvider NVARCHAR(50) NOT NULL,
                    GeofenceRadiusMeters INT NOT NULL,
                    GuestReviewEnabled BIT NOT NULL,
                    AnalyticsRetentionDays INT NOT NULL
                );
            END;

            IF COL_LENGTH(N'dbo.SystemSettings', N'PremiumUnlockPriceUsd') IS NULL
                ALTER TABLE dbo.SystemSettings ADD PremiumUnlockPriceUsd INT NULL;

            IF COL_LENGTH(N'dbo.SystemSettings', N'GuestReviewEnabled') IS NULL
                ALTER TABLE dbo.SystemSettings ADD GuestReviewEnabled BIT NULL;

            IF COL_LENGTH(N'dbo.SystemSettings', N'AnalyticsRetentionDays') IS NULL
                ALTER TABLE dbo.SystemSettings ADD AnalyticsRetentionDays INT NULL;
            """);

        ExecuteNonQuery(
            connection,
            null,
            $"""
            UPDATE dbo.SystemSettings
            SET PremiumUnlockPriceUsd = CASE
                    WHEN PremiumUnlockPriceUsd IS NULL OR PremiumUnlockPriceUsd <= 0 THEN {PremiumAccessCatalog.DefaultPremiumPriceUsd}
                    ELSE PremiumUnlockPriceUsd
                END,
                GuestReviewEnabled = COALESCE(GuestReviewEnabled, CAST(1 AS bit)),
                AnalyticsRetentionDays = COALESCE(AnalyticsRetentionDays, 180)
            WHERE PremiumUnlockPriceUsd IS NULL
               OR PremiumUnlockPriceUsd <= 0
               OR GuestReviewEnabled IS NULL
               OR AnalyticsRetentionDays IS NULL;

            IF EXISTS (
                SELECT 1
                FROM sys.columns
                WHERE object_id = OBJECT_ID(N'dbo.SystemSettings')
                    AND name = N'PremiumUnlockPriceUsd'
                    AND is_nullable = 1
            )
            BEGIN
                ALTER TABLE dbo.SystemSettings ALTER COLUMN PremiumUnlockPriceUsd INT NOT NULL;
            END;

            IF EXISTS (
                SELECT 1
                FROM sys.columns
                WHERE object_id = OBJECT_ID(N'dbo.SystemSettings')
                    AND name = N'GuestReviewEnabled'
                    AND is_nullable = 1
            )
            BEGIN
                ALTER TABLE dbo.SystemSettings ALTER COLUMN GuestReviewEnabled BIT NOT NULL;
            END;

            IF EXISTS (
                SELECT 1
                FROM sys.columns
                WHERE object_id = OBJECT_ID(N'dbo.SystemSettings')
                    AND name = N'AnalyticsRetentionDays'
                    AND is_nullable = 1
            )
            BEGIN
                ALTER TABLE dbo.SystemSettings ALTER COLUMN AnalyticsRetentionDays INT NOT NULL;
            END;
            """);

        ExecuteNonQuery(
            connection,
            null,
            """
            IF OBJECT_ID(N'dbo.SystemSettingLanguages', N'U') IS NULL
            BEGIN
                CREATE TABLE dbo.SystemSettingLanguages (
                    SettingId INT NOT NULL,
                    LanguageType NVARCHAR(20) NOT NULL,
                    LanguageCode NVARCHAR(20) NOT NULL,
                    PRIMARY KEY (SettingId, LanguageType, LanguageCode),
                    CONSTRAINT FK_SystemSettingLanguages_SystemSettings FOREIGN KEY (SettingId) REFERENCES dbo.SystemSettings(Id)
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
                    [Password] NVARCHAR(200) NOT NULL,
                    [Status] NVARCHAR(30) NOT NULL,
                    IsActive BIT NOT NULL,
                    IsBanned BIT NOT NULL,
                    PreferredLanguage NVARCHAR(20) NOT NULL,
                    IsPremium BIT NOT NULL,
                    CreatedAt DATETIMEOFFSET(7) NOT NULL,
                    LastActiveAt DATETIMEOFFSET(7) NULL,
                    Username NVARCHAR(120) NULL,
                    Country NVARCHAR(20) NOT NULL
                );
            END;

            IF COL_LENGTH(N'dbo.CustomerUsers', N'Username') IS NULL
                ALTER TABLE dbo.CustomerUsers ADD Username NVARCHAR(120) NULL;

            IF COL_LENGTH(N'dbo.CustomerUsers', N'Password') IS NULL
                ALTER TABLE dbo.CustomerUsers ADD [Password] NVARCHAR(200) NULL;

            IF COL_LENGTH(N'dbo.CustomerUsers', N'Country') IS NULL
                ALTER TABLE dbo.CustomerUsers ADD Country NVARCHAR(20) NULL;

            IF COL_LENGTH(N'dbo.CustomerUsers', N'IsActive') IS NULL
                ALTER TABLE dbo.CustomerUsers ADD IsActive BIT NULL;

            IF COL_LENGTH(N'dbo.CustomerUsers', N'IsBanned') IS NULL
                ALTER TABLE dbo.CustomerUsers ADD IsBanned BIT NULL;

            IF COL_LENGTH(N'dbo.CustomerUsers', N'IsPremium') IS NULL
                ALTER TABLE dbo.CustomerUsers ADD IsPremium BIT NULL;
            """);

        ExecuteNonQuery(
            connection,
            null,
            """
            IF EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = N'CK_CustomerUsers_DeviceType')
            BEGIN
                ALTER TABLE dbo.CustomerUsers DROP CONSTRAINT CK_CustomerUsers_DeviceType;
            END;

            IF EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = N'CK_CustomerUsers_Identity')
            BEGIN
                ALTER TABLE dbo.CustomerUsers DROP CONSTRAINT CK_CustomerUsers_Identity;
            END;

            IF COL_LENGTH(N'dbo.CustomerUsers', N'DeviceId') IS NOT NULL
            BEGIN
                ALTER TABLE dbo.CustomerUsers DROP COLUMN DeviceId;
            END;

            IF COL_LENGTH(N'dbo.CustomerUsers', N'DeviceType') IS NOT NULL
            BEGIN
                ALTER TABLE dbo.CustomerUsers DROP COLUMN DeviceType;
            END;

            IF COL_LENGTH(N'dbo.CustomerUsers', N'TotalScans') IS NOT NULL
            BEGIN
                ALTER TABLE dbo.CustomerUsers DROP COLUMN TotalScans;
            END;

            UPDATE dbo.CustomerUsers
            SET Username = NULLIF(LTRIM(RTRIM(Username)), N''),
                Email = NULLIF(LTRIM(RTRIM(Email)), N''),
                Phone = NULLIF(LTRIM(RTRIM(Phone)), N''),
                [Password] = NULLIF(LTRIM(RTRIM([Password])), N''),
                Country = NULLIF(UPPER(LTRIM(RTRIM(Country))), N''),
                PreferredLanguage = NULLIF(LTRIM(RTRIM(PreferredLanguage)), N'');

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
                [Password] = COALESCE(NULLIF(LTRIM(RTRIM([Password])), N''), N'Customer@123'),
                Country = COALESCE(NULLIF(UPPER(LTRIM(RTRIM(Country))), N''), N'VN'),
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
                IsPremium = COALESCE(IsPremium, CAST(0 AS bit)),
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
               OR LOWER(COALESCE([Status], N'')) NOT IN (N'active', N'banned', N'inactive')
               OR NULLIF(LTRIM(RTRIM(PreferredLanguage)), N'') IS NULL
               OR NULLIF(LTRIM(RTRIM([Password])), N'') IS NULL
               OR IsPremium IS NULL
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
                    AND name = N'IsPremium'
                    AND is_nullable = 1
            )
            BEGIN
                ALTER TABLE dbo.CustomerUsers ALTER COLUMN IsPremium BIT NOT NULL;
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
                    AND name = N'Password'
                    AND is_nullable = 1
            )
            BEGIN
                ALTER TABLE dbo.CustomerUsers ALTER COLUMN [Password] NVARCHAR(200) NOT NULL;
            END;

            IF EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = N'CK_CustomerUsers_Status')
            BEGIN
                ALTER TABLE dbo.CustomerUsers DROP CONSTRAINT CK_CustomerUsers_Status;
            END;

            ALTER TABLE dbo.CustomerUsers
            ADD CONSTRAINT CK_CustomerUsers_Status
            CHECK ([Status] IN (N'active', N'banned', N'inactive'));

            IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = N'CK_CustomerUsers_Identity')
            BEGIN
                ALTER TABLE dbo.CustomerUsers
                ADD CONSTRAINT CK_CustomerUsers_Identity
                CHECK (
                    NULLIF(LTRIM(RTRIM(Username)), N'') IS NOT NULL OR
                    NULLIF(LTRIM(RTRIM(Email)), N'') IS NOT NULL
                );
            END;

            IF OBJECT_ID(N'dbo.UserPoiVisits', N'U') IS NOT NULL
            BEGIN
                DROP TABLE dbo.UserPoiVisits;
            END;
            """);
    }

    private void RemoveLegacyPoiDefaultLanguageColumn(SqlConnection connection)
    {
        const string columnExistsSql = """
            SELECT COL_LENGTH(N'dbo.Pois', N'DefaultLanguageCode');
            """;
        using var columnExistsCommand = CreateCommand(connection, null, columnExistsSql);
        if (columnExistsCommand.ExecuteScalar() is null or DBNull)
        {
            return;
        }

        const string defaultConstraintSql = """
            SELECT TOP 1 dc.name
            FROM sys.default_constraints dc
            INNER JOIN sys.columns c
                ON c.default_object_id = dc.object_id
            WHERE dc.parent_object_id = OBJECT_ID(N'dbo.Pois')
              AND c.name = N'DefaultLanguageCode';
            """;
        using var defaultConstraintCommand = CreateCommand(connection, null, defaultConstraintSql);
        var defaultConstraintName = Convert.ToString(defaultConstraintCommand.ExecuteScalar(), CultureInfo.InvariantCulture);

        if (!string.IsNullOrWhiteSpace(defaultConstraintName))
        {
            ExecuteNonQuery(
                connection,
                null,
                $"ALTER TABLE dbo.Pois DROP CONSTRAINT {QuoteSqlIdentifier(defaultConstraintName)};");
        }

        ExecuteNonQuery(
            connection,
            null,
            """
            ALTER TABLE dbo.Pois DROP COLUMN DefaultLanguageCode;
            """);
    }

    private void EnsurePremiumPurchaseSchema(SqlConnection connection)
    {
        ExecuteNonQuery(
            connection,
            null,
            """
            IF OBJECT_ID(N'dbo.PremiumPurchaseTransactions', N'U') IS NULL
            BEGIN
                CREATE TABLE dbo.PremiumPurchaseTransactions (
                    Id NVARCHAR(50) NOT NULL PRIMARY KEY,
                    CustomerUserId NVARCHAR(50) NOT NULL,
                    AmountUsd INT NOT NULL,
                    CurrencyCode NVARCHAR(10) NOT NULL,
                    PaymentProvider NVARCHAR(30) NOT NULL,
                    PaymentMethod NVARCHAR(30) NOT NULL,
                    PaymentReference NVARCHAR(100) NULL,
                    MaskedAccount NVARCHAR(60) NULL,
                    IdempotencyKey NVARCHAR(100) NOT NULL,
                    [Status] NVARCHAR(20) NOT NULL,
                    FailureMessage NVARCHAR(300) NULL,
                    CreatedAt DATETIMEOFFSET(7) NOT NULL,
                    ProcessedAt DATETIMEOFFSET(7) NULL,
                    CONSTRAINT FK_PremiumPurchaseTransactions_CustomerUsers
                        FOREIGN KEY (CustomerUserId) REFERENCES dbo.CustomerUsers(Id)
                );
            END;

            IF COL_LENGTH(N'dbo.PremiumPurchaseTransactions', N'PaymentReference') IS NULL
                ALTER TABLE dbo.PremiumPurchaseTransactions ADD PaymentReference NVARCHAR(100) NULL;

            IF COL_LENGTH(N'dbo.PremiumPurchaseTransactions', N'MaskedAccount') IS NULL
                ALTER TABLE dbo.PremiumPurchaseTransactions ADD MaskedAccount NVARCHAR(60) NULL;

            IF COL_LENGTH(N'dbo.PremiumPurchaseTransactions', N'IdempotencyKey') IS NULL
                ALTER TABLE dbo.PremiumPurchaseTransactions ADD IdempotencyKey NVARCHAR(100) NULL;

            IF COL_LENGTH(N'dbo.PremiumPurchaseTransactions', N'FailureMessage') IS NULL
                ALTER TABLE dbo.PremiumPurchaseTransactions ADD FailureMessage NVARCHAR(300) NULL;

            IF COL_LENGTH(N'dbo.PremiumPurchaseTransactions', N'ProcessedAt') IS NULL
                ALTER TABLE dbo.PremiumPurchaseTransactions ADD ProcessedAt DATETIMEOFFSET(7) NULL;
            """);

        ExecuteNonQuery(
            connection,
            null,
            """
            UPDATE dbo.PremiumPurchaseTransactions
            SET CurrencyCode = COALESCE(NULLIF(LTRIM(RTRIM(CurrencyCode)), N''), N'USD'),
                PaymentProvider = COALESCE(NULLIF(LTRIM(RTRIM(PaymentProvider)), N''), N'mock'),
                PaymentMethod = COALESCE(NULLIF(LTRIM(RTRIM(PaymentMethod)), N''), N'bank_card'),
                [Status] = CASE
                    WHEN LOWER(COALESCE([Status], N'')) IN (N'pending', N'succeeded', N'failed') THEN LOWER([Status])
                    ELSE N'succeeded'
                END,
                IdempotencyKey = COALESCE(NULLIF(LTRIM(RTRIM(IdempotencyKey)), N''), CONCAT(N'legacy-', Id))
            WHERE CurrencyCode IS NULL
               OR NULLIF(LTRIM(RTRIM(CurrencyCode)), N'') IS NULL
               OR PaymentProvider IS NULL
               OR NULLIF(LTRIM(RTRIM(PaymentProvider)), N'') IS NULL
               OR PaymentMethod IS NULL
               OR NULLIF(LTRIM(RTRIM(PaymentMethod)), N'') IS NULL
               OR [Status] IS NULL
               OR LOWER(COALESCE([Status], N'')) NOT IN (N'pending', N'succeeded', N'failed')
               OR IdempotencyKey IS NULL
               OR NULLIF(LTRIM(RTRIM(IdempotencyKey)), N'') IS NULL;

            IF EXISTS (
                SELECT 1
                FROM sys.columns
                WHERE object_id = OBJECT_ID(N'dbo.PremiumPurchaseTransactions')
                    AND name = N'IdempotencyKey'
                    AND is_nullable = 1
            )
            BEGIN
                ALTER TABLE dbo.PremiumPurchaseTransactions ALTER COLUMN IdempotencyKey NVARCHAR(100) NOT NULL;
            END;

            IF NOT EXISTS (
                SELECT 1
                FROM sys.check_constraints
                WHERE name = N'CK_PremiumPurchaseTransactions_Status'
            )
            BEGIN
                ALTER TABLE dbo.PremiumPurchaseTransactions
                ADD CONSTRAINT CK_PremiumPurchaseTransactions_Status
                CHECK ([Status] IN (N'pending', N'succeeded', N'failed'));
            END;

            IF NOT EXISTS (
                SELECT 1
                FROM sys.indexes
                WHERE name = N'UX_PremiumPurchaseTransactions_Customer_Idempotency'
                    AND object_id = OBJECT_ID(N'dbo.PremiumPurchaseTransactions')
            )
            BEGIN
                CREATE UNIQUE INDEX UX_PremiumPurchaseTransactions_Customer_Idempotency
                ON dbo.PremiumPurchaseTransactions (CustomerUserId, IdempotencyKey);
            END;

            IF NOT EXISTS (
                SELECT 1
                FROM sys.indexes
                WHERE name = N'UX_PremiumPurchaseTransactions_Customer_Pending'
                    AND object_id = OBJECT_ID(N'dbo.PremiumPurchaseTransactions')
            )
            BEGIN
                CREATE UNIQUE INDEX UX_PremiumPurchaseTransactions_Customer_Pending
                ON dbo.PremiumPurchaseTransactions (CustomerUserId)
                WHERE [Status] = N'pending';
            END;
            """);
    }

    private void EnsureTourManagementSchema(SqlConnection connection)
    {
        ExecuteNonQuery(
            connection,
            null,
            """
            IF OBJECT_ID(N'dbo.Routes', N'U') IS NULL
            BEGIN
                CREATE TABLE dbo.Routes (
                    Id NVARCHAR(50) NOT NULL PRIMARY KEY,
                    Name NVARCHAR(150) NOT NULL,
                    Theme NVARCHAR(100) NOT NULL,
                    [Description] NVARCHAR(MAX) NOT NULL,
                    DurationMinutes INT NOT NULL,
                    CoverImageUrl NVARCHAR(500) NOT NULL,
                    Difficulty NVARCHAR(30) NOT NULL,
                    IsFeatured BIT NOT NULL,
                    IsActive BIT NOT NULL,
                    UpdatedBy NVARCHAR(120) NOT NULL,
                    UpdatedAt DATETIMEOFFSET(7) NOT NULL
                );
            END;

            IF COL_LENGTH(N'dbo.Routes', N'Theme') IS NULL
                ALTER TABLE dbo.Routes ADD Theme NVARCHAR(100) NULL;

            IF COL_LENGTH(N'dbo.Routes', N'CoverImageUrl') IS NULL
                ALTER TABLE dbo.Routes ADD CoverImageUrl NVARCHAR(500) NULL;

            IF COL_LENGTH(N'dbo.Routes', N'Difficulty') IS NULL
                ALTER TABLE dbo.Routes ADD Difficulty NVARCHAR(30) NULL;

            IF COL_LENGTH(N'dbo.Routes', N'IsFeatured') IS NULL
                ALTER TABLE dbo.Routes ADD IsFeatured BIT NULL;

            IF COL_LENGTH(N'dbo.Routes', N'IsActive') IS NULL
                ALTER TABLE dbo.Routes ADD IsActive BIT NULL;

            IF COL_LENGTH(N'dbo.Routes', N'UpdatedBy') IS NULL
                ALTER TABLE dbo.Routes ADD UpdatedBy NVARCHAR(120) NULL;

            IF COL_LENGTH(N'dbo.Routes', N'UpdatedAt') IS NULL
                ALTER TABLE dbo.Routes ADD UpdatedAt DATETIMEOFFSET(7) NULL;
            """);

        ExecuteNonQuery(
            connection,
            null,
            """
            UPDATE dbo.Routes
            SET Name = CASE NULLIF(LTRIM(RTRIM(Name)), N'')
                    WHEN N'Khoi dau 45 phut' THEN N'Khởi đầu 45 phút'
                    WHEN N'Hai san buoi toi' THEN N'Hải sản buổi tối'
                    ELSE Name
                END,
                Theme = CASE
                    WHEN NULLIF(LTRIM(RTRIM(Theme)), N'') = N'An vat' THEN N'Ăn vặt'
                    WHEN NULLIF(LTRIM(RTRIM(Theme)), N'') = N'Hai san' THEN N'Hải sản'
                    WHEN NULLIF(LTRIM(RTRIM(Theme)), N'') = N'Buoi toi' THEN N'Buổi tối'
                    WHEN NULLIF(LTRIM(RTRIM(Theme)), N'') = N'Khach quoc te' THEN N'Khách quốc tế'
                    WHEN NULLIF(LTRIM(RTRIM(Theme)), N'') = N'Gia dinh' THEN N'Gia đình'
                    WHEN NULLIF(LTRIM(RTRIM(Theme)), N'') = N'Tong hop' THEN N'Tổng hợp'
                    ELSE COALESCE(
                        NULLIF(LTRIM(RTRIM(Theme)), N''),
                        CASE
                            WHEN LOWER(COALESCE(Difficulty, N'')) = N'foodie' THEN N'Hải sản'
                            WHEN LOWER(COALESCE(Difficulty, N'')) = N'easy' THEN N'Tổng hợp'
                            ELSE N'Tổng hợp'
                        END)
                END,
                [Description] = CASE NULLIF(LTRIM(RTRIM([Description])), N'')
                    WHEN N'Tour ngan cho khach moi den, uu tien cac POI noi bat va nhung mon de tiep can.'
                        THEN N'Tour ngắn cho khách mới đến, ưu tiên các POI nổi bật và những món dễ tiếp cận.'
                    WHEN N'Tour buoi toi tap trung vao mon nuong, oc va khong khi pho am thuc ve dem.'
                        THEN N'Tour buổi tối tập trung vào món nướng, ốc và không khí phố ẩm thực về đêm.'
                    ELSE [Description]
                END,
                CoverImageUrl = COALESCE(NULLIF(LTRIM(RTRIM(CoverImageUrl)), N''), N''),
                Difficulty = COALESCE(NULLIF(LTRIM(RTRIM(Difficulty)), N''), N'custom'),
                IsFeatured = COALESCE(IsFeatured, CAST(0 AS bit)),
                IsActive = COALESCE(IsActive, CAST(1 AS bit)),
                UpdatedBy = CASE
                    WHEN NULLIF(LTRIM(RTRIM(UpdatedBy)), N'') = N'Minh Anh' THEN N'Minh Ánh'
                    ELSE COALESCE(NULLIF(LTRIM(RTRIM(UpdatedBy)), N''), N'SYSTEM')
                END,
                UpdatedAt = COALESCE(UpdatedAt, SYSDATETIMEOFFSET());

            IF EXISTS (
                SELECT 1
                FROM sys.columns
                WHERE object_id = OBJECT_ID(N'dbo.Routes')
                    AND name = N'Theme'
                    AND is_nullable = 1
            )
            BEGIN
                ALTER TABLE dbo.Routes ALTER COLUMN Theme NVARCHAR(100) NOT NULL;
            END;

            IF EXISTS (
                SELECT 1
                FROM sys.columns
                WHERE object_id = OBJECT_ID(N'dbo.Routes')
                    AND name = N'CoverImageUrl'
                    AND is_nullable = 1
            )
            BEGIN
                ALTER TABLE dbo.Routes ALTER COLUMN CoverImageUrl NVARCHAR(500) NOT NULL;
            END;

            IF EXISTS (
                SELECT 1
                FROM sys.columns
                WHERE object_id = OBJECT_ID(N'dbo.Routes')
                    AND name = N'Difficulty'
                    AND is_nullable = 1
            )
            BEGIN
                ALTER TABLE dbo.Routes ALTER COLUMN Difficulty NVARCHAR(30) NOT NULL;
            END;

            IF EXISTS (
                SELECT 1
                FROM sys.columns
                WHERE object_id = OBJECT_ID(N'dbo.Routes')
                    AND name = N'IsFeatured'
                    AND is_nullable = 1
            )
            BEGIN
                ALTER TABLE dbo.Routes ALTER COLUMN IsFeatured BIT NOT NULL;
            END;

            IF EXISTS (
                SELECT 1
                FROM sys.columns
                WHERE object_id = OBJECT_ID(N'dbo.Routes')
                    AND name = N'IsActive'
                    AND is_nullable = 1
            )
            BEGIN
                ALTER TABLE dbo.Routes ALTER COLUMN IsActive BIT NOT NULL;
            END;

            IF EXISTS (
                SELECT 1
                FROM sys.columns
                WHERE object_id = OBJECT_ID(N'dbo.Routes')
                    AND name = N'UpdatedBy'
                    AND is_nullable = 1
            )
            BEGIN
                ALTER TABLE dbo.Routes ALTER COLUMN UpdatedBy NVARCHAR(120) NOT NULL;
            END;

            IF EXISTS (
                SELECT 1
                FROM sys.columns
                WHERE object_id = OBJECT_ID(N'dbo.Routes')
                    AND name = N'UpdatedAt'
                    AND is_nullable = 1
            )
            BEGIN
                ALTER TABLE dbo.Routes ALTER COLUMN UpdatedAt DATETIMEOFFSET(7) NOT NULL;
            END;
            """);

        ExecuteNonQuery(
            connection,
            null,
            """
            IF OBJECT_ID(N'dbo.RouteStops', N'U') IS NULL
            BEGIN
                CREATE TABLE dbo.RouteStops (
                    RouteId NVARCHAR(50) NOT NULL,
                    StopOrder INT NOT NULL,
                    PoiId NVARCHAR(50) NOT NULL,
                    PRIMARY KEY (RouteId, StopOrder)
                );
            END;

            IF NOT EXISTS (
                SELECT 1
                FROM sys.foreign_keys
                WHERE name = N'FK_RouteStops_Routes'
            )
            BEGIN
                ALTER TABLE dbo.RouteStops
                ADD CONSTRAINT FK_RouteStops_Routes FOREIGN KEY (RouteId) REFERENCES dbo.Routes(Id);
            END;

            IF NOT EXISTS (
                SELECT 1
                FROM sys.foreign_keys
                WHERE name = N'FK_RouteStops_Pois'
            )
            BEGIN
                ALTER TABLE dbo.RouteStops
                ADD CONSTRAINT FK_RouteStops_Pois FOREIGN KEY (PoiId) REFERENCES dbo.Pois(Id);
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

        return false;
    }

    private void NormalizeLegacyEntityTypes(SqlConnection connection)
    {
        var migratedTranslations = ExecuteNonQuery(
            connection,
            null,
            """
            UPDATE legacy
            SET EntityType = N'poi'
            FROM dbo.PoiTranslations legacy
            WHERE legacy.EntityType = N'place'
              AND NOT EXISTS (
                  SELECT 1
                  FROM dbo.PoiTranslations normalized
                  WHERE normalized.EntityType = N'poi'
                    AND normalized.EntityId = legacy.EntityId
                    AND normalized.LanguageCode = legacy.LanguageCode
              );
            """);

        var deletedDuplicateTranslations = ExecuteNonQuery(
            connection,
            null,
            """
            DELETE legacy
            FROM dbo.PoiTranslations legacy
            WHERE legacy.EntityType = N'place'
              AND EXISTS (
                  SELECT 1
                  FROM dbo.PoiTranslations normalized
                  WHERE normalized.EntityType = N'poi'
                    AND normalized.EntityId = legacy.EntityId
                    AND normalized.LanguageCode = legacy.LanguageCode
              );
            """);

        var migratedAudioGuides = ExecuteNonQuery(
            connection,
            null,
            """
            UPDATE legacy
            SET EntityType = N'poi'
            FROM dbo.AudioGuides legacy
            WHERE legacy.EntityType = N'place'
              AND NOT EXISTS (
                  SELECT 1
                  FROM dbo.AudioGuides normalized
                  WHERE normalized.EntityType = N'poi'
                    AND normalized.EntityId = legacy.EntityId
                    AND normalized.LanguageCode = legacy.LanguageCode
              );
            """);

        var deletedDuplicateAudioGuides = ExecuteNonQuery(
            connection,
            null,
            """
            DELETE legacy
            FROM dbo.AudioGuides legacy
            WHERE legacy.EntityType = N'place'
              AND EXISTS (
                  SELECT 1
                  FROM dbo.AudioGuides normalized
                  WHERE normalized.EntityType = N'poi'
                    AND normalized.EntityId = legacy.EntityId
                    AND normalized.LanguageCode = legacy.LanguageCode
              );
            """);

        var migratedMediaAssets = ExecuteNonQuery(
            connection,
            null,
            """
            UPDATE dbo.MediaAssets
            SET EntityType = N'poi'
            WHERE EntityType = N'place';
            """);

        if (migratedTranslations > 0 || deletedDuplicateTranslations > 0 ||
            migratedAudioGuides > 0 || deletedDuplicateAudioGuides > 0 || migratedMediaAssets > 0)
        {
            _logger.LogInformation(
                "Normalized legacy POI entity types. translationsMigrated={TranslationsMigrated}, translationsDeleted={TranslationsDeleted}, audioMigrated={AudioMigrated}, audioDeleted={AudioDeleted}, mediaMigrated={MediaMigrated}",
                migratedTranslations,
                deletedDuplicateTranslations,
                migratedAudioGuides,
                deletedDuplicateAudioGuides,
                migratedMediaAssets);
        }
    }
}
