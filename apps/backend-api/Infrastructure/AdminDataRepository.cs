using Microsoft.Data.SqlClient;
using System.Globalization;
using System.Linq;
using VinhKhanh.BackendApi.Contracts;
using VinhKhanh.BackendApi.Models;

namespace VinhKhanh.BackendApi.Infrastructure;

public sealed partial class AdminDataRepository
{
    private const int MaxAuditLogs = 120;
    private const int MaxUserActivityLogs = 500;
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

    public AdminRequestContext? GetAdminRequestContext(string accessToken)
    {
        using var connection = OpenConnection();
        return GetAdminRequestContext(connection, null, accessToken, DateTimeOffset.UtcNow);
    }

    public IReadOnlyList<AdminUser> GetUsers(AdminRequestContext? actor = null)
    {
        using var connection = OpenConnection();
        var items = GetUsers(connection, null);
        if (actor is null || actor.IsSuperAdmin)
        {
            return items;
        }

        return items
            .Where(item => string.Equals(item.Id, actor.UserId, StringComparison.OrdinalIgnoreCase))
            .ToList();
    }

    public IReadOnlyList<CustomerUser> GetCustomerUsers()
    {
        using var connection = OpenConnection();
        return GetCustomerUsers(connection, null);
    }

    public IReadOnlyList<EndUser> GetEndUsers(AdminRequestContext actor)
    {
        if (!actor.IsSuperAdmin)
        {
            throw new ApiForbiddenException("Chủ quán không được phép xem danh sách người dùng cuối của toàn hệ thống.");
        }

        using var connection = OpenConnection();
        return GetEndUsers(connection, null);
    }

    public EndUser? GetEndUserById(string id, AdminRequestContext actor)
    {
        if (!actor.IsSuperAdmin)
        {
            throw new ApiForbiddenException("Chủ quán không được phép xem chi tiết người dùng cuối.");
        }

        using var connection = OpenConnection();
        return GetEndUserById(connection, null, id);
    }

    public IReadOnlyList<PoiCategory> GetCategories()
    {
        using var connection = OpenConnection();
        return GetCategories(connection, null);
    }

    public IReadOnlyList<Poi> GetPois(AdminRequestContext? actor = null)
    {
        using var connection = OpenConnection();
        return ApplyPoiScope(connection, null, GetPois(connection, null), actor);
    }

    public IReadOnlyList<Translation> GetTranslations(AdminRequestContext? actor = null)
    {
        using var connection = OpenConnection();
        var items = GetTranslations(connection, null);
        var pois = actor is null
            ? GetPois(connection, null)
            : ApplyPoiScope(connection, null, GetPois(connection, null), actor);
        var foodItems = actor is null
            ? GetFoodItems(connection, null)
            : ApplyFoodItemScope(connection, null, GetFoodItems(connection, null), actor, pois);
        var routes = actor is null
            ? GetRoutes(connection, null)
            : ApplyRouteScope(connection, null, GetRoutes(connection, null), actor);
        var promotions = actor is null
            ? GetPromotions(connection, null)
            : ApplyPromotionScope(connection, null, GetPromotions(connection, null), actor);

        if (actor is not null)
        {
            items = ApplyTranslationScope(connection, null, items, actor, pois, foodItems, routes, promotions);
        }

        items = FilterOutdatedEntityTranslations(items, pois, routes);
        return CollapseTranslations(items);
    }

    public IReadOnlyList<AudioGuide> GetAudioGuides(AdminRequestContext? actor = null)
    {
        using var connection = OpenConnection();
        var items = GetAudioGuides(connection, null);
        var pois = actor is null
            ? GetPois(connection, null)
            : ApplyPoiScope(connection, null, GetPois(connection, null), actor);
        var foodItems = actor is null
            ? GetFoodItems(connection, null)
            : ApplyFoodItemScope(connection, null, GetFoodItems(connection, null), actor, pois);
        var routes = actor is null
            ? GetRoutes(connection, null)
            : ApplyRouteScope(connection, null, GetRoutes(connection, null), actor);

        if (actor is not null)
        {
            items = ApplyAudioGuideScope(connection, null, items, actor, pois, foodItems, routes);
        }

        items = FilterOutdatedEntityAudioGuides(items, pois, routes);
        return CollapseAudioGuides(items);
    }

    public IReadOnlyList<MediaAsset> GetMediaAssets(AdminRequestContext? actor = null)
    {
        using var connection = OpenConnection();
        var items = GetMediaAssets(connection, null);
        if (actor is null)
        {
            return items;
        }

        var pois = ApplyPoiScope(connection, null, GetPois(connection, null), actor);
        var foodItems = ApplyFoodItemScope(connection, null, GetFoodItems(connection, null), actor, pois);
        var routes = ApplyRouteScope(connection, null, GetRoutes(connection, null), actor);
        var promotions = ApplyPromotionScope(connection, null, GetPromotions(connection, null), actor);
        return ApplyMediaAssetScope(connection, null, items, actor, pois, foodItems, routes, promotions);
    }

    public IReadOnlyList<FoodItem> GetFoodItems(AdminRequestContext? actor = null)
    {
        using var connection = OpenConnection();
        var pois = ApplyPoiScope(connection, null, GetPois(connection, null), actor);
        return ApplyFoodItemScope(connection, null, GetFoodItems(connection, null), actor, pois);
    }

    public IReadOnlyList<TourRoute> GetRoutes(AdminRequestContext? actor = null)
    {
        using var connection = OpenConnection();
        return ApplyRouteScope(connection, null, GetRoutes(connection, null), actor);
    }

    public bool IsRouteTranslation(string id)
    {
        using var connection = OpenConnection();
        return IsRouteEntityType(GetTranslationById(connection, null, id)?.EntityType);
    }

    public bool IsRouteAudioGuide(string id)
    {
        using var connection = OpenConnection();
        return IsRouteEntityType(GetAudioGuideById(connection, null, id)?.EntityType);
    }

    public bool IsRouteMediaAsset(string id)
    {
        using var connection = OpenConnection();
        return IsRouteEntityType(GetMediaAssetById(connection, null, id)?.EntityType);
    }

    public IReadOnlyList<Promotion> GetPromotions(AdminRequestContext? actor = null)
    {
        using var connection = OpenConnection();
        return ApplyPromotionScope(connection, null, GetPromotions(connection, null), actor);
    }

    public IReadOnlyList<ViewLog> GetViewLogs(AdminRequestContext? actor = null)
    {
        using var connection = OpenConnection();
        var items = GetViewLogs(connection, null);
        if (actor is null)
        {
            return items;
        }

        var pois = ApplyPoiScope(connection, null, GetPois(connection, null), actor);
        return ApplyViewLogScope(connection, null, items, actor, pois);
    }

    public IReadOnlyList<AudioListenLog> GetAudioListenLogs(AdminRequestContext? actor = null)
    {
        using var connection = OpenConnection();
        var items = GetAudioListenLogs(connection, null);
        if (actor is null)
        {
            return items;
        }

        var pois = ApplyPoiScope(connection, null, GetPois(connection, null), actor);
        return ApplyAudioListenLogScope(connection, null, items, actor, pois);
    }

    public IReadOnlyList<AppUsageEvent> GetAppUsageEvents(AdminRequestContext? actor = null)
    {
        using var connection = OpenConnection();
        var items = GetAppUsageEvents(connection, null);
        if (actor is null)
        {
            return items;
        }

        var pois = ApplyPoiScope(connection, null, GetPois(connection, null), actor);
        return ApplyUsageEventScope(items, actor, pois);
    }

    public IReadOnlyList<AuditLog> GetAuditLogs(AdminRequestContext actor)
    {
        using var connection = OpenConnection();
        return ApplyAuditScope(connection, null, GetAuditLogs(connection, null), actor);
    }

    public SystemSetting GetSettings()
    {
        using var connection = OpenConnection();
        return GetSettings(connection, null);
    }

    public AdminBootstrapResponse GetBootstrap(
        AdminRequestContext? admin = null,
        string? customerUserId = null)
    {
        using var connection = OpenConnection();

        var users = admin is null ? [] : GetUsers(connection, null);
        var customerUsers = Array.Empty<CustomerUser>();
        var categories = GetCategories(connection, null);
        var pois = GetPois(connection, null);
        var translations = GetTranslations(connection, null);
        var audioGuides = GetAudioGuides(connection, null);
        var mediaAssets = GetMediaAssets(connection, null);
        var foodItems = GetFoodItems(connection, null);
        var routes = GetRoutes(connection, null);
        var promotions = GetPromotions(connection, null);
        var usageEvents = GetAppUsageEvents(connection, null);
        var viewLogs = GetViewLogs(connection, null);
        var audioListenLogs = GetAudioListenLogs(connection, null);
        var auditLogs = admin is null ? [] : GetAuditLogs(connection, null);
        var settings = GetSettings(connection, null);

        if (admin is not null)
        {
            users = ApplyAdminUserScope(users, admin);
            pois = ApplyPoiScope(connection, null, pois, admin);
            // Admin POI forms need the full category catalog so create/edit can always choose a category,
            // even when the current actor has no POIs yet.
            foodItems = ApplyFoodItemScope(connection, null, foodItems, admin, pois);
            routes = ApplyRouteScope(connection, null, routes, admin);
            promotions = ApplyPromotionScope(connection, null, promotions, admin);
            usageEvents = ApplyUsageEventScope(usageEvents, admin, pois);
            viewLogs = ApplyViewLogScope(connection, null, viewLogs, admin, pois);
            audioListenLogs = ApplyAudioListenLogScope(connection, null, audioListenLogs, admin, pois);
            translations = ApplyTranslationScope(connection, null, translations, admin, pois, foodItems, routes, promotions);
            audioGuides = ApplyAudioGuideScope(connection, null, audioGuides, admin, pois, foodItems, routes);
            mediaAssets = ApplyMediaAssetScope(connection, null, mediaAssets, admin, pois, foodItems, routes, promotions);
            auditLogs = ApplyAuditScope(connection, null, auditLogs, admin);
        }
        else
        {
            var allowedLanguages = GetSupportedLanguageCodeSet(settings);

            pois = ApplyPublicPoiScope(pois);
            categories = categories.Where(category => pois.Any(poi => poi.CategoryId == category.Id)).ToList();
            foodItems = foodItems.Where(item => pois.Any(poi => poi.Id == item.PoiId)).ToList();
            routes = ApplyPublicRouteScope(routes, pois);
            promotions = ApplyPublicPromotionScope(promotions, pois);
            usageEvents = usageEvents
                .Where(item => string.IsNullOrWhiteSpace(item.PoiId) || pois.Any(poi => poi.Id == item.PoiId))
                .ToList();
            viewLogs = viewLogs.Where(log => pois.Any(poi => poi.Id == log.PoiId)).ToList();
            audioListenLogs = audioListenLogs.Where(log => pois.Any(poi => poi.Id == log.PoiId)).ToList();
            translations = ApplyPublicTranslationScope(translations, pois, foodItems, routes, promotions, allowedLanguages);
            audioGuides = ApplyPublicAudioGuideScope(audioGuides, pois, foodItems, routes, allowedLanguages);
            mediaAssets = ApplyPublicMediaAssetScope(mediaAssets, pois, foodItems, routes, promotions);
            users = [];
            auditLogs = [];
        }

        translations = FilterOutdatedEntityTranslations(translations, pois, routes);
        audioGuides = FilterOutdatedEntityAudioGuides(audioGuides, pois, routes);
        translations = CollapseTranslations(translations);
        audioGuides = CollapseAudioGuides(audioGuides);
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
            usageEvents,
            viewLogs,
            audioListenLogs,
            auditLogs,
            settings,
            syncState);
    }

    private static IReadOnlyList<Translation> CollapseTranslations(IReadOnlyList<Translation> translations)
    {
        if (translations.Count <= 1)
        {
            return translations;
        }

        return translations
            .Where(item =>
                !string.IsNullOrWhiteSpace(item.EntityType) &&
                !string.IsNullOrWhiteSpace(item.EntityId) &&
                !string.IsNullOrWhiteSpace(item.LanguageCode))
            .GroupBy(
                item => (
                    EntityType: item.EntityType.Trim().ToLowerInvariant(),
                    EntityId: item.EntityId.Trim(),
                    LanguageCode: PremiumAccessCatalog.NormalizeLanguageCode(item.LanguageCode)))
            .Select(group =>
            {
                return group
                    .OrderByDescending(item => item.UpdatedAt)
                    .ThenByDescending(item => item.Id, StringComparer.OrdinalIgnoreCase)
                    .First();
            })
            .OrderByDescending(item => item.UpdatedAt)
            .ThenByDescending(item => item.Id, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static IReadOnlyList<Translation> FilterOutdatedEntityTranslations(
        IReadOnlyList<Translation> translations,
        IReadOnlyList<Poi> pois,
        IReadOnlyList<TourRoute> routes)
    {
        // Translation titles/descriptions are the source of truth for localized POI/route content.
        // Filtering them by the entity UpdatedAt causes moderation/status-only updates to erase titles
        // from the UI and incorrectly fall back to slugs.
        return translations;
    }

    private static IReadOnlyList<AudioGuide> CollapseAudioGuides(IReadOnlyList<AudioGuide> audioGuides)
    {
        if (audioGuides.Count <= 1)
        {
            return audioGuides;
        }

        return audioGuides
            .Where(item =>
                !string.IsNullOrWhiteSpace(item.EntityType) &&
                !string.IsNullOrWhiteSpace(item.EntityId) &&
                !string.IsNullOrWhiteSpace(item.LanguageCode))
            .GroupBy(
                item => (
                    EntityType: item.EntityType.Trim().ToLowerInvariant(),
                    EntityId: item.EntityId.Trim(),
                    LanguageCode: PremiumAccessCatalog.NormalizeLanguageCode(item.LanguageCode)))
            .Select(group =>
                group
                    .OrderByDescending(item => item.UpdatedAt)
                    .ThenByDescending(item => item.Id, StringComparer.OrdinalIgnoreCase)
                    .First())
            .OrderByDescending(item => item.UpdatedAt)
            .ThenByDescending(item => item.Id, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static IReadOnlyList<AudioGuide> FilterOutdatedEntityAudioGuides(
        IReadOnlyList<AudioGuide> audioGuides,
        IReadOnlyList<Poi> pois,
        IReadOnlyList<TourRoute> routes)
    {
        if (audioGuides.Count == 0)
        {
            return audioGuides;
        }

        var poiUpdatedAtById = pois
            .Where(item => !string.IsNullOrWhiteSpace(item.Id))
            .ToDictionary(item => item.Id, item => item.UpdatedAt, StringComparer.OrdinalIgnoreCase);
        var routeUpdatedAtById = routes
            .Where(item => !string.IsNullOrWhiteSpace(item.Id))
            .ToDictionary(item => item.Id, item => item.UpdatedAt, StringComparer.OrdinalIgnoreCase);

        return audioGuides
            .Where(audioGuide =>
            {
                if (string.Equals(audioGuide.EntityType, "poi", StringComparison.OrdinalIgnoreCase) &&
                    poiUpdatedAtById.TryGetValue(audioGuide.EntityId, out var poiUpdatedAt))
                {
                    return audioGuide.UpdatedAt >= poiUpdatedAt;
                }

                if (string.Equals(audioGuide.EntityType, "route", StringComparison.OrdinalIgnoreCase) &&
                    routeUpdatedAtById.TryGetValue(audioGuide.EntityId, out var routeUpdatedAt))
                {
                    return audioGuide.UpdatedAt >= routeUpdatedAt;
                }

                return true;
            })
            .ToList();
    }

    public DashboardSummaryResponse GetDashboardSummary(AdminRequestContext actor)
    {
        using var connection = OpenConnection();
        var pois = ApplyPoiScope(connection, null, GetPois(connection, null), actor);
        var audioGuides = ApplyAudioGuideScope(
            connection,
            null,
            GetAudioGuides(connection, null),
            actor,
            pois,
            ApplyFoodItemScope(connection, null, GetFoodItems(connection, null), actor, pois),
            ApplyRouteScope(connection, null, GetRoutes(connection, null), actor));
        var usageEvents = ApplyUsageEventScope(GetAppUsageEvents(connection, null), actor, pois);

        return new DashboardSummaryResponse(
            usageEvents.Count(item => string.Equals(item.EventType, "poi_view", StringComparison.OrdinalIgnoreCase)),
            usageEvents.Count(item => string.Equals(item.EventType, "audio_play", StringComparison.OrdinalIgnoreCase)),
            usageEvents.Count(item => string.Equals(item.EventType, "qr_scan", StringComparison.OrdinalIgnoreCase)),
            pois.Count(item => string.Equals(item.Status, "published", StringComparison.OrdinalIgnoreCase)),
            audioGuides.Count(item => !string.Equals(item.Status, "ready", StringComparison.OrdinalIgnoreCase)));
    }

    private static IReadOnlyCollection<string> GetSupportedLanguageCodeSet(SystemSetting settings)
    {
        var languageCodes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var code in settings.SupportedLanguages)
        {
            languageCodes.Add(PremiumAccessCatalog.NormalizeLanguageCode(code));
        }

        languageCodes.Add(PremiumAccessCatalog.NormalizeLanguageCode(settings.DefaultLanguage));
        languageCodes.Add(PremiumAccessCatalog.NormalizeLanguageCode(settings.FallbackLanguage));

        return languageCodes
            .Where(code => !string.IsNullOrWhiteSpace(code))
            .ToList();
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

            using var verifiedConnection = OpenConnection();
            EnsureRuntimeCompatibilitySchema(verifiedConnection);
            NormalizePersistedPoiAddresses(verifiedConnection);
            NormalizeLegacyEntityTypes(verifiedConnection);
            RepairModeratedPoiLocalizedAssetTimestamps(verifiedConnection);

            if (!_allowSchemaUpdates)
            {
                _logger.LogInformation(
                    "Automatic advanced database schema updates are disabled. Backend applied the required runtime compatibility schema only.");
                return;
            }

            RemoveLegacyPoiDefaultLanguageColumn(verifiedConnection);
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
        return TableExists(connection, null, "AdminUsers") &&
            TableExists(connection, null, "Pois") &&
            TableExists(connection, null, "SystemSettings");
    }

    private void EnsureRuntimeCompatibilitySchema(SqlConnection connection)
    {
        EnsureAdminUsersRuntimeSchema(connection);
        EnsurePoiRuntimeSchema(connection);
        EnsureTranslationRuntimeSchema(connection);
        EnsureRefreshSessionsTable(connection);
        EnsureSeparatedAuditLogSchema(connection);
        RemoveLegacyReviewData(connection);
        EnsureSystemSettingsSchema(connection);
        EnsureAppUsageEventSchema(connection);
        EnsureTourManagementSchema(connection);
    }

    private void RemoveLegacyReviewData(SqlConnection connection)
    {
        ExecuteNonQuery(
            connection,
            null,
            """
            IF OBJECT_ID(N'dbo.AdminAuditLogs', N'U') IS NOT NULL
            BEGIN
                DELETE FROM dbo.AdminAuditLogs
                WHERE [Module] = N'REVIEW';
            END;

            IF OBJECT_ID(N'dbo.AuditLogs', N'U') IS NOT NULL
            BEGIN
                DELETE FROM dbo.AuditLogs
                WHERE [Action] LIKE N'%đánh giá%';
            END;

            IF OBJECT_ID(N'dbo.Reviews', N'U') IS NOT NULL
            BEGIN
                DROP TABLE dbo.Reviews;
            END;
            """);
    }

    private void EnsureAdminUsersRuntimeSchema(SqlConnection connection)
    {
        ExecuteNonQuery(
            connection,
            null,
            """
            IF COL_LENGTH(N'dbo.AdminUsers', N'AvatarColor') IS NULL
                ALTER TABLE dbo.AdminUsers ADD AvatarColor NVARCHAR(20) NULL;

            IF COL_LENGTH(N'dbo.AdminUsers', N'ManagedPoiId') IS NULL
                ALTER TABLE dbo.AdminUsers ADD ManagedPoiId NVARCHAR(50) NULL;

            IF COL_LENGTH(N'dbo.AdminUsers', N'ApprovalStatus') IS NULL
                ALTER TABLE dbo.AdminUsers ADD ApprovalStatus NVARCHAR(20) NULL;

            IF COL_LENGTH(N'dbo.AdminUsers', N'RejectionReason') IS NULL
                ALTER TABLE dbo.AdminUsers ADD RejectionReason NVARCHAR(1000) NULL;

            IF COL_LENGTH(N'dbo.AdminUsers', N'RegistrationSubmittedAt') IS NULL
                ALTER TABLE dbo.AdminUsers ADD RegistrationSubmittedAt DATETIMEOFFSET(7) NULL;

            IF COL_LENGTH(N'dbo.AdminUsers', N'RegistrationReviewedAt') IS NULL
                ALTER TABLE dbo.AdminUsers ADD RegistrationReviewedAt DATETIMEOFFSET(7) NULL;
            """);

        ExecuteNonQuery(
            connection,
            null,
            """
            UPDATE dbo.AdminUsers
            SET AvatarColor = COALESCE(NULLIF(LTRIM(RTRIM(AvatarColor)), N''), N'#f97316')
            WHERE AvatarColor IS NULL OR NULLIF(LTRIM(RTRIM(AvatarColor)), N'') IS NULL;
            """);

        ExecuteNonQuery(
            connection,
            null,
            """
            UPDATE adminUser
            SET ApprovalStatus = normalized.ApprovalStatus,
                RegistrationSubmittedAt = COALESCE(RegistrationSubmittedAt, CreatedAt),
                RegistrationReviewedAt = CASE
                    WHEN normalized.ApprovalStatus = N'rejected'
                        THEN RegistrationReviewedAt
                    WHEN normalized.ApprovalStatus = N'pending'
                        THEN RegistrationReviewedAt
                    ELSE COALESCE(RegistrationReviewedAt, CreatedAt)
                END,
                RejectionReason = CASE
                    WHEN normalized.ApprovalStatus = N'rejected'
                        THEN NULLIF(LTRIM(RTRIM(RejectionReason)), N'')
                    ELSE NULL
                END
            FROM dbo.AdminUsers adminUser
            CROSS APPLY (
                SELECT CASE
                    WHEN LOWER(COALESCE(LTRIM(RTRIM(adminUser.ApprovalStatus)), N'')) IN (N'pending', N'approved', N'rejected')
                        THEN LOWER(LTRIM(RTRIM(adminUser.ApprovalStatus)))
                    ELSE N'approved'
                END AS ApprovalStatus
            ) normalized;
            """);
    }

    private void EnsurePoiRuntimeSchema(SqlConnection connection)
    {
        ExecuteNonQuery(
            connection,
            null,
            """
            IF COL_LENGTH(N'dbo.Pois', N'OwnerUserId') IS NULL
                ALTER TABLE dbo.Pois ADD OwnerUserId NVARCHAR(50) NULL;

            IF COL_LENGTH(N'dbo.Pois', N'IsActive') IS NULL
                ALTER TABLE dbo.Pois ADD IsActive BIT NULL;

            IF COL_LENGTH(N'dbo.Pois', N'LockedBySuperAdmin') IS NULL
                ALTER TABLE dbo.Pois ADD LockedBySuperAdmin BIT NULL;

            IF COL_LENGTH(N'dbo.Pois', N'ApprovedAt') IS NULL
                ALTER TABLE dbo.Pois ADD ApprovedAt DATETIMEOFFSET(7) NULL;

            IF COL_LENGTH(N'dbo.Pois', N'RejectionReason') IS NULL
                ALTER TABLE dbo.Pois ADD RejectionReason NVARCHAR(1000) NULL;

            IF COL_LENGTH(N'dbo.Pois', N'RejectedAt') IS NULL
                ALTER TABLE dbo.Pois ADD RejectedAt DATETIMEOFFSET(7) NULL;
            """);

        ExecuteNonQuery(
            connection,
            null,
            """
            DECLARE @averageVisitDurationConstraint sysname;
            DECLARE @popularityScoreConstraint sysname;
            DECLARE @dropColumnSql nvarchar(max);

            IF COL_LENGTH(N'dbo.Pois', N'AverageVisitDurationMinutes') IS NOT NULL
            BEGIN
                SELECT @averageVisitDurationConstraint = defaultConstraint.name
                FROM sys.default_constraints defaultConstraint
                INNER JOIN sys.columns columnInfo
                    ON columnInfo.object_id = defaultConstraint.parent_object_id
                   AND columnInfo.column_id = defaultConstraint.parent_column_id
                WHERE defaultConstraint.parent_object_id = OBJECT_ID(N'dbo.Pois')
                  AND columnInfo.name = N'AverageVisitDurationMinutes';

                IF @averageVisitDurationConstraint IS NOT NULL
                BEGIN
                    SET @dropColumnSql = N'ALTER TABLE dbo.Pois DROP CONSTRAINT '
                        + QUOTENAME(@averageVisitDurationConstraint)
                        + N';';
                    EXEC sp_executesql @dropColumnSql;
                END;

                ALTER TABLE dbo.Pois DROP COLUMN AverageVisitDurationMinutes;
                SET @averageVisitDurationConstraint = NULL;
            END;

            IF COL_LENGTH(N'dbo.Pois', N'PopularityScore') IS NOT NULL
            BEGIN
                SELECT @popularityScoreConstraint = defaultConstraint.name
                FROM sys.default_constraints defaultConstraint
                INNER JOIN sys.columns columnInfo
                    ON columnInfo.object_id = defaultConstraint.parent_object_id
                   AND columnInfo.column_id = defaultConstraint.parent_column_id
                WHERE defaultConstraint.parent_object_id = OBJECT_ID(N'dbo.Pois')
                  AND columnInfo.name = N'PopularityScore';

                IF @popularityScoreConstraint IS NOT NULL
                BEGIN
                    SET @dropColumnSql = N'ALTER TABLE dbo.Pois DROP CONSTRAINT '
                        + QUOTENAME(@popularityScoreConstraint)
                        + N';';
                    EXEC sp_executesql @dropColumnSql;
                END;

                ALTER TABLE dbo.Pois DROP COLUMN PopularityScore;
                SET @popularityScoreConstraint = NULL;
            END;
            """);

        ExecuteNonQuery(
            connection,
            null,
            """
            IF COL_LENGTH(N'dbo.Pois', N'IsActive') IS NOT NULL
               AND NOT EXISTS (
                   SELECT 1
                   FROM sys.default_constraints defaultConstraint
                   INNER JOIN sys.columns columnInfo
                       ON columnInfo.object_id = defaultConstraint.parent_object_id
                      AND columnInfo.column_id = defaultConstraint.parent_column_id
                   WHERE defaultConstraint.parent_object_id = OBJECT_ID(N'dbo.Pois')
                     AND columnInfo.name = N'IsActive'
               )
                ALTER TABLE dbo.Pois ADD CONSTRAINT DF_Pois_IsActive DEFAULT ((1)) FOR IsActive;
            """);

        ExecuteNonQuery(
            connection,
            null,
            """
            IF COL_LENGTH(N'dbo.Pois', N'LockedBySuperAdmin') IS NOT NULL
               AND NOT EXISTS (
                   SELECT 1
                   FROM sys.default_constraints defaultConstraint
                   INNER JOIN sys.columns columnInfo
                       ON columnInfo.object_id = defaultConstraint.parent_object_id
                      AND columnInfo.column_id = defaultConstraint.parent_column_id
                   WHERE defaultConstraint.parent_object_id = OBJECT_ID(N'dbo.Pois')
                     AND columnInfo.name = N'LockedBySuperAdmin'
               )
                ALTER TABLE dbo.Pois ADD CONSTRAINT DF_Pois_LockedBySuperAdmin DEFAULT ((0)) FOR LockedBySuperAdmin;
            """);

        ExecuteNonQuery(
            connection,
            null,
            """
            IF OBJECT_ID(N'dbo.PoiTags', N'U') IS NULL
            BEGIN
                CREATE TABLE dbo.PoiTags (
                    PoiId NVARCHAR(50) NOT NULL,
                    TagValue NVARCHAR(100) NOT NULL,
                    PRIMARY KEY (PoiId, TagValue),
                    CONSTRAINT FK_PoiTags_Pois FOREIGN KEY (PoiId) REFERENCES dbo.Pois(Id)
                );
            END;
            """);

        ExecuteNonQuery(
            connection,
            null,
            """
            UPDATE dbo.Pois
            SET IsActive = COALESCE(IsActive, 1),
                LockedBySuperAdmin = COALESCE(LockedBySuperAdmin, 0),
                ApprovedAt = CASE
                    WHEN LOWER(COALESCE(LTRIM(RTRIM([Status])), N'')) = N'published'
                        THEN COALESCE(ApprovedAt, UpdatedAt, CreatedAt)
                    ELSE ApprovedAt
                END,
                RejectionReason = CASE
                    WHEN LOWER(COALESCE(LTRIM(RTRIM([Status])), N'')) = N'rejected'
                        THEN NULLIF(LTRIM(RTRIM(RejectionReason)), N'')
                    ELSE NULL
                END,
                RejectedAt = CASE
                    WHEN LOWER(COALESCE(LTRIM(RTRIM([Status])), N'')) = N'rejected'
                        THEN COALESCE(RejectedAt, UpdatedAt)
                    ELSE NULL
                END;
            """);

        ExecuteNonQuery(
            connection,
            null,
            """
            IF EXISTS (
                SELECT 1
                FROM sys.columns
                WHERE object_id = OBJECT_ID(N'dbo.Pois')
                  AND name = N'IsActive'
                  AND is_nullable = 1
            )
            BEGIN
                ALTER TABLE dbo.Pois ALTER COLUMN IsActive BIT NOT NULL;
            END;
            """);

        ExecuteNonQuery(
            connection,
            null,
            """
            IF EXISTS (
                SELECT 1
                FROM sys.columns
                WHERE object_id = OBJECT_ID(N'dbo.Pois')
                  AND name = N'LockedBySuperAdmin'
                  AND is_nullable = 1
            )
            BEGIN
                ALTER TABLE dbo.Pois ALTER COLUMN LockedBySuperAdmin BIT NOT NULL;
            END;
            """);
    }

    private void EnsureTranslationRuntimeSchema(SqlConnection connection)
    {
        ExecuteNonQuery(
            connection,
            null,
            """
            IF OBJECT_ID(N'dbo.PoiTranslations', N'U') IS NOT NULL
            BEGIN
                IF COL_LENGTH(N'dbo.PoiTranslations', N'SourceLanguageCode') IS NULL
                    ALTER TABLE dbo.PoiTranslations ADD SourceLanguageCode NVARCHAR(20) NULL;

                IF COL_LENGTH(N'dbo.PoiTranslations', N'SourceHash') IS NULL
                    ALTER TABLE dbo.PoiTranslations ADD SourceHash NVARCHAR(128) NULL;

                IF COL_LENGTH(N'dbo.PoiTranslations', N'SourceUpdatedAt') IS NULL
                    ALTER TABLE dbo.PoiTranslations ADD SourceUpdatedAt DATETIMEOFFSET(7) NULL;
            END;
            """);
    }

    private void EnsureCustomerUsersRuntimeSchema(SqlConnection connection)
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

            IF COL_LENGTH(N'dbo.CustomerUsers', N'PreferredLanguage') IS NULL
                ALTER TABLE dbo.CustomerUsers ADD PreferredLanguage NVARCHAR(20) NULL;

            IF COL_LENGTH(N'dbo.CustomerUsers', N'IsPremium') IS NULL
                ALTER TABLE dbo.CustomerUsers ADD IsPremium BIT NULL;

            IF COL_LENGTH(N'dbo.CustomerUsers', N'LastActiveAt') IS NULL
                ALTER TABLE dbo.CustomerUsers ADD LastActiveAt DATETIMEOFFSET(7) NULL;

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
                IsPremium = COALESCE(IsPremium, CAST(0 AS bit)),
                PreferredLanguage = COALESCE(NULLIF(LTRIM(RTRIM(PreferredLanguage)), N''), N'vi')
            WHERE NULLIF(LTRIM(RTRIM(Username)), N'') IS NULL
               OR NULLIF(LTRIM(RTRIM(Country)), N'') IS NULL
               OR NULLIF(LTRIM(RTRIM(PreferredLanguage)), N'') IS NULL
               OR NULLIF(LTRIM(RTRIM([Password])), N'') IS NULL
               OR IsPremium IS NULL;

            IF OBJECT_ID(N'dbo.CustomerFavoritePois', N'U') IS NULL
            BEGIN
                CREATE TABLE dbo.CustomerFavoritePois (
                    CustomerUserId NVARCHAR(50) NOT NULL,
                    PoiId NVARCHAR(50) NOT NULL,
                    PRIMARY KEY (CustomerUserId, PoiId),
                    CONSTRAINT FK_CustomerFavoritePois_CustomerUsers FOREIGN KEY (CustomerUserId) REFERENCES dbo.CustomerUsers(Id),
                    CONSTRAINT FK_CustomerFavoritePois_Pois FOREIGN KEY (PoiId) REFERENCES dbo.Pois(Id)
                );
            END;
            """);
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
                    AccessToken NVARCHAR(200) NOT NULL,
                    RefreshToken NVARCHAR(200) NOT NULL PRIMARY KEY,
                    UserId NVARCHAR(50) NOT NULL,
                    AccessTokenExpiresAt DATETIMEOFFSET(7) NOT NULL,
                    ExpiresAt DATETIMEOFFSET(7) NOT NULL,
                    CONSTRAINT FK_RefreshSessions_AdminUsers FOREIGN KEY (UserId) REFERENCES dbo.AdminUsers(Id)
                );
            END;
            """);

        ExecuteNonQuery(
            connection,
            null,
            """
            IF COL_LENGTH(N'dbo.RefreshSessions', N'AccessToken') IS NULL
                ALTER TABLE dbo.RefreshSessions ADD AccessToken NVARCHAR(200) NULL;

            IF COL_LENGTH(N'dbo.RefreshSessions', N'AccessTokenExpiresAt') IS NULL
                ALTER TABLE dbo.RefreshSessions ADD AccessTokenExpiresAt DATETIMEOFFSET(7) NULL;
            """);

        ExecuteNonQuery(
            connection,
            null,
            """
            UPDATE dbo.RefreshSessions
            SET AccessToken = COALESCE(NULLIF(LTRIM(RTRIM(AccessToken)), N''), CONCAT(N'legacy_access_', REPLACE(CONVERT(NVARCHAR(36), NEWID()), N'-', N''))),
                AccessTokenExpiresAt = COALESCE(AccessTokenExpiresAt, ExpiresAt)
            WHERE AccessToken IS NULL
               OR NULLIF(LTRIM(RTRIM(AccessToken)), N'') IS NULL
               OR AccessTokenExpiresAt IS NULL;
            """);

        ExecuteNonQuery(
            connection,
            null,
            """
            IF EXISTS (
                SELECT 1
                FROM sys.columns
                WHERE object_id = OBJECT_ID(N'dbo.RefreshSessions')
                    AND name = N'AccessToken'
                    AND is_nullable = 1
            )
            BEGIN
                ALTER TABLE dbo.RefreshSessions ALTER COLUMN AccessToken NVARCHAR(200) NOT NULL;
            END;

            IF EXISTS (
                SELECT 1
                FROM sys.columns
                WHERE object_id = OBJECT_ID(N'dbo.RefreshSessions')
                    AND name = N'AccessTokenExpiresAt'
                    AND is_nullable = 1
            )
            BEGIN
                ALTER TABLE dbo.RefreshSessions ALTER COLUMN AccessTokenExpiresAt DATETIMEOFFSET(7) NOT NULL;
            END;
            """);

        ExecuteNonQuery(
            connection,
            null,
            """
            IF NOT EXISTS (
                SELECT 1
                FROM sys.indexes
                WHERE name = N'UX_RefreshSessions_AccessToken'
                    AND object_id = OBJECT_ID(N'dbo.RefreshSessions')
            )
            BEGIN
                CREATE UNIQUE INDEX UX_RefreshSessions_AccessToken
                ON dbo.RefreshSessions (AccessToken);
            END;
            """);
    }

    private void EnsureSeparatedAuditLogSchema(SqlConnection connection)
    {
        ExecuteNonQuery(
            connection,
            null,
            """
            IF OBJECT_ID(N'dbo.AdminAuditLogs', N'U') IS NULL
            BEGIN
                CREATE TABLE dbo.AdminAuditLogs (
                    Id NVARCHAR(50) NOT NULL PRIMARY KEY,
                    ActorId NVARCHAR(50) NOT NULL,
                    ActorName NVARCHAR(120) NOT NULL,
                    ActorRole NVARCHAR(50) NOT NULL,
                    ActorType NVARCHAR(30) NOT NULL,
                    [Action] NVARCHAR(160) NOT NULL,
                    [Module] NVARCHAR(60) NOT NULL,
                    TargetId NVARCHAR(120) NOT NULL,
                    TargetSummary NVARCHAR(300) NOT NULL,
                    BeforeSummary NVARCHAR(MAX) NULL,
                    AfterSummary NVARCHAR(MAX) NULL,
                    SourceApp NVARCHAR(60) NOT NULL,
                    LegacyAuditId NVARCHAR(50) NULL,
                    CreatedAt DATETIMEOFFSET(7) NOT NULL
                );
            END;

            IF OBJECT_ID(N'dbo.UserActivityLogs', N'U') IS NULL
            BEGIN
                CREATE TABLE dbo.UserActivityLogs (
                    Id NVARCHAR(50) NOT NULL PRIMARY KEY,
                    ActorId NVARCHAR(50) NOT NULL,
                    ActorType NVARCHAR(30) NOT NULL,
                    EventType NVARCHAR(160) NOT NULL,
                    Metadata NVARCHAR(MAX) NOT NULL,
                    SourceApp NVARCHAR(60) NOT NULL,
                    LegacyAuditId NVARCHAR(50) NULL,
                    CreatedAt DATETIMEOFFSET(7) NOT NULL
                );
            END;

            IF COL_LENGTH(N'dbo.AdminAuditLogs', N'LegacyAuditId') IS NULL
                ALTER TABLE dbo.AdminAuditLogs ADD LegacyAuditId NVARCHAR(50) NULL;

            IF COL_LENGTH(N'dbo.UserActivityLogs', N'LegacyAuditId') IS NULL
                ALTER TABLE dbo.UserActivityLogs ADD LegacyAuditId NVARCHAR(50) NULL;

            IF NOT EXISTS (
                SELECT 1
                FROM sys.indexes
                WHERE name = N'UX_AdminAuditLogs_LegacyAuditId'
                    AND object_id = OBJECT_ID(N'dbo.AdminAuditLogs')
            )
            BEGIN
                CREATE UNIQUE INDEX UX_AdminAuditLogs_LegacyAuditId
                ON dbo.AdminAuditLogs (LegacyAuditId)
                WHERE LegacyAuditId IS NOT NULL;
            END;

            IF NOT EXISTS (
                SELECT 1
                FROM sys.indexes
                WHERE name = N'UX_UserActivityLogs_LegacyAuditId'
                    AND object_id = OBJECT_ID(N'dbo.UserActivityLogs')
            )
            BEGIN
                CREATE UNIQUE INDEX UX_UserActivityLogs_LegacyAuditId
                ON dbo.UserActivityLogs (LegacyAuditId)
                WHERE LegacyAuditId IS NOT NULL;
            END;
            """);

        ExecuteNonQuery(
            connection,
            null,
            """
            IF OBJECT_ID(N'dbo.AuditLogs', N'U') IS NOT NULL
            BEGIN
                INSERT INTO dbo.AdminAuditLogs (
                    Id, ActorId, ActorName, ActorRole, ActorType, [Action], [Module], TargetId, TargetSummary,
                    BeforeSummary, AfterSummary, SourceApp, LegacyAuditId, CreatedAt
                )
                SELECT
                    legacy.Id,
                    COALESCE(adminUser.Id, legacy.TargetValue, N'legacy-admin'),
                    legacy.ActorName,
                    legacy.ActorRole,
                    N'ADMIN',
                    legacy.[Action],
                    CASE
                        WHEN legacy.[Action] LIKE N'%đăng nhập%' OR legacy.[Action] LIKE N'%phiên đăng nhập%' THEN N'AUTH'
                        WHEN legacy.[Action] LIKE N'%POI%' THEN N'POI'
                        WHEN legacy.[Action] LIKE N'%audio%' THEN N'AUDIO_GUIDE'
                        WHEN legacy.[Action] LIKE N'%thuyết minh%' THEN N'TRANSLATION'
                        WHEN legacy.[Action] LIKE N'%tour%' THEN N'TOUR'
                        WHEN legacy.[Action] LIKE N'%ưu đãi%' THEN N'PROMOTION'
                        WHEN legacy.[Action] LIKE N'%tài khoản admin%' OR legacy.[Action] LIKE N'%chủ quán%' THEN N'ADMIN_USER'
                        WHEN legacy.[Action] LIKE N'%cài đặt%' OR legacy.[Action] LIKE N'%ngôn ngữ premium%' THEN N'SETTINGS'
                        ELSE N'LEGACY'
                    END,
                    legacy.TargetValue,
                    legacy.TargetValue,
                    NULL,
                    NULL,
                    N'ADMIN_WEB',
                    legacy.Id,
                    legacy.CreatedAt
                FROM dbo.AuditLogs legacy
                LEFT JOIN dbo.AdminUsers adminUser
                    ON adminUser.Email = legacy.TargetValue
                WHERE UPPER(COALESCE(legacy.ActorRole, N'')) IN (N'SUPER_ADMIN', N'SUPERADMIN', N'PLACE_OWNER', N'PLACEOWNER', N'SYSTEM')
                  AND NOT EXISTS (
                      SELECT 1
                      FROM dbo.AdminAuditLogs migrated
                      WHERE migrated.LegacyAuditId = legacy.Id
                  );

                INSERT INTO dbo.UserActivityLogs (
                    Id, ActorId, ActorType, EventType, Metadata, SourceApp, LegacyAuditId, CreatedAt
                )
                SELECT
                    legacy.Id,
                    legacy.TargetValue,
                    N'END_USER',
                    legacy.[Action],
                    CONCAT(N'actor=', legacy.ActorName, N'; target=', legacy.TargetValue),
                    N'MOBILE_APP',
                    legacy.Id,
                    legacy.CreatedAt
                FROM dbo.AuditLogs legacy
                WHERE UPPER(COALESCE(legacy.ActorRole, N'')) NOT IN (N'SUPER_ADMIN', N'SUPERADMIN', N'PLACE_OWNER', N'PLACEOWNER', N'SYSTEM')
                  AND NOT EXISTS (
                      SELECT 1
                      FROM dbo.UserActivityLogs migrated
                      WHERE migrated.LegacyAuditId = legacy.Id
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
                    AnalyticsRetentionDays INT NOT NULL
                );
            END;

            IF COL_LENGTH(N'dbo.SystemSettings', N'PremiumUnlockPriceUsd') IS NULL
                ALTER TABLE dbo.SystemSettings ADD PremiumUnlockPriceUsd INT NULL;

            IF COL_LENGTH(N'dbo.SystemSettings', N'TtsProvider') IS NULL
                ALTER TABLE dbo.SystemSettings ADD TtsProvider NVARCHAR(50) NULL;

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
                TtsProvider = COALESCE(NULLIF(LTRIM(RTRIM(TtsProvider)), N''), N'elevenlabs'),
                AnalyticsRetentionDays = COALESCE(AnalyticsRetentionDays, 180)
            WHERE PremiumUnlockPriceUsd IS NULL
               OR PremiumUnlockPriceUsd <= 0
               OR NULLIF(LTRIM(RTRIM(TtsProvider)), N'') IS NULL
               OR AnalyticsRetentionDays IS NULL;

            IF COL_LENGTH(N'dbo.SystemSettings', N'GuestReviewEnabled') IS NOT NULL
            BEGIN
                DECLARE @guestReviewConstraint sysname;

                SELECT TOP 1 @guestReviewConstraint = defaultConstraint.name
                FROM sys.default_constraints defaultConstraint
                INNER JOIN sys.columns columnInfo
                    ON columnInfo.object_id = defaultConstraint.parent_object_id
                   AND columnInfo.column_id = defaultConstraint.parent_column_id
                WHERE defaultConstraint.parent_object_id = OBJECT_ID(N'dbo.SystemSettings')
                  AND columnInfo.name = N'GuestReviewEnabled';

                IF @guestReviewConstraint IS NOT NULL
                BEGIN
                    EXEC(N'ALTER TABLE dbo.SystemSettings DROP CONSTRAINT [' + @guestReviewConstraint + N']');
                END;

                ALTER TABLE dbo.SystemSettings DROP COLUMN GuestReviewEnabled;
            END;

            IF EXISTS (
                SELECT 1
                FROM sys.columns
                WHERE object_id = OBJECT_ID(N'dbo.SystemSettings')
                    AND name = N'TtsProvider'
                    AND is_nullable = 1
            )
            BEGIN
                ALTER TABLE dbo.SystemSettings ALTER COLUMN TtsProvider NVARCHAR(50) NOT NULL;
            END;

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

            IF COL_LENGTH(N'dbo.CustomerUsers', N'PreferredLanguage') IS NULL
                ALTER TABLE dbo.CustomerUsers ADD PreferredLanguage NVARCHAR(20) NULL;

            IF COL_LENGTH(N'dbo.CustomerUsers', N'IsPremium') IS NULL
                ALTER TABLE dbo.CustomerUsers ADD IsPremium BIT NULL;

            IF COL_LENGTH(N'dbo.CustomerUsers', N'LastActiveAt') IS NULL
                ALTER TABLE dbo.CustomerUsers ADD LastActiveAt DATETIMEOFFSET(7) NULL;
            """);

        ExecuteNonQuery(
            connection,
            null,
            """
            IF EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = N'CK_CustomerUsers_DeviceType')
            BEGIN
                ALTER TABLE dbo.CustomerUsers DROP CONSTRAINT CK_CustomerUsers_DeviceType;
            END;

            IF EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = N'CK_CustomerUsers_Status')
            BEGIN
                ALTER TABLE dbo.CustomerUsers DROP CONSTRAINT CK_CustomerUsers_Status;
            END;

            IF EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = N'CK_CustomerUsers_Identity')
            BEGIN
                ALTER TABLE dbo.CustomerUsers DROP CONSTRAINT CK_CustomerUsers_Identity;
            END;

            DECLARE @constraintName sysname;

            IF COL_LENGTH(N'dbo.CustomerUsers', N'Status') IS NOT NULL
            BEGIN
                SELECT TOP 1 @constraintName = dc.name
                FROM sys.default_constraints dc
                INNER JOIN sys.columns c
                    ON c.default_object_id = dc.object_id
                WHERE dc.parent_object_id = OBJECT_ID(N'dbo.CustomerUsers')
                  AND c.name = N'Status';

                IF @constraintName IS NOT NULL
                    EXEC(N'ALTER TABLE dbo.CustomerUsers DROP CONSTRAINT ' + QUOTENAME(@constraintName) + N';');

                ALTER TABLE dbo.CustomerUsers DROP COLUMN [Status];
                SET @constraintName = NULL;
            END;

            IF COL_LENGTH(N'dbo.CustomerUsers', N'IsActive') IS NOT NULL
            BEGIN
                SELECT TOP 1 @constraintName = dc.name
                FROM sys.default_constraints dc
                INNER JOIN sys.columns c
                    ON c.default_object_id = dc.object_id
                WHERE dc.parent_object_id = OBJECT_ID(N'dbo.CustomerUsers')
                  AND c.name = N'IsActive';

                IF @constraintName IS NOT NULL
                    EXEC(N'ALTER TABLE dbo.CustomerUsers DROP CONSTRAINT ' + QUOTENAME(@constraintName) + N';');

                ALTER TABLE dbo.CustomerUsers DROP COLUMN IsActive;
                SET @constraintName = NULL;
            END;

            IF COL_LENGTH(N'dbo.CustomerUsers', N'IsBanned') IS NOT NULL
            BEGIN
                SELECT TOP 1 @constraintName = dc.name
                FROM sys.default_constraints dc
                INNER JOIN sys.columns c
                    ON c.default_object_id = dc.object_id
                WHERE dc.parent_object_id = OBJECT_ID(N'dbo.CustomerUsers')
                  AND c.name = N'IsBanned';

                IF @constraintName IS NOT NULL
                    EXEC(N'ALTER TABLE dbo.CustomerUsers DROP CONSTRAINT ' + QUOTENAME(@constraintName) + N';');

                ALTER TABLE dbo.CustomerUsers DROP COLUMN IsBanned;
                SET @constraintName = NULL;
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
                IsPremium = COALESCE(IsPremium, CAST(0 AS bit)),
                PreferredLanguage = COALESCE(NULLIF(LTRIM(RTRIM(PreferredLanguage)), N''), N'vi')
            WHERE NULLIF(LTRIM(RTRIM(Username)), N'') IS NULL
               OR NULLIF(LTRIM(RTRIM(Country)), N'') IS NULL
               OR NULLIF(LTRIM(RTRIM(PreferredLanguage)), N'') IS NULL
               OR NULLIF(LTRIM(RTRIM([Password])), N'') IS NULL
               OR IsPremium IS NULL;

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

            IF EXISTS (
                SELECT 1
                FROM sys.columns
                WHERE object_id = OBJECT_ID(N'dbo.CustomerUsers')
                    AND name = N'PreferredLanguage'
                    AND is_nullable = 1
            )
            BEGIN
                ALTER TABLE dbo.CustomerUsers ALTER COLUMN PreferredLanguage NVARCHAR(20) NOT NULL;
            END;

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
                    [Description] NVARCHAR(MAX) NOT NULL,
                    IsFeatured BIT NOT NULL,
                    IsActive BIT NOT NULL,
                    IsSystemRoute BIT NOT NULL,
                    OwnerUserId NVARCHAR(50) NULL,
                    UpdatedBy NVARCHAR(120) NOT NULL,
                    UpdatedAt DATETIMEOFFSET(7) NOT NULL
                );
            END;

            IF COL_LENGTH(N'dbo.Routes', N'Theme') IS NULL
                ALTER TABLE dbo.Routes ADD Theme NVARCHAR(100) NULL;

            IF COL_LENGTH(N'dbo.Routes', N'DurationMinutes') IS NULL
                ALTER TABLE dbo.Routes ADD DurationMinutes INT NULL;

            IF COL_LENGTH(N'dbo.Routes', N'CoverImageUrl') IS NULL
                ALTER TABLE dbo.Routes ADD CoverImageUrl NVARCHAR(500) NULL;

            IF COL_LENGTH(N'dbo.Routes', N'Difficulty') IS NULL
                ALTER TABLE dbo.Routes ADD Difficulty NVARCHAR(30) NULL;

            IF COL_LENGTH(N'dbo.Routes', N'IsFeatured') IS NULL
                ALTER TABLE dbo.Routes ADD IsFeatured BIT NULL;

            IF COL_LENGTH(N'dbo.Routes', N'IsActive') IS NULL
                ALTER TABLE dbo.Routes ADD IsActive BIT NULL;

            IF COL_LENGTH(N'dbo.Routes', N'IsSystemRoute') IS NULL
                ALTER TABLE dbo.Routes ADD IsSystemRoute BIT NULL;

            IF COL_LENGTH(N'dbo.Routes', N'OwnerUserId') IS NULL
                ALTER TABLE dbo.Routes ADD OwnerUserId NVARCHAR(50) NULL;

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
                IsSystemRoute = COALESCE(IsSystemRoute, CAST(1 AS bit)),
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
                    AND name = N'IsSystemRoute'
                    AND is_nullable = 1
            )
            BEGIN
                ALTER TABLE dbo.Routes ALTER COLUMN IsSystemRoute BIT NOT NULL;
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

            IF NOT EXISTS (
                SELECT 1
                FROM sys.foreign_keys
                WHERE name = N'FK_Routes_AdminUsers_OwnerUserId'
            )
            BEGIN
                ALTER TABLE dbo.Routes
                ADD CONSTRAINT FK_Routes_AdminUsers_OwnerUserId
                FOREIGN KEY (OwnerUserId) REFERENCES dbo.AdminUsers(Id);
            END;
            """);

        ExecuteNonQuery(
            connection,
            null,
            """
            IF COL_LENGTH(N'dbo.Routes', N'Theme') IS NOT NULL
                ALTER TABLE dbo.Routes DROP COLUMN Theme;

            IF COL_LENGTH(N'dbo.Routes', N'DurationMinutes') IS NOT NULL
                ALTER TABLE dbo.Routes DROP COLUMN DurationMinutes;

            IF COL_LENGTH(N'dbo.Routes', N'CoverImageUrl') IS NOT NULL
                ALTER TABLE dbo.Routes DROP COLUMN CoverImageUrl;

            IF COL_LENGTH(N'dbo.Routes', N'Difficulty') IS NOT NULL
                ALTER TABLE dbo.Routes DROP COLUMN Difficulty;
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

    private IReadOnlyList<AdminUser> ApplyAdminUserScope(
        IReadOnlyList<AdminUser> users,
        AdminRequestContext actor) =>
        actor.IsSuperAdmin
            ? users
            : users.Where(user => string.Equals(user.Id, actor.UserId, StringComparison.OrdinalIgnoreCase)).ToList();

    private IReadOnlyList<Poi> ApplyPoiScope(
        SqlConnection connection,
        SqlTransaction? transaction,
        IReadOnlyList<Poi> pois,
        AdminRequestContext? actor)
    {
        if (actor is null)
        {
            return ApplyPublicPoiScope(pois);
        }

        if (actor.IsSuperAdmin)
        {
            return pois;
        }

        var ownerPoiIds = GetOwnerPoiIds(connection, transaction, actor.UserId);
        return pois.Where(poi => ownerPoiIds.Contains(poi.Id)).ToList();
    }

    private static IReadOnlyList<Poi> ApplyPublicPoiScope(IReadOnlyList<Poi> pois) =>
        pois
            .Where(poi =>
                string.Equals(poi.Status, "published", StringComparison.OrdinalIgnoreCase) &&
                poi.IsActive)
            .ToList();

    private IReadOnlyList<FoodItem> ApplyFoodItemScope(
        SqlConnection connection,
        SqlTransaction? transaction,
        IReadOnlyList<FoodItem> foodItems,
        AdminRequestContext? actor,
        IReadOnlyList<Poi> scopedPois)
    {
        if (actor is not null && actor.IsSuperAdmin)
        {
            return foodItems;
        }

        var visiblePoiIds = scopedPois.Select(poi => poi.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        return foodItems.Where(item => visiblePoiIds.Contains(item.PoiId)).ToList();
    }

    private IReadOnlyList<TourRoute> ApplyRouteScope(
        SqlConnection connection,
        SqlTransaction? transaction,
        IReadOnlyList<TourRoute> routes,
        AdminRequestContext? actor)
    {
        if (actor is null)
        {
            return ApplyPublicRouteScope(routes, ApplyPublicPoiScope(GetPois(connection, transaction)));
        }

        if (actor.IsSuperAdmin)
        {
            return routes;
        }

        return [];
    }

    private static bool IsRouteEntityType(string? entityType) =>
        string.Equals(entityType?.Trim(), "route", StringComparison.OrdinalIgnoreCase);

    private static IReadOnlyList<TourRoute> ApplyPublicRouteScope(
        IReadOnlyList<TourRoute> routes,
        IReadOnlyList<Poi> visiblePois)
    {
        var visiblePoiIds = visiblePois.Select(poi => poi.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        return routes
            .Where(route =>
                route.IsActive &&
                route.IsSystemRoute &&
                route.StopPoiIds.Any(visiblePoiIds.Contains))
            .ToList();
    }

    private IReadOnlyList<Promotion> ApplyPromotionScope(
        SqlConnection connection,
        SqlTransaction? transaction,
        IReadOnlyList<Promotion> promotions,
        AdminRequestContext? actor)
    {
        if (actor is null)
        {
            return ApplyPublicPromotionScope(promotions, ApplyPublicPoiScope(GetPois(connection, transaction)));
        }

        if (actor.IsSuperAdmin)
        {
            return [];
        }

        var ownerPoiIds = GetOwnerPoiIds(connection, transaction, actor.UserId);
        return promotions.Where(promotion => ownerPoiIds.Contains(promotion.PoiId)).ToList();
    }

    private static IReadOnlyList<Promotion> ApplyPublicPromotionScope(
        IReadOnlyList<Promotion> promotions,
        IReadOnlyList<Poi> visiblePois)
    {
        var visiblePoiIds = visiblePois.Select(poi => poi.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        return promotions
            .Where(promotion =>
                visiblePoiIds.Contains(promotion.PoiId) &&
                !string.Equals(promotion.Status, "expired", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(promotion.Status, "deleted", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(promotion.Status, "hidden", StringComparison.OrdinalIgnoreCase))
            .ToList();
    }

    private IReadOnlyList<ViewLog> ApplyViewLogScope(
        SqlConnection _connection,
        SqlTransaction? _transaction,
        IReadOnlyList<ViewLog> viewLogs,
        AdminRequestContext actor,
        IReadOnlyList<Poi> scopedPois)
    {
        if (actor.IsSuperAdmin)
        {
            return viewLogs;
        }

        var visiblePoiIds = scopedPois.Select(poi => poi.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        return viewLogs.Where(log => visiblePoiIds.Contains(log.PoiId)).ToList();
    }

    private IReadOnlyList<AudioListenLog> ApplyAudioListenLogScope(
        SqlConnection _connection,
        SqlTransaction? _transaction,
        IReadOnlyList<AudioListenLog> audioListenLogs,
        AdminRequestContext actor,
        IReadOnlyList<Poi> scopedPois)
    {
        if (actor.IsSuperAdmin)
        {
            return audioListenLogs;
        }

        var visiblePoiIds = scopedPois.Select(poi => poi.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        return audioListenLogs.Where(log => visiblePoiIds.Contains(log.PoiId)).ToList();
    }

    private IReadOnlyList<AppUsageEvent> ApplyUsageEventScope(
        IReadOnlyList<AppUsageEvent> usageEvents,
        AdminRequestContext actor,
        IReadOnlyList<Poi> scopedPois)
    {
        if (actor.IsSuperAdmin)
        {
            return usageEvents;
        }

        var visiblePoiIds = scopedPois.Select(poi => poi.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        return usageEvents
            .Where(item => string.IsNullOrWhiteSpace(item.PoiId) || visiblePoiIds.Contains(item.PoiId))
            .ToList();
    }

    private IReadOnlyList<Translation> ApplyTranslationScope(
        SqlConnection connection,
        SqlTransaction? transaction,
        IReadOnlyList<Translation> translations,
        AdminRequestContext actor,
        IReadOnlyList<Poi> scopedPois,
        IReadOnlyList<FoodItem> scopedFoodItems,
        IReadOnlyList<TourRoute> scopedRoutes,
        IReadOnlyList<Promotion> scopedPromotions)
    {
        if (actor.IsSuperAdmin)
        {
            return translations;
        }

        var visiblePoiIds = scopedPois.Select(poi => poi.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var visibleFoodItemIds = scopedFoodItems.Select(item => item.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var visibleRouteIds = scopedRoutes.Select(route => route.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var visiblePromotionIds = scopedPromotions.Select(promotion => promotion.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);

        return translations
            .Where(translation =>
                (string.Equals(translation.EntityType, "poi", StringComparison.OrdinalIgnoreCase) &&
                 visiblePoiIds.Contains(translation.EntityId)) ||
                (string.Equals(translation.EntityType, "food_item", StringComparison.OrdinalIgnoreCase) &&
                 visibleFoodItemIds.Contains(translation.EntityId)) ||
                (string.Equals(translation.EntityType, "route", StringComparison.OrdinalIgnoreCase) &&
                 visibleRouteIds.Contains(translation.EntityId)) ||
                (string.Equals(translation.EntityType, "promotion", StringComparison.OrdinalIgnoreCase) &&
                 visiblePromotionIds.Contains(translation.EntityId)))
            .ToList();
    }

    private IReadOnlyList<AudioGuide> ApplyAudioGuideScope(
        SqlConnection connection,
        SqlTransaction? transaction,
        IReadOnlyList<AudioGuide> audioGuides,
        AdminRequestContext actor,
        IReadOnlyList<Poi> scopedPois,
        IReadOnlyList<FoodItem> scopedFoodItems,
        IReadOnlyList<TourRoute> scopedRoutes)
    {
        if (actor.IsSuperAdmin)
        {
            return audioGuides;
        }

        var visiblePoiIds = scopedPois.Select(poi => poi.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var visibleFoodItemIds = scopedFoodItems.Select(item => item.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var visibleRouteIds = scopedRoutes.Select(route => route.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);

        return audioGuides
            .Where(audioGuide =>
                (string.Equals(audioGuide.EntityType, "poi", StringComparison.OrdinalIgnoreCase) &&
                 visiblePoiIds.Contains(audioGuide.EntityId)) ||
                (string.Equals(audioGuide.EntityType, "food_item", StringComparison.OrdinalIgnoreCase) &&
                 visibleFoodItemIds.Contains(audioGuide.EntityId)) ||
                (string.Equals(audioGuide.EntityType, "route", StringComparison.OrdinalIgnoreCase) &&
                 visibleRouteIds.Contains(audioGuide.EntityId)))
            .ToList();
    }

    private IReadOnlyList<MediaAsset> ApplyMediaAssetScope(
        SqlConnection connection,
        SqlTransaction? transaction,
        IReadOnlyList<MediaAsset> mediaAssets,
        AdminRequestContext actor,
        IReadOnlyList<Poi> scopedPois,
        IReadOnlyList<FoodItem> scopedFoodItems,
        IReadOnlyList<TourRoute> scopedRoutes,
        IReadOnlyList<Promotion> scopedPromotions)
    {
        if (actor.IsSuperAdmin)
        {
            return mediaAssets;
        }

        var visiblePoiIds = scopedPois.Select(poi => poi.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var visibleFoodItemIds = scopedFoodItems.Select(item => item.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var visibleRouteIds = scopedRoutes.Select(route => route.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var visiblePromotionIds = scopedPromotions.Select(promotion => promotion.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);

        return mediaAssets
            .Where(asset =>
                (string.Equals(asset.EntityType, "poi", StringComparison.OrdinalIgnoreCase) &&
                 visiblePoiIds.Contains(asset.EntityId)) ||
                (string.Equals(asset.EntityType, "food_item", StringComparison.OrdinalIgnoreCase) &&
                 visibleFoodItemIds.Contains(asset.EntityId)) ||
                (string.Equals(asset.EntityType, "route", StringComparison.OrdinalIgnoreCase) &&
                 visibleRouteIds.Contains(asset.EntityId)) ||
                (string.Equals(asset.EntityType, "promotion", StringComparison.OrdinalIgnoreCase) &&
                 visiblePromotionIds.Contains(asset.EntityId)))
            .ToList();
    }

    private IReadOnlyList<Translation> ApplyPublicTranslationScope(
        IReadOnlyList<Translation> translations,
        IReadOnlyList<Poi> visiblePois,
        IReadOnlyList<FoodItem> visibleFoodItems,
        IReadOnlyList<TourRoute> visibleRoutes,
        IReadOnlyList<Promotion> visiblePromotions,
        IReadOnlyCollection<string> allowedLanguages)
    {
        var visiblePoiIds = visiblePois.Select(poi => poi.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var visibleFoodItemIds = visibleFoodItems.Select(item => item.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var visibleRouteIds = visibleRoutes.Select(route => route.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var visiblePromotionIds = visiblePromotions.Select(promotion => promotion.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);

        return translations
            .Where(translation =>
                allowedLanguages.Contains(PremiumAccessCatalog.NormalizeLanguageCode(translation.LanguageCode)) &&
                (
                    (string.Equals(translation.EntityType, "poi", StringComparison.OrdinalIgnoreCase) &&
                     visiblePoiIds.Contains(translation.EntityId)) ||
                    (string.Equals(translation.EntityType, "food_item", StringComparison.OrdinalIgnoreCase) &&
                     visibleFoodItemIds.Contains(translation.EntityId)) ||
                    (string.Equals(translation.EntityType, "route", StringComparison.OrdinalIgnoreCase) &&
                     visibleRouteIds.Contains(translation.EntityId)) ||
                    (string.Equals(translation.EntityType, "promotion", StringComparison.OrdinalIgnoreCase) &&
                     visiblePromotionIds.Contains(translation.EntityId))
                ))
            .ToList();
    }

    private IReadOnlyList<AudioGuide> ApplyPublicAudioGuideScope(
        IReadOnlyList<AudioGuide> audioGuides,
        IReadOnlyList<Poi> visiblePois,
        IReadOnlyList<FoodItem> visibleFoodItems,
        IReadOnlyList<TourRoute> visibleRoutes,
        IReadOnlyCollection<string> allowedLanguages)
    {
        var visiblePoiIds = visiblePois.Select(poi => poi.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var visibleFoodItemIds = visibleFoodItems.Select(item => item.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var visibleRouteIds = visibleRoutes.Select(route => route.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);

        return audioGuides
            .Where(audioGuide =>
                allowedLanguages.Contains(PremiumAccessCatalog.NormalizeLanguageCode(audioGuide.LanguageCode)) &&
                (
                    (string.Equals(audioGuide.EntityType, "poi", StringComparison.OrdinalIgnoreCase) &&
                     visiblePoiIds.Contains(audioGuide.EntityId)) ||
                    (string.Equals(audioGuide.EntityType, "food_item", StringComparison.OrdinalIgnoreCase) &&
                     visibleFoodItemIds.Contains(audioGuide.EntityId)) ||
                    (string.Equals(audioGuide.EntityType, "route", StringComparison.OrdinalIgnoreCase) &&
                     visibleRouteIds.Contains(audioGuide.EntityId))
                ))
            .ToList();
    }

    private IReadOnlyList<MediaAsset> ApplyPublicMediaAssetScope(
        IReadOnlyList<MediaAsset> mediaAssets,
        IReadOnlyList<Poi> visiblePois,
        IReadOnlyList<FoodItem> visibleFoodItems,
        IReadOnlyList<TourRoute> visibleRoutes,
        IReadOnlyList<Promotion> visiblePromotions)
    {
        var visiblePoiIds = visiblePois.Select(poi => poi.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var visibleFoodItemIds = visibleFoodItems.Select(item => item.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var visibleRouteIds = visibleRoutes.Select(route => route.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var visiblePromotionIds = visiblePromotions.Select(promotion => promotion.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);

        return mediaAssets
            .Where(asset =>
                (string.Equals(asset.EntityType, "poi", StringComparison.OrdinalIgnoreCase) &&
                 visiblePoiIds.Contains(asset.EntityId)) ||
                (string.Equals(asset.EntityType, "food_item", StringComparison.OrdinalIgnoreCase) &&
                 visibleFoodItemIds.Contains(asset.EntityId)) ||
                (string.Equals(asset.EntityType, "route", StringComparison.OrdinalIgnoreCase) &&
                 visibleRouteIds.Contains(asset.EntityId)) ||
                (string.Equals(asset.EntityType, "promotion", StringComparison.OrdinalIgnoreCase) &&
                 visiblePromotionIds.Contains(asset.EntityId)))
            .ToList();
    }

    private IReadOnlyList<AuditLog> ApplyAuditScope(
        SqlConnection connection,
        SqlTransaction? transaction,
        IReadOnlyList<AuditLog> auditLogs,
        AdminRequestContext actor)
    {
        if (actor.IsSuperAdmin)
        {
            return auditLogs;
        }

        var ownerPois = ApplyPoiScope(connection, transaction, GetPois(connection, transaction), actor);
        var ownerPoiIds = ownerPois.Select(poi => poi.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var ownerFoodItemIds = ApplyFoodItemScope(connection, transaction, GetFoodItems(connection, transaction), actor, ownerPois)
            .Select(item => item.Id)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var ownerRouteIds = ApplyRouteScope(connection, transaction, GetRoutes(connection, transaction), actor)
            .Select(route => route.Id)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var ownerPromotionIds = ApplyPromotionScope(connection, transaction, GetPromotions(connection, transaction), actor)
            .Select(promotion => promotion.Id)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        return auditLogs
            .Where(log =>
                string.Equals(log.ActorId, actor.UserId, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(log.TargetId, actor.UserId, StringComparison.OrdinalIgnoreCase) ||
                (string.Equals(log.Module, "POI", StringComparison.OrdinalIgnoreCase) &&
                 ownerPoiIds.Contains(log.TargetId)) ||
                (string.Equals(log.Module, "FOOD_ITEM", StringComparison.OrdinalIgnoreCase) &&
                 ownerFoodItemIds.Contains(log.TargetId)) ||
                (string.Equals(log.Module, "TRANSLATION", StringComparison.OrdinalIgnoreCase) &&
                 (ownerPoiIds.Contains(log.TargetId) || ownerFoodItemIds.Contains(log.TargetId) || ownerRouteIds.Contains(log.TargetId))) ||
                (string.Equals(log.Module, "AUDIO_GUIDE", StringComparison.OrdinalIgnoreCase) &&
                 (ownerPoiIds.Contains(log.TargetId) || ownerFoodItemIds.Contains(log.TargetId) || ownerRouteIds.Contains(log.TargetId))) ||
                (string.Equals(log.Module, "MEDIA", StringComparison.OrdinalIgnoreCase) &&
                 (ownerPoiIds.Contains(log.TargetId) || ownerFoodItemIds.Contains(log.TargetId) || ownerRouteIds.Contains(log.TargetId))) ||
                (string.Equals(log.Module, "TOUR", StringComparison.OrdinalIgnoreCase) &&
                 ownerRouteIds.Contains(log.TargetId)) ||
                (string.Equals(log.Module, "PROMOTION", StringComparison.OrdinalIgnoreCase) &&
                 ownerPromotionIds.Contains(log.TargetId)))
            .ToList();
    }

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

    private void RepairModeratedPoiLocalizedAssetTimestamps(SqlConnection connection)
    {
        var repairedTranslations = ExecuteNonQuery(
            connection,
            null,
            """
            IF OBJECT_ID(N'dbo.PoiTranslations', N'U') IS NOT NULL
               AND OBJECT_ID(N'dbo.Pois', N'U') IS NOT NULL
            BEGIN
                UPDATE translation
                SET UpdatedAt = poi.UpdatedAt
                FROM dbo.PoiTranslations translation
                INNER JOIN dbo.Pois poi
                    ON poi.Id = translation.EntityId
                WHERE translation.EntityType IN (N'poi', N'place')
                  AND (
                        translation.UpdatedAt IS NULL OR
                        translation.UpdatedAt < poi.UpdatedAt
                  )
                  AND (
                        (
                            LOWER(COALESCE(LTRIM(RTRIM(poi.[Status])), N'')) IN (N'published', N'draft')
                            AND poi.ApprovedAt IS NOT NULL
                        ) OR LOWER(COALESCE(LTRIM(RTRIM(poi.[Status])), N'')) = N'rejected'
                  );
            END;
            """);

        var repairedAudioGuides = ExecuteNonQuery(
            connection,
            null,
            """
            IF OBJECT_ID(N'dbo.AudioGuides', N'U') IS NOT NULL
               AND OBJECT_ID(N'dbo.Pois', N'U') IS NOT NULL
            BEGIN
                UPDATE audioGuide
                SET UpdatedAt = poi.UpdatedAt
                FROM dbo.AudioGuides audioGuide
                INNER JOIN dbo.Pois poi
                    ON poi.Id = audioGuide.EntityId
                WHERE audioGuide.EntityType IN (N'poi', N'place')
                  AND (
                        audioGuide.UpdatedAt IS NULL OR
                        audioGuide.UpdatedAt < poi.UpdatedAt
                  )
                  AND (
                        (
                            LOWER(COALESCE(LTRIM(RTRIM(poi.[Status])), N'')) IN (N'published', N'draft')
                            AND poi.ApprovedAt IS NOT NULL
                        ) OR LOWER(COALESCE(LTRIM(RTRIM(poi.[Status])), N'')) = N'rejected'
                  );
            END;
            """);

        if (repairedTranslations > 0 || repairedAudioGuides > 0)
        {
            _logger.LogInformation(
                "Repaired moderated POI localized asset timestamps. translationsUpdated={TranslationsUpdated}, audioUpdated={AudioUpdated}",
                repairedTranslations,
                repairedAudioGuides);
        }
    }
}
