using Microsoft.Data.SqlClient;

namespace VinhKhanh.BackendApi.Infrastructure;

public sealed partial class AdminDataRepository
{
    public bool UpdateAudioGuideBlobLocation(
        string audioGuideId,
        string audioUrl,
        string audioFilePath,
        string updatedBy)
    {
        using var connection = OpenConnection();
        using var transaction = connection.BeginTransaction();
        var now = DateTimeOffset.UtcNow;
        var existing = GetAudioGuideById(connection, transaction, audioGuideId);
        if (existing is null)
        {
            return false;
        }

        var updated = ExecuteNonQuery(
            connection,
            transaction,
            """
            UPDATE dbo.AudioGuides
            SET AudioUrl = ?,
                AudioFilePath = ?,
                AudioFileName = ?,
                UpdatedBy = ?,
                UpdatedAt = ?
            WHERE Id = ?;
            """,
            audioUrl,
            audioFilePath,
            audioFilePath.Replace('\\', '/').Split('/').LastOrDefault() ?? string.Empty,
            string.IsNullOrWhiteSpace(updatedBy) ? "Blob backfill" : updatedBy,
            now,
            audioGuideId) > 0;

        TouchRelatedPoisForContentEntityChange(
            connection,
            transaction,
            existing.EntityType,
            existing.EntityId,
            existing.EntityType,
            existing.EntityId,
            now);

        transaction.Commit();
        return updated;
    }

    public bool UpdateMediaAssetBlobUrl(string mediaAssetId, string url)
    {
        using var connection = OpenConnection();
        using var transaction = connection.BeginTransaction();
        var now = DateTimeOffset.UtcNow;
        var existing = GetMediaAssetById(connection, transaction, mediaAssetId);
        if (existing is null)
        {
            return false;
        }

        var updated = ExecuteNonQuery(
            connection,
            transaction,
            """
            UPDATE dbo.MediaAssets
            SET Url = ?,
                CreatedAt = ?
            WHERE Id = ?;
            """,
            url,
            now,
            mediaAssetId) > 0;

        TouchRelatedPoisForContentEntityChange(
            connection,
            transaction,
            existing.EntityType,
            existing.EntityId,
            existing.EntityType,
            existing.EntityId,
            now);

        transaction.Commit();
        return updated;
    }

    public bool UpdateFoodItemImageBlobUrl(string foodItemId, string url)
    {
        using var connection = OpenConnection();
        using var transaction = connection.BeginTransaction();
        var now = DateTimeOffset.UtcNow;
        var existing = GetFoodItemById(connection, transaction, foodItemId);
        if (existing is null)
        {
            return false;
        }

        var updated = ExecuteNonQuery(
            connection,
            transaction,
            """
            UPDATE dbo.FoodItems
            SET ImageUrl = ?
            WHERE Id = ?;
            """,
            url,
            foodItemId) > 0;

        TouchPoiUpdatedAt(connection, transaction, existing.PoiId, now);
        transaction.Commit();
        return updated;
    }

    public bool UpdateRouteCoverImageBlobUrl(string routeId, string url)
    {
        using var connection = OpenConnection();
        using var transaction = connection.BeginTransaction();
        if (!ColumnExists(connection, transaction, "Routes", "CoverImageUrl"))
        {
            return false;
        }

        var now = DateTimeOffset.UtcNow;
        var updated = ExecuteNonQuery(
            connection,
            transaction,
            """
            UPDATE dbo.Routes
            SET CoverImageUrl = ?,
                UpdatedAt = ?
            WHERE Id = ?;
            """,
            url,
            now,
            routeId) > 0;

        transaction.Commit();
        return updated;
    }
}
