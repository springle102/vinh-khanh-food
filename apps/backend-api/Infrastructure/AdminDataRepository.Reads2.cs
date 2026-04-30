using Microsoft.Data.SqlClient;
using VinhKhanh.BackendApi.Models;

namespace VinhKhanh.BackendApi.Infrastructure;

public sealed partial class AdminDataRepository
{
    private IReadOnlyList<TourRoute> GetRoutes(SqlConnection connection, SqlTransaction? transaction)
    {
        const string routesSql = """
            SELECT Id, Name, [Description], IsFeatured, IsActive, IsSystemRoute, OwnerUserId, UpdatedBy, UpdatedAt
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
                var routeName = ReadString(routesReader, "Name");
                var stopPoiIds = stopMap.TryGetValue(routeId, out var items)
                    ? new List<string>(items)
                    : new List<string>();
                routes.Add(new TourRoute
                {
                    Id = routeId,
                    Name = routeName,
                    Theme = BuildRouteTheme(routeName),
                    Description = ReadString(routesReader, "Description"),
                    DurationMinutes = ResolveRouteDurationMinutes(stopPoiIds.Count),
                    Difficulty = DefaultRouteDifficulty,
                    CoverImageUrl = string.Empty,
                    IsFeatured = ReadBool(routesReader, "IsFeatured"),
                    StopPoiIds = stopPoiIds,
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
            SELECT Id, PoiId, Title, [Description], StartAt, EndAt, [Status], VisibleFrom, CreatedByUserId, OwnerUserId, IsDeleted
            FROM dbo.Promotions
            WHERE COALESCE(IsDeleted, CAST(0 AS bit)) = CAST(0 AS bit)
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
                DeviceType = NormalizeUsagePlatform(ReadString(reader, "DeviceType")),
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
            WHERE UPPER(COALESCE(legacy.ActorRole, N'')) IN (N'SUPER_ADMIN', N'SUPERADMIN', N'PLACE_OWNER', N'PLACEOWNER', N'SYSTEM')
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
                ActorRole = AdminRoleCatalog.NormalizeKnownRoleOrOriginal(ReadString(reader, "ActorRole")),
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
            SELECT Id, AppName, SupportEmail, SupportPhone, ContactAddress, SupportInstructions,
                   SupportHours, ContactUpdatedAtUtc,
                   DefaultLanguage, FallbackLanguage, StorageProvider,
                   GeofenceRadiusMeters, AnalyticsRetentionDays
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
                    SupportPhone = ReadString(settingReader, "SupportPhone"),
                    ContactAddress = ReadString(settingReader, "ContactAddress"),
                    SupportInstructions = ReadString(settingReader, "SupportInstructions"),
                    SupportHours = ReadString(settingReader, "SupportHours"),
                    ContactUpdatedAtUtc = ReadDateTimeOffset(settingReader, "ContactUpdatedAtUtc"),
                    DefaultLanguage = ReadString(settingReader, "DefaultLanguage"),
                    FallbackLanguage = ReadString(settingReader, "FallbackLanguage"),
                    StorageProvider = ReadString(settingReader, "StorageProvider"),
                    GeofenceRadiusMeters = ReadInt(settingReader, "GeofenceRadiusMeters"),
                    AnalyticsRetentionDays = ReadInt(settingReader, "AnalyticsRetentionDays")
                };
                _logger.LogInformation(
                    "[SystemSettingsSql] loaded current contact settings. id=1; hasPhone={HasPhone}; hasEmail={HasEmail}; hasAddress={HasAddress}; hasComplaintGuide={HasComplaintGuide}; contactUpdatedAtUtc={ContactUpdatedAtUtc}",
                    !string.IsNullOrWhiteSpace(setting.SupportPhone),
                    !string.IsNullOrWhiteSpace(setting.SupportEmail),
                    !string.IsNullOrWhiteSpace(setting.ContactAddress),
                    !string.IsNullOrWhiteSpace(setting.SupportInstructions),
                    setting.ContactUpdatedAtUtc);
            }
            else
            {
                _logger.LogWarning("[SystemSettingsSql] no SystemSettings row found for id=1; returning empty defaults.");
            }
        }

        using (var languagesCommand = CreateCommand(connection, transaction, languagesSql))
        using (var languagesReader = languagesCommand.ExecuteReader())
        {
            while (languagesReader.Read())
            {
                var languageCode = PremiumAccessCatalog.NormalizeLanguageCode(ReadString(languagesReader, "LanguageCode"));

                if (!setting.SupportedLanguages.Contains(languageCode, StringComparer.OrdinalIgnoreCase))
                {
                    setting.SupportedLanguages.Add(languageCode);
                }
            }
        }

        ApplyLegacyOfflinePackageSettingsIfAvailable(connection, transaction, setting);

        return NormalizeSystemSetting(setting, logWarnings: true);
    }

    private void ApplyLegacyOfflinePackageSettingsIfAvailable(
        SqlConnection connection,
        SqlTransaction? transaction,
        SystemSetting setting)
    {
        var hasDownloadsEnabled = ColumnExists(connection, transaction, "SystemSettings", "OfflinePackageDownloadsEnabled");
        var hasMaxSize = ColumnExists(connection, transaction, "SystemSettings", "OfflinePackageMaxSizeMb");
        var hasDescription = ColumnExists(connection, transaction, "SystemSettings", "OfflinePackageDescription");
        if (!hasDownloadsEnabled && !hasMaxSize && !hasDescription)
        {
            setting.OfflinePackageDownloadsEnabled = false;
            setting.OfflinePackageMaxSizeMb = 0;
            setting.OfflinePackageDescription = string.Empty;
            return;
        }

        var downloadsExpression = hasDownloadsEnabled
            ? "COALESCE(OfflinePackageDownloadsEnabled, CAST(0 AS bit))"
            : "CAST(0 AS bit)";
        var maxSizeExpression = hasMaxSize
            ? "COALESCE(OfflinePackageMaxSizeMb, 0)"
            : "0";
        var descriptionExpression = hasDescription
            ? "COALESCE(OfflinePackageDescription, N'')"
            : "N''";

        using var command = CreateCommand(
            connection,
            transaction,
            $"""
            SELECT {downloadsExpression} AS OfflinePackageDownloadsEnabled,
                   {maxSizeExpression} AS OfflinePackageMaxSizeMb,
                   {descriptionExpression} AS OfflinePackageDescription
            FROM dbo.SystemSettings
            WHERE Id = 1;
            """);
        using var reader = command.ExecuteReader();
        if (!reader.Read())
        {
            return;
        }

        setting.OfflinePackageDownloadsEnabled = ReadBool(reader, "OfflinePackageDownloadsEnabled");
        setting.OfflinePackageMaxSizeMb = Math.Max(0, ReadInt(reader, "OfflinePackageMaxSizeMb"));
        setting.OfflinePackageDescription = ReadString(reader, "OfflinePackageDescription").Trim();
    }

    private AdminUser? GetUserByCredentials(SqlConnection connection, SqlTransaction? transaction, string email, string password)
    {
        const string sql = """
            SELECT TOP 1 Id, Name, Email, Phone, Role, [Password], [Status], CreatedAt, LastLoginAt, AvatarColor, ManagedPoiId,
                   ApprovalStatus, RejectionReason, RegistrationSubmittedAt, RegistrationReviewedAt
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
            SELECT TOP 1 Id, Name, Email, Phone, Role, [Password], [Status], CreatedAt, LastLoginAt, AvatarColor, ManagedPoiId,
                   ApprovalStatus, RejectionReason, RegistrationSubmittedAt, RegistrationReviewedAt
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
                userAccount.ManagedPoiId,
                userAccount.ApprovalStatus
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

        var normalizedRole = AdminRoleCatalog.NormalizeRole(ReadString(reader, "Role"));
        if (!AdminRoleCatalog.IsAdminRole(normalizedRole))
        {
            return null;
        }

        if (AdminRoleCatalog.IsPlaceOwner(normalizedRole) &&
            !AdminApprovalCatalog.IsApproved(ReadNullableString(reader, "ApprovalStatus")))
        {
            return null;
        }

        return new AdminRequestContext(
            ReadString(reader, "Id"),
            ReadString(reader, "Name"),
            ReadString(reader, "Email"),
            normalizedRole!,
            ReadString(reader, "Status"),
            ReadNullableString(reader, "ManagedPoiId"));
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
            SELECT TOP 1 Id, EntityType, EntityId, LanguageCode, Title, ShortText, FullText, SeoTitle, SeoDescription,
                   SourceLanguageCode, SourceHash, SourceUpdatedAt, UpdatedBy, UpdatedAt
            FROM dbo.PoiTranslations
            WHERE Id = ?;
            """;

        using var command = CreateCommand(connection, transaction, sql, id);
        using var reader = command.ExecuteReader();
        return reader.Read() ? MapTranslation(reader) : null;
    }

    private AudioGuide? GetAudioGuideById(SqlConnection connection, SqlTransaction? transaction, string id)
    {
        const string sql = """
            SELECT TOP 1
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
        var normalizedLanguageCode = PremiumAccessCatalog.NormalizeLanguageCode(languageCode);
        var normalizedLanguageKey = PremiumAccessCatalog.NormalizeLanguageLookupKey(languageCode);
        var normalizedLanguagePrefix = $"{normalizedLanguageKey}-%";

        const string sql = """
            SELECT TOP 1
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
            WHERE EntityId = ?
              AND (
                    LOWER(REPLACE(LTRIM(RTRIM(LanguageCode)), N'_', N'-')) = ? OR
                    LOWER(REPLACE(LTRIM(RTRIM(LanguageCode)), N'_', N'-')) = ? OR
                    LOWER(REPLACE(LTRIM(RTRIM(LanguageCode)), N'_', N'-')) LIKE ?
              )
              AND (
                    EntityType = ? OR
                    (? = N'poi' AND EntityType = N'place')
              )
            ORDER BY
                CASE WHEN EntityType = ? THEN 0 ELSE 1 END,
                CASE
                    WHEN LOWER(REPLACE(LTRIM(RTRIM(LanguageCode)), N'_', N'-')) = ? THEN 0
                    WHEN LOWER(REPLACE(LTRIM(RTRIM(LanguageCode)), N'_', N'-')) = ? THEN 1
                    ELSE 2
                END,
                UpdatedAt DESC,
                Id DESC;
            """;

        using var command = CreateCommand(
            connection,
            transaction,
            sql,
            entityId,
            normalizedLanguageCode.ToLowerInvariant(),
            normalizedLanguageKey,
            normalizedLanguagePrefix,
            entityType,
            entityType,
            entityType,
            normalizedLanguageCode.ToLowerInvariant(),
            normalizedLanguageKey);
        using var reader = command.ExecuteReader();
        return reader.Read() ? MapAudioGuide(reader) : null;
    }
}
