using Microsoft.Data.SqlClient;
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

    public IReadOnlyList<EndUser> GetEndUsers()
    {
        using var connection = OpenConnection();
        return GetEndUsers(connection, null);
    }

    public EndUser? GetEndUserById(string id)
    {
        using var connection = OpenConnection();
        return GetEndUserById(connection, null, id);
    }

    public IReadOnlyList<EndUserPoiVisit> GetEndUserHistory(string id)
    {
        using var connection = OpenConnection();
        return GetEndUserHistory(connection, null, id);
    }

    public IReadOnlyList<PoiCategory> GetCategories()
    {
        using var connection = OpenConnection();
        return GetCategories(connection, null);
    }

    public IReadOnlyList<Poi> GetPois()
    {
        using var connection = OpenConnection();
        return GetPois(connection, null);
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

    public AdminBootstrapResponse GetBootstrap()
    {
        using var connection = OpenConnection();

        return new AdminBootstrapResponse(
            GetUsers(connection, null),
            GetCustomerUsers(connection, null),
            GetCategories(connection, null),
            GetPois(connection, null),
            GetTranslations(connection, null),
            GetAudioGuides(connection, null),
            GetMediaAssets(connection, null),
            GetFoodItems(connection, null),
            GetRoutes(connection, null),
            GetPromotions(connection, null),
            GetReviews(connection, null),
            GetViewLogs(connection, null),
            GetAudioListenLogs(connection, null),
            GetAuditLogs(connection, null),
            GetSettings(connection, null));
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
}
