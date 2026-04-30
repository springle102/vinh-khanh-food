using Microsoft.AspNetCore.Http;
using Microsoft.Data.SqlClient;
using System.Globalization;
using System.Linq;
using VinhKhanh.BackendApi.Contracts;
using VinhKhanh.BackendApi.Models;
using VinhKhanh.Core.Mobile;

namespace VinhKhanh.BackendApi.Infrastructure;

public sealed partial class AdminDataRepository
{
    private const int MaxAuditLogs = 120;
    private const int MaxUserActivityLogs = 500;
    private const string DefaultDevelopmentAdminUserId = "dev-super-admin";
    private const string DefaultDevelopmentAdminName = "Development Super Admin";
    private const string DefaultDevelopmentAdminEmail = "dev-admin@localhost";
    private const string DefaultDevelopmentAdminPassword = "DevOnly!123";
    private const string DefaultDevelopmentAdminAvatarColor = "#f97316";
    private readonly string _connectionString;
    private readonly string _seedSqlServerPath;
    private readonly bool _allowCreateDatabase;
    private readonly bool _allowSeedDatabase;
    private readonly bool _allowSchemaUpdates;
    private readonly bool _isDevelopment;
    private readonly ILogger<AdminDataRepository> _logger;
    private readonly object _databaseInitializationGate = new();
    private bool _databaseInitialized;
    private DateTimeOffset? _lastInitializationFailureAtUtc;
    private string? _lastInitializationFailureMessage;
    private static readonly TimeSpan DatabaseInitializationRetryDelay = TimeSpan.FromSeconds(5);

    public AdminDataRepository(
        IConfiguration configuration,
        IWebHostEnvironment environment,
        ILogger<AdminDataRepository> logger)
    {
        _logger = logger;
        _isDevelopment = environment.IsDevelopment();
        _connectionString = ResolveConnectionString(configuration);
        _seedSqlServerPath = ResolveSeedSqlPath(configuration, environment);
        _allowCreateDatabase = ResolveDatabaseInitializationFlag(configuration, "AllowCreateDatabase");
        _allowSeedDatabase = ResolveDatabaseInitializationFlag(configuration, "AllowSeedDatabase");
        _allowSchemaUpdates = ResolveDatabaseInitializationFlag(configuration, "AllowSchemaUpdates");
    }

    private void EnsureDatabaseInitialized()
    {
        if (_databaseInitialized)
        {
            return;
        }

        lock (_databaseInitializationGate)
        {
            if (_databaseInitialized)
            {
                return;
            }

            var now = DateTimeOffset.UtcNow;
            if (_lastInitializationFailureAtUtc is { } lastFailureAtUtc &&
                now - lastFailureAtUtc < DatabaseInitializationRetryDelay &&
                !string.IsNullOrWhiteSpace(_lastInitializationFailureMessage))
            {
                throw new ApiRequestException(
                    StatusCodes.Status503ServiceUnavailable,
                    _lastInitializationFailureMessage);
            }

            try
            {
                InitializeDatabase();
                _databaseInitialized = true;
                _lastInitializationFailureAtUtc = null;
                _lastInitializationFailureMessage = null;
            }
            catch (Exception exception)
            {
                var failureMessage = string.IsNullOrWhiteSpace(exception.Message)
                    ? "Backend chưa thể kết nối cơ sở dữ liệu. Vui lòng kiểm tra SQL Server rồi thử lại."
                    : exception.Message;

                _lastInitializationFailureAtUtc = now;
                _lastInitializationFailureMessage = failureMessage;
                _logger.LogError(
                    exception,
                    "Database initialization failed. The API will stay online and retry after {RetryDelaySeconds}s.",
                    DatabaseInitializationRetryDelay.TotalSeconds);

                throw new ApiRequestException(
                    StatusCodes.Status503ServiceUnavailable,
                    failureMessage);
            }
        }
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

        return BuildSourceTranslations(GetSettings(connection, null), pois, foodItems, routes, promotions);
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
        AdminRequestContext? admin = null)
    {
        using var connection = OpenConnection();

        var users = admin is null ? [] : GetUsers(connection, null);
        var categories = GetCategories(connection, null);
        var pois = GetPois(connection, null);
        IReadOnlyList<Translation> translations = [];
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
            translations = BuildSourceTranslations(settings, pois, foodItems, routes, promotions);
            audioGuides = ApplyAudioGuideScope(connection, null, audioGuides, admin, pois, foodItems, routes);
            mediaAssets = ApplyMediaAssetScope(connection, null, mediaAssets, admin, pois, foodItems, routes, promotions);
            auditLogs = admin.IsSuperAdmin
                ? ApplyAuditScope(connection, null, auditLogs, admin)
                : [];
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
            translations = BuildSourceTranslations(settings, pois, foodItems, routes, promotions);
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

    private static IReadOnlyList<Translation> BuildSourceTranslations(
        SystemSetting settings,
        IReadOnlyList<Poi> pois,
        IReadOnlyList<FoodItem> foodItems,
        IReadOnlyList<TourRoute> routes,
        IReadOnlyList<Promotion> promotions)
    {
        var sourceLanguageCode = PremiumAccessCatalog.NormalizeLanguageCode(settings.DefaultLanguage);
        if (string.IsNullOrWhiteSpace(sourceLanguageCode))
        {
            sourceLanguageCode = "vi";
        }

        var translations = new List<Translation>();

        foreach (var poi in pois)
        {
            var title = string.IsNullOrWhiteSpace(poi.Title) ? poi.Slug : poi.Title;
            var fullText = !string.IsNullOrWhiteSpace(poi.AudioScript)
                ? poi.AudioScript
                : poi.Description;
            translations.Add(CreateSourceTranslation(
                "poi",
                poi.Id,
                poi.SourceLanguageCode,
                title,
                poi.ShortDescription,
                fullText,
                poi.UpdatedBy,
                poi.UpdatedAt));
        }

        foreach (var foodItem in foodItems)
        {
            translations.Add(CreateSourceTranslation(
                "food_item",
                foodItem.Id,
                sourceLanguageCode,
                foodItem.Name,
                string.Empty,
                foodItem.Description,
                "source",
                DateTimeOffset.MinValue));
        }

        foreach (var route in routes)
        {
            translations.Add(CreateSourceTranslation(
                "route",
                route.Id,
                sourceLanguageCode,
                route.Name,
                route.Theme,
                route.Description,
                route.UpdatedBy,
                route.UpdatedAt));
        }

        foreach (var promotion in promotions)
        {
            translations.Add(CreateSourceTranslation(
                "promotion",
                promotion.Id,
                sourceLanguageCode,
                promotion.Title,
                string.Empty,
                promotion.Description,
                "source",
                DateTimeOffset.MinValue));
        }

        return translations;
    }

    private static Translation CreateSourceTranslation(
        string entityType,
        string entityId,
        string languageCode,
        string title,
        string shortText,
        string fullText,
        string updatedBy,
        DateTimeOffset updatedAt)
    {
        var normalizedLanguageCode = PremiumAccessCatalog.NormalizeLanguageCode(languageCode);
        if (string.IsNullOrWhiteSpace(normalizedLanguageCode))
        {
            normalizedLanguageCode = "vi";
        }

        return new Translation
        {
            Id = $"source-{entityType}-{entityId}-{normalizedLanguageCode}",
            EntityType = entityType,
            EntityId = entityId,
            LanguageCode = normalizedLanguageCode,
            Title = title,
            ShortText = shortText,
            FullText = fullText,
            SeoTitle = title,
            SeoDescription = string.IsNullOrWhiteSpace(shortText) ? fullText : shortText,
            SourceLanguageCode = normalizedLanguageCode,
            SourceHash = TranslationSourceVersioning.CreateSourceHashForRuntime(title, shortText, fullText, normalizedLanguageCode),
            SourceUpdatedAt = updatedAt == DateTimeOffset.MinValue ? null : updatedAt,
            UpdatedBy = string.IsNullOrWhiteSpace(updatedBy) ? "source" : updatedBy,
            UpdatedAt = updatedAt == DateTimeOffset.MinValue ? DateTimeOffset.UtcNow : updatedAt
        };
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
            .Select(group => AudioGuideCatalog.SelectCanonical(group)!)
            .OrderByDescending(item => item.UpdatedAt)
            .ThenByDescending(item => item.Id, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static IReadOnlyList<AudioGuide> FilterOutdatedEntityAudioGuides(
        IReadOnlyList<AudioGuide> audioGuides,
        IReadOnlyList<Poi> pois,
        IReadOnlyList<TourRoute> routes)
    {
        // Audio invalidation is tracked explicitly in AudioGuides via IsOutdated/GenerationStatus.
        // Hiding rows by parent UpdatedAt makes admin troubleshooting unreliable after unrelated POI edits.
        return audioGuides;
    }

    public DashboardSummaryResponse GetDashboardSummary(AdminRequestContext actor)
    {
        using var connection = OpenConnection();
        var settings = GetSettings(connection, null);
        var pois = ApplyPoiScope(connection, null, GetPois(connection, null), actor);
        var routes = ApplyRouteScope(connection, null, GetRoutes(connection, null), actor);
        var promotions = ApplyPromotionScope(connection, null, GetPromotions(connection, null), actor);
        var usageEvents = ApplyUsageEventScope(GetAppUsageEvents(connection, null), actor, pois);
        var totalPoiViews = SumUsageEventMetric(
            usageEvents,
            item => string.Equals(item.EventType, MobileUsageEventTypes.PoiView, StringComparison.OrdinalIgnoreCase),
            AnalyticsMetricWeights.PoiView);
        var totalAudioPlays = SumUsageEventMetric(
            usageEvents,
            item => string.Equals(item.EventType, MobileUsageEventTypes.AudioPlay, StringComparison.OrdinalIgnoreCase),
            AnalyticsMetricWeights.AudioListen);
        var totalQrScans = SumUsageEventMetric(
            usageEvents,
            IsTrackedApkDownloadAccessEvent,
            AnalyticsMetricWeights.QrScan);
        var totalOfferViews = SumUsageEventMetric(
            usageEvents,
            item => string.Equals(item.EventType, MobileUsageEventTypes.OfferView, StringComparison.OrdinalIgnoreCase),
            AnalyticsMetricWeights.OfferView);
        var totalPois = SumMetricWeights(pois, AnalyticsMetricWeights.TotalPoi);
        var totalTours = SumMetricWeights(routes, AnalyticsMetricWeights.TotalTour);
        var totalOffers = SumMetricWeights(promotions, AnalyticsMetricWeights.TotalOffer);
        var onlineUsers = GetOnlineUsersCount(connection, null, DateTimeOffset.UtcNow);
        var poiViewCounts = usageEvents
            .Where(item =>
                string.Equals(item.EventType, MobileUsageEventTypes.PoiView, StringComparison.OrdinalIgnoreCase) &&
                !string.IsNullOrWhiteSpace(item.PoiId))
            .GroupBy(item => item.PoiId!, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => SumMetricWeights(group, AnalyticsMetricWeights.PoiView),
                StringComparer.OrdinalIgnoreCase);
        var audioPlayCountsByLanguage = usageEvents
            .Where(item => string.Equals(item.EventType, MobileUsageEventTypes.AudioPlay, StringComparison.OrdinalIgnoreCase))
            .GroupBy(item => PremiumAccessCatalog.NormalizeLanguageCode(item.LanguageCode), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => SumMetricWeights(group, AnalyticsMetricWeights.AudioListen),
                StringComparer.OrdinalIgnoreCase);
        var visibleLanguageCodes = GetSupportedLanguageCodeSet(settings)
            .Concat(audioPlayCountsByLanguage.Keys)
            .Where(code => !string.IsNullOrWhiteSpace(code))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(code => code, StringComparer.OrdinalIgnoreCase)
            .ToList();
        var audioPlaysByLanguage = visibleLanguageCodes
            .Select(languageCode => new DashboardAudioLanguageMetricResponse(
                languageCode,
                audioPlayCountsByLanguage.TryGetValue(languageCode, out var count) ? count : 0))
            .ToList();
        var poiViewsByPoi = pois
            .Select(poi => new DashboardPoiViewMetricResponse(
                poi.Id,
                ResolveDashboardPoiTitle(poi),
                poiViewCounts.TryGetValue(poi.Id, out var count) ? count : 0))
            .OrderByDescending(item => item.TotalPoiViews)
            .ThenBy(item => item.PoiTitle, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return new DashboardSummaryResponse(
            totalPoiViews,
            totalAudioPlays,
            totalQrScans,
            totalOfferViews,
            totalPois,
            totalTours,
            totalOffers,
            onlineUsers,
            audioPlaysByLanguage,
            poiViewsByPoi);
    }

    private static int SumUsageEventMetric(
        IEnumerable<AppUsageEvent> usageEvents,
        Func<AppUsageEvent, bool> predicate,
        int weight)
        => SumMetricWeights(usageEvents.Where(predicate), weight);

    private static int SumMetricWeights<T>(IEnumerable<T> items, int weight)
        => items.Sum(_ => weight);

    private static bool IsTrackedApkDownloadAccessEvent(AppUsageEvent usageEvent)
    {
        if (string.Equals(usageEvent.EventType, MobileUsageEventTypes.ApkDownloadAccess, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return string.Equals(usageEvent.EventType, MobileUsageEventTypes.QrScan, StringComparison.OrdinalIgnoreCase);
    }

    private static string ResolveDashboardPoiTitle(Poi poi)
        => string.IsNullOrWhiteSpace(poi.Title)
            ? (!string.IsNullOrWhiteSpace(poi.Slug) ? poi.Slug.Trim() : poi.Id)
            : poi.Title.Trim();

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
            using var connection = OpenConnectionCore(_connectionString);
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

            using var verifiedConnection = OpenConnectionCore(_connectionString);
            EnsureRuntimeCompatibilitySchema(verifiedConnection);
            NormalizePersistedPoiAddresses(verifiedConnection);
            NormalizeLegacyEntityTypes(verifiedConnection);
            NormalizeLegacyUsagePlatforms(verifiedConnection);
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
        EnsurePromotionRuntimeSchema(connection);
        EnsurePoiChangeRequestSchema(connection);
        EnsureTranslationRuntimeSchema(connection);
        EnsureAudioGuideRuntimeSchema(connection);
        EnsureRefreshSessionsTable(connection);
        EnsureSeparatedAuditLogSchema(connection);
        RemoveLegacyReviewData(connection);
        EnsureSystemSettingsSchema(connection);
        EnsureAppUsageEventSchema(connection);
        EnsureAppPresenceSchema(connection);
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

        EnsureDevelopmentAdminUserSeed(connection);
    }

    private void EnsureDevelopmentAdminUserSeed(SqlConnection connection)
    {
        if (!_isDevelopment)
        {
            return;
        }

        if (ExecuteScalarInt(connection, null, "SELECT COUNT(*) FROM dbo.AdminUsers;") > 0)
        {
            return;
        }

        var now = DateTimeOffset.UtcNow;
        ExecuteNonQuery(
            connection,
            null,
            """
            INSERT INTO dbo.AdminUsers (
                Id, Name, Email, Phone, Role, [Password], [Status], CreatedAt, LastLoginAt, AvatarColor,
                ManagedPoiId, ApprovalStatus, RejectionReason, RegistrationSubmittedAt, RegistrationReviewedAt
            )
            VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?);
            """,
            DefaultDevelopmentAdminUserId,
            DefaultDevelopmentAdminName,
            DefaultDevelopmentAdminEmail,
            string.Empty,
            AdminRoleCatalog.SuperAdmin,
            DefaultDevelopmentAdminPassword,
            "active",
            now,
            null,
            DefaultDevelopmentAdminAvatarColor,
            null,
            AdminApprovalCatalog.Approved,
            null,
            now,
            now);

        _logger.LogWarning(
            "Seeded default development admin account because dbo.AdminUsers was empty. email={Email}",
            DefaultDevelopmentAdminEmail);
    }

    private void EnsurePoiRuntimeSchema(SqlConnection connection)
    {
        ExecuteNonQuery(
            connection,
            null,
            """
            IF COL_LENGTH(N'dbo.Pois', N'OwnerUserId') IS NULL
                ALTER TABLE dbo.Pois ADD OwnerUserId NVARCHAR(50) NULL;

            IF COL_LENGTH(N'dbo.Pois', N'Title') IS NULL
                ALTER TABLE dbo.Pois ADD Title NVARCHAR(200) NULL;

            IF COL_LENGTH(N'dbo.Pois', N'ShortDescription') IS NULL
                ALTER TABLE dbo.Pois ADD ShortDescription NVARCHAR(MAX) NULL;

            IF COL_LENGTH(N'dbo.Pois', N'Description') IS NULL
                ALTER TABLE dbo.Pois ADD [Description] NVARCHAR(MAX) NULL;

            IF COL_LENGTH(N'dbo.Pois', N'AudioScript') IS NULL
                ALTER TABLE dbo.Pois ADD AudioScript NVARCHAR(MAX) NULL;

            IF COL_LENGTH(N'dbo.Pois', N'SourceLanguageCode') IS NULL
                ALTER TABLE dbo.Pois ADD SourceLanguageCode NVARCHAR(20) NULL;

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

            IF COL_LENGTH(N'dbo.Pois', N'TriggerRadius') IS NULL
                ALTER TABLE dbo.Pois ADD TriggerRadius INT NULL;

            IF COL_LENGTH(N'dbo.Pois', N'Priority') IS NULL
                ALTER TABLE dbo.Pois ADD Priority INT NULL;

            IF COL_LENGTH(N'dbo.Pois', N'PlaceTier') IS NULL
                ALTER TABLE dbo.Pois ADD PlaceTier INT NULL;
            """);

        ExecuteNonQuery(
            connection,
            null,
            """
            IF OBJECT_ID(N'dbo.PoiTranslations', N'U') IS NOT NULL
            BEGIN
                UPDATE poi
                SET Title = COALESCE(NULLIF(LTRIM(RTRIM(poi.Title)), N''), NULLIF(LTRIM(RTRIM(source.Title)), N''), poi.Slug),
                    ShortDescription = COALESCE(NULLIF(LTRIM(RTRIM(poi.ShortDescription)), N''), NULLIF(LTRIM(RTRIM(source.ShortText)), N''), N''),
                    [Description] = COALESCE(NULLIF(LTRIM(RTRIM(poi.[Description])), N''), NULLIF(LTRIM(RTRIM(source.FullText)), N''), NULLIF(LTRIM(RTRIM(source.ShortText)), N''), N''),
                    AudioScript = COALESCE(NULLIF(LTRIM(RTRIM(poi.AudioScript)), N''), NULLIF(LTRIM(RTRIM(source.FullText)), N''), NULLIF(LTRIM(RTRIM(source.ShortText)), N''), N''),
                    SourceLanguageCode = COALESCE(NULLIF(LTRIM(RTRIM(poi.SourceLanguageCode)), N''), N'vi')
                FROM dbo.Pois poi
                OUTER APPLY (
                    SELECT TOP 1 translation.Title, translation.ShortText, translation.FullText, translation.LanguageCode
                    FROM dbo.PoiTranslations translation
                    WHERE translation.EntityId = poi.Id
                      AND translation.EntityType IN (N'poi', N'place')
                    ORDER BY
                        CASE
                            WHEN LOWER(LTRIM(RTRIM(translation.LanguageCode))) = N'vi' THEN 0
                            ELSE 1
                        END,
                        translation.UpdatedAt DESC,
                        translation.Id DESC
                ) source;
            END;
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
            IF COL_LENGTH(N'dbo.Pois', N'TriggerRadius') IS NOT NULL
               AND NOT EXISTS (
                   SELECT 1
                   FROM sys.default_constraints defaultConstraint
                   INNER JOIN sys.columns columnInfo
                       ON columnInfo.object_id = defaultConstraint.parent_object_id
                      AND columnInfo.column_id = defaultConstraint.parent_column_id
                   WHERE defaultConstraint.parent_object_id = OBJECT_ID(N'dbo.Pois')
                     AND columnInfo.name = N'TriggerRadius'
               )
                ALTER TABLE dbo.Pois ADD CONSTRAINT DF_Pois_TriggerRadius DEFAULT ((20)) FOR TriggerRadius;
            """);

        ExecuteNonQuery(
            connection,
            null,
            """
            IF COL_LENGTH(N'dbo.Pois', N'Priority') IS NOT NULL
               AND NOT EXISTS (
                   SELECT 1
                   FROM sys.default_constraints defaultConstraint
                   INNER JOIN sys.columns columnInfo
                       ON columnInfo.object_id = defaultConstraint.parent_object_id
                      AND columnInfo.column_id = defaultConstraint.parent_column_id
                   WHERE defaultConstraint.parent_object_id = OBJECT_ID(N'dbo.Pois')
                     AND columnInfo.name = N'Priority'
               )
                ALTER TABLE dbo.Pois ADD CONSTRAINT DF_Pois_Priority DEFAULT ((0)) FOR Priority;
            """);

        ExecuteNonQuery(
            connection,
            null,
            """
            IF COL_LENGTH(N'dbo.Pois', N'PlaceTier') IS NOT NULL
               AND NOT EXISTS (
                   SELECT 1
                   FROM sys.default_constraints defaultConstraint
                   INNER JOIN sys.columns columnInfo
                       ON columnInfo.object_id = defaultConstraint.parent_object_id
                      AND columnInfo.column_id = defaultConstraint.parent_column_id
                   WHERE defaultConstraint.parent_object_id = OBJECT_ID(N'dbo.Pois')
                     AND columnInfo.name = N'PlaceTier'
               )
                ALTER TABLE dbo.Pois ADD CONSTRAINT DF_Pois_PlaceTier DEFAULT ((0)) FOR PlaceTier;
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
                Title = COALESCE(NULLIF(LTRIM(RTRIM(Title)), N''), Slug),
                ShortDescription = COALESCE(ShortDescription, N''),
                [Description] = COALESCE([Description], N''),
                AudioScript = COALESCE(NULLIF(LTRIM(RTRIM(AudioScript)), N''), COALESCE([Description], N'')),
                SourceLanguageCode = COALESCE(NULLIF(LTRIM(RTRIM(SourceLanguageCode)), N''), N'vi'),
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
                END,
                TriggerRadius = CASE
                    WHEN TriggerRadius IS NULL OR TriggerRadius < 20 THEN 20
                    ELSE TriggerRadius
                END,
                Priority = COALESCE(Priority, 0),
                PlaceTier = CASE
                    WHEN PlaceTier = 1 THEN 1
                    ELSE 0
                END;
            """);

        foreach (var column in new[] { "Title", "ShortDescription", "Description", "AudioScript", "SourceLanguageCode" })
        {
            ExecuteNonQuery(
                connection,
                null,
                $"""
                IF EXISTS (
                    SELECT 1
                    FROM sys.columns
                    WHERE object_id = OBJECT_ID(N'dbo.Pois')
                      AND name = N'{column}'
                      AND is_nullable = 1
                )
                BEGIN
                    ALTER TABLE dbo.Pois ALTER COLUMN {QuoteSqlIdentifier(column)} {(column == "Title" ? "NVARCHAR(200)" : column == "SourceLanguageCode" ? "NVARCHAR(20)" : "NVARCHAR(MAX)")} NOT NULL;
                END;
                """);
        }

        ExecuteNonQuery(
            connection,
            null,
            """
            IF EXISTS (
                SELECT 1
                FROM sys.columns
                WHERE object_id = OBJECT_ID(N'dbo.Pois')
                  AND name = N'PlaceTier'
                  AND is_nullable = 1
            )
            BEGIN
                ALTER TABLE dbo.Pois ALTER COLUMN PlaceTier INT NOT NULL;
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

    private void EnsurePromotionRuntimeSchema(SqlConnection connection)
    {
        ExecuteNonQuery(
            connection,
            null,
            """
            IF OBJECT_ID(N'dbo.Promotions', N'U') IS NULL
            BEGIN
                CREATE TABLE dbo.Promotions (
                    Id NVARCHAR(50) NOT NULL PRIMARY KEY,
                    PoiId NVARCHAR(50) NOT NULL,
                    Title NVARCHAR(200) NOT NULL,
                    [Description] NVARCHAR(MAX) NOT NULL,
                    StartAt DATETIMEOFFSET(7) NOT NULL,
                    EndAt DATETIMEOFFSET(7) NOT NULL,
                    [Status] NVARCHAR(20) NOT NULL,
                    VisibleFrom DATETIMEOFFSET(7) NULL,
                    CreatedByUserId NVARCHAR(50) NOT NULL,
                    OwnerUserId NVARCHAR(50) NULL,
                    IsDeleted BIT NOT NULL CONSTRAINT DF_Promotions_IsDeleted DEFAULT ((0)),
                    CONSTRAINT FK_Promotions_Pois FOREIGN KEY (PoiId) REFERENCES dbo.Pois(Id)
                );
            END;

            IF COL_LENGTH(N'dbo.Promotions', N'VisibleFrom') IS NULL
                ALTER TABLE dbo.Promotions ADD VisibleFrom DATETIMEOFFSET(7) NULL;

            IF COL_LENGTH(N'dbo.Promotions', N'CreatedByUserId') IS NULL
                ALTER TABLE dbo.Promotions ADD CreatedByUserId NVARCHAR(50) NULL;

            IF COL_LENGTH(N'dbo.Promotions', N'OwnerUserId') IS NULL
                ALTER TABLE dbo.Promotions ADD OwnerUserId NVARCHAR(50) NULL;

            IF COL_LENGTH(N'dbo.Promotions', N'IsDeleted') IS NULL
                ALTER TABLE dbo.Promotions ADD IsDeleted BIT NULL;
            """);

        ExecuteNonQuery(
            connection,
            null,
            """
            UPDATE promotion
            SET [Status] = CASE
                    WHEN LOWER(COALESCE(LTRIM(RTRIM(promotion.[Status])), N'')) IN (N'active', N'published')
                        THEN N'active'
                    WHEN LOWER(COALESCE(LTRIM(RTRIM(promotion.[Status])), N'')) = N'expired'
                        THEN N'active'
                    ELSE N'upcoming'
                END,
                VisibleFrom = COALESCE(promotion.VisibleFrom, promotion.StartAt, SYSDATETIMEOFFSET()),
                CreatedByUserId = COALESCE(NULLIF(LTRIM(RTRIM(promotion.CreatedByUserId)), N''), N'system'),
                OwnerUserId = COALESCE(NULLIF(LTRIM(RTRIM(promotion.OwnerUserId)), N''), poi.OwnerUserId),
                IsDeleted = CASE
                    WHEN LOWER(COALESCE(LTRIM(RTRIM(promotion.[Status])), N'')) IN (N'deleted', N'hidden')
                        THEN CAST(1 AS bit)
                    ELSE COALESCE(promotion.IsDeleted, CAST(0 AS bit))
                END
            FROM dbo.Promotions promotion
            LEFT JOIN dbo.Pois poi
                ON poi.Id = promotion.PoiId;
            """);

        ExecuteNonQuery(
            connection,
            null,
            """
            IF NOT EXISTS (
                SELECT 1
                FROM sys.default_constraints defaultConstraint
                INNER JOIN sys.columns columnInfo
                    ON columnInfo.object_id = defaultConstraint.parent_object_id
                   AND columnInfo.column_id = defaultConstraint.parent_column_id
                WHERE defaultConstraint.parent_object_id = OBJECT_ID(N'dbo.Promotions')
                  AND columnInfo.name = N'IsDeleted'
            )
                ALTER TABLE dbo.Promotions ADD CONSTRAINT DF_Promotions_IsDeleted DEFAULT ((0)) FOR IsDeleted;

            IF EXISTS (
                SELECT 1
                FROM sys.columns
                WHERE object_id = OBJECT_ID(N'dbo.Promotions')
                  AND name = N'CreatedByUserId'
                  AND is_nullable = 1
            )
                ALTER TABLE dbo.Promotions ALTER COLUMN CreatedByUserId NVARCHAR(50) NOT NULL;

            IF EXISTS (
                SELECT 1
                FROM sys.columns
                WHERE object_id = OBJECT_ID(N'dbo.Promotions')
                  AND name = N'IsDeleted'
                  AND is_nullable = 1
            )
                ALTER TABLE dbo.Promotions ALTER COLUMN IsDeleted BIT NOT NULL;

            IF NOT EXISTS (
                SELECT 1
                FROM sys.indexes
                WHERE name = N'IX_Promotions_PoiId_Status'
                  AND object_id = OBJECT_ID(N'dbo.Promotions')
            )
                CREATE INDEX IX_Promotions_PoiId_Status
                ON dbo.Promotions (PoiId, [Status], VisibleFrom, IsDeleted);
            """);
    }

    private void EnsurePoiChangeRequestSchema(SqlConnection connection)
    {
        ExecuteNonQuery(
            connection,
            null,
            """
            IF OBJECT_ID(N'dbo.PoiChangeRequests', N'U') IS NULL
            BEGIN
                CREATE TABLE dbo.PoiChangeRequests (
                    Id NVARCHAR(50) NOT NULL PRIMARY KEY,
                    PoiId NVARCHAR(50) NOT NULL,
                    SubmittedByUserId NVARCHAR(50) NOT NULL,
                    SubmittedByName NVARCHAR(120) NOT NULL,
                    BeforeJson NVARCHAR(MAX) NOT NULL,
                    AfterJson NVARCHAR(MAX) NOT NULL,
                    [Status] NVARCHAR(20) NOT NULL,
                    RejectionReason NVARCHAR(1000) NULL,
                    SubmittedAt DATETIMEOFFSET(7) NOT NULL,
                    ReviewedAt DATETIMEOFFSET(7) NULL,
                    ReviewedByUserId NVARCHAR(50) NULL,
                    ReviewedByName NVARCHAR(120) NULL,
                    AppliedAt DATETIMEOFFSET(7) NULL,
                    CONSTRAINT FK_PoiChangeRequests_Pois FOREIGN KEY (PoiId) REFERENCES dbo.Pois(Id),
                    CONSTRAINT FK_PoiChangeRequests_SubmittedBy FOREIGN KEY (SubmittedByUserId) REFERENCES dbo.AdminUsers(Id)
                );
            END;

            IF COL_LENGTH(N'dbo.PoiChangeRequests', N'ReviewedByUserId') IS NULL
                ALTER TABLE dbo.PoiChangeRequests ADD ReviewedByUserId NVARCHAR(50) NULL;

            IF COL_LENGTH(N'dbo.PoiChangeRequests', N'ReviewedByName') IS NULL
                ALTER TABLE dbo.PoiChangeRequests ADD ReviewedByName NVARCHAR(120) NULL;

            IF COL_LENGTH(N'dbo.PoiChangeRequests', N'AppliedAt') IS NULL
                ALTER TABLE dbo.PoiChangeRequests ADD AppliedAt DATETIMEOFFSET(7) NULL;

            UPDATE dbo.PoiChangeRequests
            SET [Status] = CASE
                WHEN LOWER(COALESCE(LTRIM(RTRIM([Status])), N'')) IN (N'pending', N'approved', N'rejected')
                    THEN LOWER(LTRIM(RTRIM([Status])))
                ELSE N'pending'
            END;

            IF NOT EXISTS (
                SELECT 1
                FROM sys.indexes
                WHERE name = N'IX_PoiChangeRequests_Status_SubmittedAt'
                  AND object_id = OBJECT_ID(N'dbo.PoiChangeRequests')
            )
                CREATE INDEX IX_PoiChangeRequests_Status_SubmittedAt
                ON dbo.PoiChangeRequests ([Status], SubmittedAt DESC);

            IF NOT EXISTS (
                SELECT 1
                FROM sys.indexes
                WHERE name = N'IX_PoiChangeRequests_PoiId'
                  AND object_id = OBJECT_ID(N'dbo.PoiChangeRequests')
            )
                CREATE INDEX IX_PoiChangeRequests_PoiId
                ON dbo.PoiChangeRequests (PoiId);
            """);
    }

    private void EnsureAudioGuideRuntimeSchema(SqlConnection connection)
    {
        ExecuteNonQuery(
            connection,
            null,
            """
            IF OBJECT_ID(N'dbo.AudioGuides', N'U') IS NULL
            BEGIN
                CREATE TABLE dbo.AudioGuides (
                    Id NVARCHAR(50) NOT NULL PRIMARY KEY,
                    EntityType NVARCHAR(30) NOT NULL,
                    EntityId NVARCHAR(50) NOT NULL,
                    LanguageCode NVARCHAR(20) NOT NULL,
                    TranscriptText NVARCHAR(MAX) NOT NULL,
                    AudioUrl NVARCHAR(500) NOT NULL,
                    AudioFilePath NVARCHAR(500) NOT NULL,
                    AudioFileName NVARCHAR(260) NOT NULL,
                    VoiceType NVARCHAR(30) NOT NULL,
                    SourceType NVARCHAR(30) NOT NULL,
                    Provider NVARCHAR(50) NOT NULL,
                    VoiceId NVARCHAR(120) NOT NULL,
                    ModelId NVARCHAR(120) NOT NULL,
                    OutputFormat NVARCHAR(50) NOT NULL,
                    DurationInSeconds FLOAT NULL,
                    FileSizeBytes BIGINT NULL,
                    TextHash NVARCHAR(128) NOT NULL,
                    ContentVersion NVARCHAR(128) NOT NULL,
                    GeneratedAt DATETIMEOFFSET(7) NULL,
                    GenerationStatus NVARCHAR(30) NOT NULL,
                    ErrorMessage NVARCHAR(2000) NULL,
                    IsOutdated BIT NOT NULL,
                    [Status] NVARCHAR(30) NOT NULL,
                    UpdatedBy NVARCHAR(120) NOT NULL,
                    UpdatedAt DATETIMEOFFSET(7) NOT NULL,
                    CONSTRAINT UQ_AudioGuides UNIQUE (EntityType, EntityId, LanguageCode)
                );
            END;

            IF COL_LENGTH(N'dbo.AudioGuides', N'TranscriptText') IS NULL
                ALTER TABLE dbo.AudioGuides ADD TranscriptText NVARCHAR(MAX) NULL;

            IF COL_LENGTH(N'dbo.AudioGuides', N'AudioFilePath') IS NULL
                ALTER TABLE dbo.AudioGuides ADD AudioFilePath NVARCHAR(500) NULL;

            IF COL_LENGTH(N'dbo.AudioGuides', N'AudioFileName') IS NULL
                ALTER TABLE dbo.AudioGuides ADD AudioFileName NVARCHAR(260) NULL;

            IF COL_LENGTH(N'dbo.AudioGuides', N'Provider') IS NULL
                ALTER TABLE dbo.AudioGuides ADD Provider NVARCHAR(50) NULL;

            IF COL_LENGTH(N'dbo.AudioGuides', N'VoiceId') IS NULL
                ALTER TABLE dbo.AudioGuides ADD VoiceId NVARCHAR(120) NULL;

            IF COL_LENGTH(N'dbo.AudioGuides', N'ModelId') IS NULL
                ALTER TABLE dbo.AudioGuides ADD ModelId NVARCHAR(120) NULL;

            IF COL_LENGTH(N'dbo.AudioGuides', N'OutputFormat') IS NULL
                ALTER TABLE dbo.AudioGuides ADD OutputFormat NVARCHAR(50) NULL;

            IF COL_LENGTH(N'dbo.AudioGuides', N'DurationInSeconds') IS NULL
                ALTER TABLE dbo.AudioGuides ADD DurationInSeconds FLOAT NULL;

            IF COL_LENGTH(N'dbo.AudioGuides', N'FileSizeBytes') IS NULL
                ALTER TABLE dbo.AudioGuides ADD FileSizeBytes BIGINT NULL;

            IF COL_LENGTH(N'dbo.AudioGuides', N'TextHash') IS NULL
                ALTER TABLE dbo.AudioGuides ADD TextHash NVARCHAR(128) NULL;

            IF COL_LENGTH(N'dbo.AudioGuides', N'ContentVersion') IS NULL
                ALTER TABLE dbo.AudioGuides ADD ContentVersion NVARCHAR(128) NULL;

            IF COL_LENGTH(N'dbo.AudioGuides', N'GeneratedAt') IS NULL
                ALTER TABLE dbo.AudioGuides ADD GeneratedAt DATETIMEOFFSET(7) NULL;

            IF COL_LENGTH(N'dbo.AudioGuides', N'GenerationStatus') IS NULL
                ALTER TABLE dbo.AudioGuides ADD GenerationStatus NVARCHAR(30) NULL;

            IF COL_LENGTH(N'dbo.AudioGuides', N'ErrorMessage') IS NULL
                ALTER TABLE dbo.AudioGuides ADD ErrorMessage NVARCHAR(2000) NULL;

            IF COL_LENGTH(N'dbo.AudioGuides', N'IsOutdated') IS NULL
                ALTER TABLE dbo.AudioGuides ADD IsOutdated BIT NULL;
            """);

        ExecuteNonQuery(
            connection,
            null,
            """
            UPDATE dbo.AudioGuides
            SET TranscriptText = COALESCE(TranscriptText, N''),
                AudioFilePath = COALESCE(AudioFilePath, N''),
                AudioFileName = COALESCE(AudioFileName, N''),
                VoiceType = COALESCE(NULLIF(LTRIM(RTRIM(VoiceType)), N''), N'standard'),
                Provider = COALESCE(
                    NULLIF(LTRIM(RTRIM(Provider)), N''),
                    CASE
                        WHEN LOWER(COALESCE(LTRIM(RTRIM(SourceType)), N'')) = N'uploaded' THEN N'uploaded'
                        ELSE N'elevenlabs'
                    END),
                VoiceId = COALESCE(NULLIF(LTRIM(RTRIM(VoiceId)), N''), N''),
                ModelId = COALESCE(NULLIF(LTRIM(RTRIM(ModelId)), N''), N''),
                OutputFormat = COALESCE(NULLIF(LTRIM(RTRIM(OutputFormat)), N''), N'mp3_44100_128'),
                TextHash = COALESCE(NULLIF(LTRIM(RTRIM(TextHash)), N''), N''),
                ContentVersion = COALESCE(NULLIF(LTRIM(RTRIM(ContentVersion)), N''), COALESCE(NULLIF(LTRIM(RTRIM(TextHash)), N''), N'')),
                UpdatedBy = COALESCE(NULLIF(LTRIM(RTRIM(UpdatedBy)), N''), N'system'),
                IsOutdated = COALESCE(IsOutdated, CAST(0 AS bit))
            WHERE TranscriptText IS NULL
               OR AudioFilePath IS NULL
               OR AudioFileName IS NULL
               OR Provider IS NULL
               OR VoiceId IS NULL
               OR ModelId IS NULL
               OR OutputFormat IS NULL
               OR TextHash IS NULL
               OR ContentVersion IS NULL
               OR IsOutdated IS NULL
               OR NULLIF(LTRIM(RTRIM(VoiceType)), N'') IS NULL
               OR NULLIF(LTRIM(RTRIM(UpdatedBy)), N'') IS NULL;
            """);

        ExecuteNonQuery(
            connection,
            null,
            """
            UPDATE dbo.AudioGuides
            SET IsOutdated = CAST(1 AS bit),
                GenerationStatus = N'outdated',
                [Status] = N'missing'
            WHERE LOWER(COALESCE(LTRIM(RTRIM(SourceType)), N'')) = N'tts'
              AND NULLIF(LTRIM(RTRIM(AudioUrl)), N'') IS NULL;
            """);

        ExecuteNonQuery(
            connection,
            null,
            """
            UPDATE dbo.AudioGuides
            SET SourceType = CASE
                WHEN LOWER(COALESCE(LTRIM(RTRIM(SourceType)), N'')) = N'uploaded' THEN N'uploaded'
                WHEN LOWER(COALESCE(LTRIM(RTRIM(SourceType)), N'')) IN (N'generated', N'pregenerated', N'pre_generated', N'tts') THEN N'generated'
                ELSE N'generated'
            END
            WHERE NULLIF(LTRIM(RTRIM(SourceType)), N'') IS NULL
               OR LOWER(LTRIM(RTRIM(SourceType))) NOT IN (N'uploaded', N'generated');
            """);

        ExecuteNonQuery(
            connection,
            null,
            """
            UPDATE dbo.AudioGuides
            SET GenerationStatus = CASE
                    WHEN COALESCE(IsOutdated, CAST(0 AS bit)) = CAST(1 AS bit) THEN N'outdated'
                    WHEN LOWER(COALESCE(LTRIM(RTRIM(GenerationStatus)), N'')) IN (N'pending', N'success', N'failed', N'outdated')
                        THEN LOWER(LTRIM(RTRIM(GenerationStatus)))
                    WHEN LOWER(COALESCE(LTRIM(RTRIM([Status])), N'')) = N'processing' THEN N'pending'
                    WHEN NULLIF(LTRIM(RTRIM(AudioUrl)), N'') IS NOT NULL
                         OR NULLIF(LTRIM(RTRIM(AudioFilePath)), N'') IS NOT NULL THEN N'success'
                    ELSE N'none'
                END,
                GeneratedAt = CASE
                    WHEN GeneratedAt IS NOT NULL THEN GeneratedAt
                    WHEN NULLIF(LTRIM(RTRIM(AudioUrl)), N'') IS NOT NULL
                         OR NULLIF(LTRIM(RTRIM(AudioFilePath)), N'') IS NOT NULL THEN UpdatedAt
                    ELSE NULL
                END;
            """);

        ExecuteNonQuery(
            connection,
            null,
            """
            UPDATE dbo.AudioGuides
            SET [Status] = CASE
                WHEN COALESCE(IsOutdated, CAST(0 AS bit)) = CAST(1 AS bit)
                     OR LOWER(COALESCE(LTRIM(RTRIM(GenerationStatus)), N'')) = N'outdated' THEN N'missing'
                WHEN LOWER(COALESCE(LTRIM(RTRIM(GenerationStatus)), N'')) = N'pending' THEN N'processing'
                WHEN LOWER(COALESCE(LTRIM(RTRIM(GenerationStatus)), N'')) = N'success'
                     AND (
                        NULLIF(LTRIM(RTRIM(AudioUrl)), N'') IS NOT NULL
                        OR NULLIF(LTRIM(RTRIM(AudioFilePath)), N'') IS NOT NULL
                     ) THEN N'ready'
                ELSE N'missing'
            END;
            """);

        ExecuteNonQuery(
            connection,
            null,
            """
            IF EXISTS (
                SELECT 1
                FROM sys.columns
                WHERE object_id = OBJECT_ID(N'dbo.AudioGuides')
                  AND name = N'TranscriptText'
                  AND is_nullable = 1
            )
                ALTER TABLE dbo.AudioGuides ALTER COLUMN TranscriptText NVARCHAR(MAX) NOT NULL;

            IF EXISTS (
                SELECT 1
                FROM sys.columns
                WHERE object_id = OBJECT_ID(N'dbo.AudioGuides')
                  AND name = N'AudioFilePath'
                  AND is_nullable = 1
            )
                ALTER TABLE dbo.AudioGuides ALTER COLUMN AudioFilePath NVARCHAR(500) NOT NULL;

            IF EXISTS (
                SELECT 1
                FROM sys.columns
                WHERE object_id = OBJECT_ID(N'dbo.AudioGuides')
                  AND name = N'AudioFileName'
                  AND is_nullable = 1
            )
                ALTER TABLE dbo.AudioGuides ALTER COLUMN AudioFileName NVARCHAR(260) NOT NULL;

            IF EXISTS (
                SELECT 1
                FROM sys.columns
                WHERE object_id = OBJECT_ID(N'dbo.AudioGuides')
                  AND name = N'Provider'
                  AND is_nullable = 1
            )
                ALTER TABLE dbo.AudioGuides ALTER COLUMN Provider NVARCHAR(50) NOT NULL;

            IF EXISTS (
                SELECT 1
                FROM sys.columns
                WHERE object_id = OBJECT_ID(N'dbo.AudioGuides')
                  AND name = N'VoiceId'
                  AND is_nullable = 1
            )
                ALTER TABLE dbo.AudioGuides ALTER COLUMN VoiceId NVARCHAR(120) NOT NULL;

            IF EXISTS (
                SELECT 1
                FROM sys.columns
                WHERE object_id = OBJECT_ID(N'dbo.AudioGuides')
                  AND name = N'ModelId'
                  AND is_nullable = 1
            )
                ALTER TABLE dbo.AudioGuides ALTER COLUMN ModelId NVARCHAR(120) NOT NULL;

            IF EXISTS (
                SELECT 1
                FROM sys.columns
                WHERE object_id = OBJECT_ID(N'dbo.AudioGuides')
                  AND name = N'OutputFormat'
                  AND is_nullable = 1
            )
                ALTER TABLE dbo.AudioGuides ALTER COLUMN OutputFormat NVARCHAR(50) NOT NULL;

            IF EXISTS (
                SELECT 1
                FROM sys.columns
                WHERE object_id = OBJECT_ID(N'dbo.AudioGuides')
                  AND name = N'TextHash'
                  AND is_nullable = 1
            )
                ALTER TABLE dbo.AudioGuides ALTER COLUMN TextHash NVARCHAR(128) NOT NULL;

            IF EXISTS (
                SELECT 1
                FROM sys.columns
                WHERE object_id = OBJECT_ID(N'dbo.AudioGuides')
                  AND name = N'ContentVersion'
                  AND is_nullable = 1
            )
                ALTER TABLE dbo.AudioGuides ALTER COLUMN ContentVersion NVARCHAR(128) NOT NULL;

            IF EXISTS (
                SELECT 1
                FROM sys.columns
                WHERE object_id = OBJECT_ID(N'dbo.AudioGuides')
                  AND name = N'GenerationStatus'
                  AND is_nullable = 1
            )
                ALTER TABLE dbo.AudioGuides ALTER COLUMN GenerationStatus NVARCHAR(30) NOT NULL;

            IF EXISTS (
                SELECT 1
                FROM sys.columns
                WHERE object_id = OBJECT_ID(N'dbo.AudioGuides')
                  AND name = N'IsOutdated'
                  AND is_nullable = 1
            )
                ALTER TABLE dbo.AudioGuides ALTER COLUMN IsOutdated BIT NOT NULL;
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
                        WHEN legacy.[Action] LIKE N'%cài đặt%' THEN N'SETTINGS'
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
                    SupportHours NVARCHAR(120) NOT NULL DEFAULT N'',
                    ContactUpdatedAtUtc DATETIMEOFFSET(7) NOT NULL DEFAULT SYSUTCDATETIME(),
                    DefaultLanguage NVARCHAR(20) NOT NULL,
                    FallbackLanguage NVARCHAR(20) NOT NULL,
                    StorageProvider NVARCHAR(50) NOT NULL,
                    GeofenceRadiusMeters INT NOT NULL,
                    AnalyticsRetentionDays INT NOT NULL
                );
            END;

            IF COL_LENGTH(N'dbo.SystemSettings', N'AnalyticsRetentionDays') IS NULL
                ALTER TABLE dbo.SystemSettings ADD AnalyticsRetentionDays INT NULL;

            IF COL_LENGTH(N'dbo.SystemSettings', N'SupportPhone') IS NULL
                ALTER TABLE dbo.SystemSettings ADD SupportPhone NVARCHAR(50) NULL;

            IF COL_LENGTH(N'dbo.SystemSettings', N'ContactAddress') IS NULL
                ALTER TABLE dbo.SystemSettings ADD ContactAddress NVARCHAR(300) NULL;

            IF COL_LENGTH(N'dbo.SystemSettings', N'SupportInstructions') IS NULL
                ALTER TABLE dbo.SystemSettings ADD SupportInstructions NVARCHAR(2000) NULL;

            IF COL_LENGTH(N'dbo.SystemSettings', N'SupportHours') IS NULL
                ALTER TABLE dbo.SystemSettings ADD SupportHours NVARCHAR(120) NULL;

            IF COL_LENGTH(N'dbo.SystemSettings', N'ContactUpdatedAtUtc') IS NULL
                ALTER TABLE dbo.SystemSettings ADD ContactUpdatedAtUtc DATETIMEOFFSET(7) NULL;
            """);

        ExecuteNonQuery(
            connection,
            null,
            """
            IF NOT EXISTS (SELECT 1 FROM dbo.SystemSettings WHERE Id = 1)
            BEGIN
                INSERT INTO dbo.SystemSettings (
                    Id, AppName, SupportEmail, DefaultLanguage, FallbackLanguage,
                    StorageProvider, GeofenceRadiusMeters, AnalyticsRetentionDays,
                    SupportPhone, ContactAddress, SupportInstructions,
                    SupportHours, ContactUpdatedAtUtc
                )
                VALUES (
                    1,
                    N'Vinh Khanh Food Guide',
                    N'support@vinhkhanhfood.local',
                    N'vi',
                    N'en',
                    N'cloudinary',
                    30,
                    180,
                    N'0900000000',
                    N'Vinh Khanh Food Street, Ho Chi Minh City',
                    N'Vui long lien he bo phan ho tro neu ban can khieu nai hoac can tro giup.',
                    N'',
                    SYSUTCDATETIME()
                );
            END;

            UPDATE dbo.SystemSettings
            SET AnalyticsRetentionDays = COALESCE(AnalyticsRetentionDays, 180),
                SupportPhone = COALESCE(NULLIF(LTRIM(RTRIM(SupportPhone)), N''), N'0900000000'),
                ContactAddress = COALESCE(ContactAddress, N''),
                SupportInstructions = COALESCE(NULLIF(LTRIM(RTRIM(SupportInstructions)), N''), N'Vui long lien he bo phan ho tro neu ban can khieu nai hoac can tro giup.'),
                SupportHours = COALESCE(SupportHours, N''),
                ContactUpdatedAtUtc = COALESCE(ContactUpdatedAtUtc, SYSUTCDATETIME())
            WHERE AnalyticsRetentionDays IS NULL
               OR NULLIF(LTRIM(RTRIM(SupportPhone)), N'') IS NULL
               OR ContactAddress IS NULL
               OR SupportInstructions IS NULL
               OR SupportHours IS NULL
               OR ContactUpdatedAtUtc IS NULL;

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

            IF COL_LENGTH(N'dbo.SystemSettings', N'TtsProvider') IS NOT NULL
            BEGIN
                DECLARE @ttsProviderConstraint sysname;

                SELECT TOP 1 @ttsProviderConstraint = defaultConstraint.name
                FROM sys.default_constraints defaultConstraint
                INNER JOIN sys.columns columnInfo
                    ON columnInfo.object_id = defaultConstraint.parent_object_id
                   AND columnInfo.column_id = defaultConstraint.parent_column_id
                WHERE defaultConstraint.parent_object_id = OBJECT_ID(N'dbo.SystemSettings')
                  AND columnInfo.name = N'TtsProvider';

                IF @ttsProviderConstraint IS NOT NULL
                BEGIN
                    EXEC(N'ALTER TABLE dbo.SystemSettings DROP CONSTRAINT [' + @ttsProviderConstraint + N']');
                END;

                ALTER TABLE dbo.SystemSettings DROP COLUMN TtsProvider;
            END;

            IF COL_LENGTH(N'dbo.SystemSettings', N'MapProvider') IS NOT NULL
            BEGIN
                DECLARE @mapProviderConstraint sysname;

                SELECT TOP 1 @mapProviderConstraint = defaultConstraint.name
                FROM sys.default_constraints defaultConstraint
                INNER JOIN sys.columns columnInfo
                    ON columnInfo.object_id = defaultConstraint.parent_object_id
                   AND columnInfo.column_id = defaultConstraint.parent_column_id
                WHERE defaultConstraint.parent_object_id = OBJECT_ID(N'dbo.SystemSettings')
                  AND columnInfo.name = N'MapProvider';

                IF @mapProviderConstraint IS NOT NULL
                BEGIN
                    EXEC(N'ALTER TABLE dbo.SystemSettings DROP CONSTRAINT [' + @mapProviderConstraint + N']');
                END;

                ALTER TABLE dbo.SystemSettings DROP COLUMN MapProvider;
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

            IF EXISTS (
                SELECT 1
                FROM sys.columns
                WHERE object_id = OBJECT_ID(N'dbo.SystemSettings')
                    AND name = N'SupportPhone'
                    AND is_nullable = 1
            )
            BEGIN
                ALTER TABLE dbo.SystemSettings ALTER COLUMN SupportPhone NVARCHAR(50) NOT NULL;
            END;

            IF EXISTS (
                SELECT 1
                FROM sys.columns
                WHERE object_id = OBJECT_ID(N'dbo.SystemSettings')
                    AND name = N'ContactAddress'
                    AND is_nullable = 1
            )
            BEGIN
                ALTER TABLE dbo.SystemSettings ALTER COLUMN ContactAddress NVARCHAR(300) NOT NULL;
            END;

            IF EXISTS (
                SELECT 1
                FROM sys.columns
                WHERE object_id = OBJECT_ID(N'dbo.SystemSettings')
                    AND name = N'SupportInstructions'
                    AND is_nullable = 1
            )
            BEGIN
                ALTER TABLE dbo.SystemSettings ALTER COLUMN SupportInstructions NVARCHAR(2000) NOT NULL;
            END;

            IF EXISTS (
                SELECT 1
                FROM sys.columns
                WHERE object_id = OBJECT_ID(N'dbo.SystemSettings')
                    AND name = N'SupportHours'
                    AND is_nullable = 1
            )
            BEGIN
                ALTER TABLE dbo.SystemSettings ALTER COLUMN SupportHours NVARCHAR(120) NOT NULL;
            END;

            IF EXISTS (
                SELECT 1
                FROM sys.columns
                WHERE object_id = OBJECT_ID(N'dbo.SystemSettings')
                    AND name = N'ContactUpdatedAtUtc'
                    AND is_nullable = 1
            )
            BEGIN
                ALTER TABLE dbo.SystemSettings ALTER COLUMN ContactUpdatedAtUtc DATETIMEOFFSET(7) NOT NULL;
            END;

            IF COL_LENGTH(N'dbo.SystemSettings', N'PremiumUnlockPriceUsd') IS NOT NULL
            BEGIN
                DECLARE @premiumUnlockConstraint sysname;

                SELECT TOP 1 @premiumUnlockConstraint = defaultConstraint.name
                FROM sys.default_constraints defaultConstraint
                INNER JOIN sys.columns columnInfo
                    ON columnInfo.object_id = defaultConstraint.parent_object_id
                   AND columnInfo.column_id = defaultConstraint.parent_column_id
                WHERE defaultConstraint.parent_object_id = OBJECT_ID(N'dbo.SystemSettings')
                  AND columnInfo.name = N'PremiumUnlockPriceUsd';

                IF @premiumUnlockConstraint IS NOT NULL
                BEGIN
                    EXEC(N'ALTER TABLE dbo.SystemSettings DROP CONSTRAINT [' + @premiumUnlockConstraint + N']');
                END;

                ALTER TABLE dbo.SystemSettings DROP COLUMN PremiumUnlockPriceUsd;
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

            IF NOT EXISTS (
                SELECT 1
                FROM dbo.SystemSettingLanguages
                WHERE SettingId = 1 AND LanguageType = N'supported'
            )
            BEGIN
                INSERT INTO dbo.SystemSettingLanguages (SettingId, LanguageType, LanguageCode)
                VALUES
                    (1, N'supported', N'vi'),
                    (1, N'supported', N'en'),
                    (1, N'supported', N'zh-CN'),
                    (1, N'supported', N'ko'),
                    (1, N'supported', N'ja');
            END;

            IF NOT EXISTS (
                SELECT 1
                FROM dbo.SystemSettingLanguages
                WHERE SettingId = 1
                  AND LanguageType = N'supported'
                  AND LanguageCode = (SELECT DefaultLanguage FROM dbo.SystemSettings WHERE Id = 1)
            )
            BEGIN
                INSERT INTO dbo.SystemSettingLanguages (SettingId, LanguageType, LanguageCode)
                SELECT 1, N'supported', DefaultLanguage
                FROM dbo.SystemSettings
                WHERE Id = 1;
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

        var ownerPoiIds = GetOwnerPoiIds(connection, transaction, actor.UserId);
        return routes
            .Where(route =>
                route.IsSystemRoute &&
                route.IsActive &&
                route.StopPoiIds.Any(ownerPoiIds.Contains))
            .ToList();
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
            return promotions;
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
            .Where(promotion => visiblePoiIds.Contains(promotion.PoiId) && IsPromotionVisibleToPublic(promotion))
            .Select(CopyPublicPromotion)
            .ToList();
    }

    private static bool IsPromotionVisibleToPublic(Promotion promotion)
    {
        var now = DateTimeOffset.UtcNow;
        if (promotion.IsDeleted || promotion.EndAt <= now)
        {
            return false;
        }

        return string.Equals(promotion.Status, "active", StringComparison.OrdinalIgnoreCase);
    }

    private static Promotion CopyPublicPromotion(Promotion promotion)
        => new()
        {
            Id = promotion.Id,
            PoiId = promotion.PoiId,
            Title = promotion.Title,
            Description = promotion.Description,
            StartAt = promotion.StartAt,
            EndAt = promotion.EndAt,
            Status = "active",
            VisibleFrom = promotion.VisibleFrom,
            CreatedByUserId = string.Empty,
            OwnerUserId = null,
            IsDeleted = false
        };

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
                AudioGuideCatalog.IsReadyForPlayback(audioGuide) &&
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

    private void NormalizeLegacyUsagePlatforms(SqlConnection connection)
    {
        var normalizedViewLogs = ExecuteNonQuery(
            connection,
            null,
            """
            IF OBJECT_ID(N'dbo.ViewLogs', N'U') IS NOT NULL
            BEGIN
                UPDATE dbo.ViewLogs
                SET DeviceType = N'android'
                WHERE LOWER(LTRIM(RTRIM(COALESCE(DeviceType, N'')))) = N'ios';
            END;
            """);

        var normalizedUsageEvents = ExecuteNonQuery(
            connection,
            null,
            """
            IF OBJECT_ID(N'dbo.AppUsageEvents', N'U') IS NOT NULL
            BEGIN
                UPDATE dbo.AppUsageEvents
                SET Platform = N'android'
                WHERE LOWER(LTRIM(RTRIM(COALESCE(Platform, N'')))) = N'ios';
            END;
            """);

        if (normalizedViewLogs > 0 || normalizedUsageEvents > 0)
        {
            _logger.LogInformation(
                "Normalized legacy usage platforms. viewLogsUpdated={ViewLogsUpdated}, usageEventsUpdated={UsageEventsUpdated}",
                normalizedViewLogs,
                normalizedUsageEvents);
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
