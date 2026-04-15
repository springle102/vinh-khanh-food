using System.Security.Cryptography;
using System.Text;
using Microsoft.Data.SqlClient;
using VinhKhanh.BackendApi.Contracts;

namespace VinhKhanh.BackendApi.Infrastructure;

public sealed partial class AdminDataRepository
{
    private const string SyncSchemaVersion = "public-app-usage-v1";

    public DataSyncState GetSyncState()
    {
        using var connection = OpenConnection();
        return GetSyncState(connection, null);
    }

    private DataSyncState GetSyncState(SqlConnection connection, SqlTransaction? transaction)
    {
        var useSeparatedAuditLogs = HasAdminAuditLogTable(connection, transaction);
        var hasLegacyAuditLogs = HasLegacyAuditLogTable(connection, transaction);
        var useUserActivityLogs = HasUserActivityLogTable(connection, transaction);

        var auditLogCountSql = useSeparatedAuditLogs
            ? "(SELECT COUNT(*) FROM dbo.AdminAuditLogs) AS AuditLogCount,"
            : hasLegacyAuditLogs
                ? "(SELECT COUNT(*) FROM dbo.AuditLogs legacy WHERE UPPER(COALESCE(legacy.ActorRole, N'')) IN (N'SUPER_ADMIN', N'SUPERADMIN', N'PLACE_OWNER', N'PLACEOWNER', N'SYSTEM')) AS AuditLogCount,"
                : "CAST(0 AS INT) AS AuditLogCount,";
        var userActivityCountSql = useUserActivityLogs
            ? "(SELECT COUNT(*) FROM dbo.UserActivityLogs) AS UserActivityLogCount,"
            : hasLegacyAuditLogs
                ? "(SELECT COUNT(*) FROM dbo.AuditLogs legacy WHERE UPPER(COALESCE(legacy.ActorRole, N'')) NOT IN (N'SUPER_ADMIN', N'SUPERADMIN', N'PLACE_OWNER', N'PLACEOWNER', N'SYSTEM')) AS UserActivityLogCount,"
                : "CAST(0 AS INT) AS UserActivityLogCount,";
        var latestAuditSql = useSeparatedAuditLogs
            ? "(SELECT MAX(CreatedAt) FROM dbo.AdminAuditLogs) AS LatestAuditAt,"
            : hasLegacyAuditLogs
                ? "(SELECT MAX(CreatedAt) FROM dbo.AuditLogs legacy WHERE UPPER(COALESCE(legacy.ActorRole, N'')) IN (N'SUPER_ADMIN', N'SUPERADMIN', N'PLACE_OWNER', N'PLACEOWNER', N'SYSTEM')) AS LatestAuditAt,"
                : "CAST(NULL AS DATETIMEOFFSET(7)) AS LatestAuditAt,";
        var latestUserActivitySql = useUserActivityLogs
            ? "(SELECT MAX(CreatedAt) FROM dbo.UserActivityLogs) AS LatestUserActivityAt"
            : hasLegacyAuditLogs
                ? "(SELECT MAX(CreatedAt) FROM dbo.AuditLogs legacy WHERE UPPER(COALESCE(legacy.ActorRole, N'')) NOT IN (N'SUPER_ADMIN', N'SUPERADMIN', N'PLACE_OWNER', N'PLACEOWNER', N'SYSTEM')) AS LatestUserActivityAt"
                : "CAST(NULL AS DATETIMEOFFSET(7)) AS LatestUserActivityAt";

        var sql = $"""
            SELECT
                (SELECT COUNT(*) FROM dbo.Categories) AS CategoryCount,
                (SELECT COUNT(*) FROM dbo.Pois) AS PoiCount,
                (SELECT COUNT(*) FROM dbo.PoiTranslations) AS TranslationCount,
                (SELECT COUNT(*) FROM dbo.AudioGuides) AS AudioGuideCount,
                (SELECT COUNT(*) FROM dbo.MediaAssets) AS MediaAssetCount,
                (SELECT COUNT(*) FROM dbo.FoodItems) AS FoodItemCount,
                (SELECT COUNT(*) FROM dbo.Routes) AS RouteCount,
                (SELECT COUNT(*) FROM dbo.Promotions) AS PromotionCount,
                (SELECT COUNT(*) FROM dbo.AppUsageEvents) AS AppUsageEventCount,
                {auditLogCountSql}
                {userActivityCountSql}
                (SELECT MAX(UpdatedAt) FROM dbo.Pois) AS LatestPoiAt,
                (SELECT MAX(UpdatedAt) FROM dbo.PoiTranslations) AS LatestTranslationAt,
                (SELECT MAX(UpdatedAt) FROM dbo.AudioGuides) AS LatestAudioGuideAt,
                (SELECT MAX(CreatedAt) FROM dbo.MediaAssets) AS LatestMediaAssetAt,
                (SELECT MAX(UpdatedAt) FROM dbo.Routes) AS LatestRouteAt,
                (SELECT MAX(OccurredAt) FROM dbo.AppUsageEvents) AS LatestAppUsageEventAt,
                {latestAuditSql}
                {latestUserActivitySql};
            """;

        using var command = CreateCommand(connection, transaction, sql);
        using var reader = command.ExecuteReader();

        if (!reader.Read())
        {
            var now = DateTimeOffset.UtcNow;
            return new DataSyncState("bootstrap-empty", now, now);
        }

        var latestPoiAt = ReadNullableDateTimeOffset(reader, "LatestPoiAt");
        var latestTranslationAt = ReadNullableDateTimeOffset(reader, "LatestTranslationAt");
        var latestAudioGuideAt = ReadNullableDateTimeOffset(reader, "LatestAudioGuideAt");
        var latestMediaAssetAt = ReadNullableDateTimeOffset(reader, "LatestMediaAssetAt");
        var latestRouteAt = ReadNullableDateTimeOffset(reader, "LatestRouteAt");
        var latestAppUsageEventAt = ReadNullableDateTimeOffset(reader, "LatestAppUsageEventAt");
        var latestAuditAt = ReadNullableDateTimeOffset(reader, "LatestAuditAt");
        var latestUserActivityAt = ReadNullableDateTimeOffset(reader, "LatestUserActivityAt");
        var generatedAt = DateTimeOffset.UtcNow;
        var lastChangedAt =
            new[]
            {
                latestPoiAt,
                latestTranslationAt,
                latestAudioGuideAt,
                latestMediaAssetAt,
                latestRouteAt,
                latestAppUsageEventAt,
                latestAuditAt,
                latestUserActivityAt,
            }
            .Where(value => value.HasValue)
            .Select(value => value!.Value)
            .DefaultIfEmpty(generatedAt)
            .Max();

        var versionParts = new[]
        {
            $"schema={SyncSchemaVersion}",
            $"categories={ReadInt(reader, "CategoryCount")}",
            $"pois={ReadInt(reader, "PoiCount")}",
            $"translations={ReadInt(reader, "TranslationCount")}",
            $"audioGuides={ReadInt(reader, "AudioGuideCount")}",
            $"mediaAssets={ReadInt(reader, "MediaAssetCount")}",
            $"foodItems={ReadInt(reader, "FoodItemCount")}",
            $"routes={ReadInt(reader, "RouteCount")}",
            $"promotions={ReadInt(reader, "PromotionCount")}",
            $"usageEvents={ReadInt(reader, "AppUsageEventCount")}",
            $"auditLogs={ReadInt(reader, "AuditLogCount")}",
            $"userActivityLogs={ReadInt(reader, "UserActivityLogCount")}",
            $"latestPoi={FormatVersionPart(latestPoiAt)}",
            $"latestTranslation={FormatVersionPart(latestTranslationAt)}",
            $"latestAudioGuide={FormatVersionPart(latestAudioGuideAt)}",
            $"latestMediaAsset={FormatVersionPart(latestMediaAssetAt)}",
            $"latestRoute={FormatVersionPart(latestRouteAt)}",
            $"latestUsage={FormatVersionPart(latestAppUsageEventAt)}",
            $"latestAudit={FormatVersionPart(latestAuditAt)}",
            $"latestUserActivity={FormatVersionPart(latestUserActivityAt)}",
        };

        return new DataSyncState(
            CreateSyncVersion(versionParts),
            generatedAt,
            lastChangedAt);
    }

    private static string FormatVersionPart(DateTimeOffset? value)
        => value?.ToUniversalTime().Ticks.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "0";

    private static string CreateSyncVersion(IEnumerable<string> parts)
    {
        var raw = string.Join("|", parts);
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(raw));
        return Convert.ToHexString(hash[..8]).ToLowerInvariant();
    }
}
