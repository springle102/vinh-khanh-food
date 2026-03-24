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
}
