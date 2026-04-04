using System.Security.Cryptography;
using System.Text;
using Microsoft.Data.SqlClient;
using VinhKhanh.BackendApi.Contracts;

namespace VinhKhanh.BackendApi.Infrastructure;

public sealed partial class AdminDataRepository
{
    public DataSyncState GetSyncState()
    {
        using var connection = OpenConnection();
        return GetSyncState(connection, null);
    }

    private DataSyncState GetSyncState(SqlConnection connection, SqlTransaction? transaction)
    {
        const string sql = """
            SELECT
                (SELECT COUNT(*) FROM dbo.CustomerUsers) AS CustomerUserCount,
                (SELECT COUNT(*) FROM dbo.Categories) AS CategoryCount,
                (SELECT COUNT(*) FROM dbo.Pois) AS PoiCount,
                (SELECT COUNT(*) FROM dbo.PoiTranslations) AS TranslationCount,
                (SELECT COUNT(*) FROM dbo.AudioGuides) AS AudioGuideCount,
                (SELECT COUNT(*) FROM dbo.MediaAssets) AS MediaAssetCount,
                (SELECT COUNT(*) FROM dbo.FoodItems) AS FoodItemCount,
                (SELECT COUNT(*) FROM dbo.Routes) AS RouteCount,
                (SELECT COUNT(*) FROM dbo.Promotions) AS PromotionCount,
                (SELECT COUNT(*) FROM dbo.Reviews) AS ReviewCount,
                (SELECT COUNT(*) FROM dbo.ViewLogs) AS ViewLogCount,
                (SELECT COUNT(*) FROM dbo.AudioListenLogs) AS AudioListenLogCount,
                (SELECT COUNT(*) FROM dbo.AuditLogs) AS AuditLogCount,
                (SELECT MAX(COALESCE(LastActiveAt, CreatedAt)) FROM dbo.CustomerUsers) AS LatestCustomerUserAt,
                (SELECT MAX(UpdatedAt) FROM dbo.Pois) AS LatestPoiAt,
                (SELECT MAX(UpdatedAt) FROM dbo.PoiTranslations) AS LatestTranslationAt,
                (SELECT MAX(UpdatedAt) FROM dbo.AudioGuides) AS LatestAudioGuideAt,
                (SELECT MAX(CreatedAt) FROM dbo.MediaAssets) AS LatestMediaAssetAt,
                (SELECT MAX(UpdatedAt) FROM dbo.Routes) AS LatestRouteAt,
                (SELECT MAX(CreatedAt) FROM dbo.Reviews) AS LatestReviewAt,
                (SELECT MAX(ViewedAt) FROM dbo.ViewLogs) AS LatestViewLogAt,
                (SELECT MAX(ListenedAt) FROM dbo.AudioListenLogs) AS LatestAudioListenAt,
                (SELECT MAX(CreatedAt) FROM dbo.AuditLogs) AS LatestAuditAt;
            """;

        using var command = CreateCommand(connection, transaction, sql);
        using var reader = command.ExecuteReader();

        if (!reader.Read())
        {
            var now = DateTimeOffset.UtcNow;
            return new DataSyncState("bootstrap-empty", now, now);
        }

        var latestCustomerUserAt = ReadNullableDateTimeOffset(reader, "LatestCustomerUserAt");
        var latestPoiAt = ReadNullableDateTimeOffset(reader, "LatestPoiAt");
        var latestTranslationAt = ReadNullableDateTimeOffset(reader, "LatestTranslationAt");
        var latestAudioGuideAt = ReadNullableDateTimeOffset(reader, "LatestAudioGuideAt");
        var latestMediaAssetAt = ReadNullableDateTimeOffset(reader, "LatestMediaAssetAt");
        var latestRouteAt = ReadNullableDateTimeOffset(reader, "LatestRouteAt");
        var latestReviewAt = ReadNullableDateTimeOffset(reader, "LatestReviewAt");
        var latestViewLogAt = ReadNullableDateTimeOffset(reader, "LatestViewLogAt");
        var latestAudioListenAt = ReadNullableDateTimeOffset(reader, "LatestAudioListenAt");
        var latestAuditAt = ReadNullableDateTimeOffset(reader, "LatestAuditAt");
        var generatedAt = DateTimeOffset.UtcNow;
        var lastChangedAt =
            new[]
            {
                latestCustomerUserAt,
                latestPoiAt,
                latestTranslationAt,
                latestAudioGuideAt,
                latestMediaAssetAt,
                latestRouteAt,
                latestReviewAt,
                latestViewLogAt,
                latestAudioListenAt,
                latestAuditAt,
            }
            .Where(value => value.HasValue)
            .Select(value => value!.Value)
            .DefaultIfEmpty(generatedAt)
            .Max();

        var versionParts = new[]
        {
            $"customers={ReadInt(reader, "CustomerUserCount")}",
            $"categories={ReadInt(reader, "CategoryCount")}",
            $"pois={ReadInt(reader, "PoiCount")}",
            $"translations={ReadInt(reader, "TranslationCount")}",
            $"audioGuides={ReadInt(reader, "AudioGuideCount")}",
            $"mediaAssets={ReadInt(reader, "MediaAssetCount")}",
            $"foodItems={ReadInt(reader, "FoodItemCount")}",
            $"routes={ReadInt(reader, "RouteCount")}",
            $"promotions={ReadInt(reader, "PromotionCount")}",
            $"reviews={ReadInt(reader, "ReviewCount")}",
            $"viewLogs={ReadInt(reader, "ViewLogCount")}",
            $"audioListens={ReadInt(reader, "AudioListenLogCount")}",
            $"auditLogs={ReadInt(reader, "AuditLogCount")}",
            $"latestCustomer={FormatVersionPart(latestCustomerUserAt)}",
            $"latestPoi={FormatVersionPart(latestPoiAt)}",
            $"latestTranslation={FormatVersionPart(latestTranslationAt)}",
            $"latestAudioGuide={FormatVersionPart(latestAudioGuideAt)}",
            $"latestMediaAsset={FormatVersionPart(latestMediaAssetAt)}",
            $"latestRoute={FormatVersionPart(latestRouteAt)}",
            $"latestReview={FormatVersionPart(latestReviewAt)}",
            $"latestView={FormatVersionPart(latestViewLogAt)}",
            $"latestListen={FormatVersionPart(latestAudioListenAt)}",
            $"latestAudit={FormatVersionPart(latestAuditAt)}",
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
