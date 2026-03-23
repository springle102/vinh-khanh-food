using Microsoft.Data.SqlClient;
using VinhKhanh.BackendApi.Contracts;
using VinhKhanh.BackendApi.Models;

namespace VinhKhanh.BackendApi.Infrastructure;

public sealed partial class AdminDataRepository
{
    public MediaAsset SaveMediaAsset(string? id, MediaAssetUpsertRequest request)
    {
        using var connection = OpenConnection();
        using var transaction = connection.BeginTransaction();

        var existing = !string.IsNullOrWhiteSpace(id) ? GetMediaAssetById(connection, transaction, id) : null;
        var isNew = existing is null;
        var mediaId = existing?.Id ?? id ?? CreateId("media");
        var createdAt = existing?.CreatedAt ?? DateTimeOffset.UtcNow;

        if (isNew)
        {
            ExecuteNonQuery(
                connection,
                transaction,
                """
                INSERT INTO dbo.MediaAssets (Id, EntityType, EntityId, MediaType, Url, AltText, CreatedAt)
                VALUES (?, ?, ?, ?, ?, ?, ?);
                """,
                mediaId,
                request.EntityType,
                request.EntityId,
                request.Type,
                request.Url,
                request.AltText,
                createdAt);
        }
        else
        {
            ExecuteNonQuery(
                connection,
                transaction,
                """
                UPDATE dbo.MediaAssets
                SET EntityType = ?,
                    EntityId = ?,
                    MediaType = ?,
                    Url = ?,
                    AltText = ?
                WHERE Id = ?;
                """,
                request.EntityType,
                request.EntityId,
                request.Type,
                request.Url,
                request.AltText,
                mediaId);
        }

        AppendAuditLog(connection, transaction, "SYSTEM", "SYSTEM", isNew ? "Tao media asset" : "Cap nhat media asset", mediaId);

        var saved = GetMediaAssetById(connection, transaction, mediaId)
            ?? throw new InvalidOperationException("Khong the doc lai media asset sau khi luu.");

        transaction.Commit();
        return saved;
    }

    public bool DeleteMediaAsset(string id)
    {
        using var connection = OpenConnection();
        using var transaction = connection.BeginTransaction();

        var deleted = ExecuteNonQuery(connection, transaction, "DELETE FROM dbo.MediaAssets WHERE Id = ?;", id) > 0;
        if (deleted)
        {
            AppendAuditLog(connection, transaction, "SYSTEM", "SYSTEM", "Xoa media asset", id);
        }

        transaction.Commit();
        return deleted;
    }

    public FoodItem SaveFoodItem(string? id, FoodItemUpsertRequest request)
    {
        using var connection = OpenConnection();
        using var transaction = connection.BeginTransaction();

        var existing = !string.IsNullOrWhiteSpace(id) ? GetFoodItemById(connection, transaction, id) : null;
        var isNew = existing is null;
        var foodId = existing?.Id ?? id ?? CreateId("food");

        if (isNew)
        {
            ExecuteNonQuery(
                connection,
                transaction,
                """
                INSERT INTO dbo.FoodItems (Id, PlaceId, Name, [Description], PriceRange, ImageUrl, SpicyLevel)
                VALUES (?, ?, ?, ?, ?, ?, ?);
                """,
                foodId,
                request.PlaceId,
                request.Name,
                request.Description,
                request.PriceRange,
                request.ImageUrl,
                request.SpicyLevel);
        }
        else
        {
            ExecuteNonQuery(
                connection,
                transaction,
                """
                UPDATE dbo.FoodItems
                SET PlaceId = ?,
                    Name = ?,
                    [Description] = ?,
                    PriceRange = ?,
                    ImageUrl = ?,
                    SpicyLevel = ?
                WHERE Id = ?;
                """,
                request.PlaceId,
                request.Name,
                request.Description,
                request.PriceRange,
                request.ImageUrl,
                request.SpicyLevel,
                foodId);
        }

        AppendAuditLog(connection, transaction, "SYSTEM", "SYSTEM", isNew ? "Tao mon an" : "Cap nhat mon an", request.Name);

        var saved = GetFoodItemById(connection, transaction, foodId)
            ?? throw new InvalidOperationException("Khong the doc lai mon an sau khi luu.");

        transaction.Commit();
        return saved;
    }

    public bool DeleteFoodItem(string id)
    {
        using var connection = OpenConnection();
        using var transaction = connection.BeginTransaction();

        var deleted = ExecuteNonQuery(connection, transaction, "DELETE FROM dbo.FoodItems WHERE Id = ?;", id) > 0;
        if (deleted)
        {
            AppendAuditLog(connection, transaction, "SYSTEM", "SYSTEM", "Xoa mon an", id);
        }

        transaction.Commit();
        return deleted;
    }

    public TourRoute SaveRoute(string? id, TourRouteUpsertRequest request)
    {
        using var connection = OpenConnection();
        using var transaction = connection.BeginTransaction();

        var existing = !string.IsNullOrWhiteSpace(id) ? GetRouteById(connection, transaction, id) : null;
        var isNew = existing is null;
        var routeId = existing?.Id ?? id ?? CreateId("route");

        if (isNew)
        {
            ExecuteNonQuery(
                connection,
                transaction,
                """
                INSERT INTO dbo.Routes (Id, Name, [Description], DurationMinutes, Difficulty, IsFeatured)
                VALUES (?, ?, ?, ?, ?, ?);
                """,
                routeId,
                request.Name,
                request.Description,
                request.DurationMinutes,
                request.Difficulty,
                request.IsFeatured);
        }
        else
        {
            ExecuteNonQuery(
                connection,
                transaction,
                """
                UPDATE dbo.Routes
                SET Name = ?,
                    [Description] = ?,
                    DurationMinutes = ?,
                    Difficulty = ?,
                    IsFeatured = ?
                WHERE Id = ?;
                """,
                request.Name,
                request.Description,
                request.DurationMinutes,
                request.Difficulty,
                request.IsFeatured,
                routeId);
        }

        ReplaceRouteStops(connection, transaction, routeId, request.StopPlaceIds);

        AppendAuditLog(
            connection,
            transaction,
            request.ActorName,
            request.ActorRole,
            isNew ? "Tao tuyen tham quan" : "Cap nhat tuyen tham quan",
            request.Name);

        var saved = GetRouteById(connection, transaction, routeId)
            ?? throw new InvalidOperationException("Khong the doc lai tuyen tham quan sau khi luu.");

        transaction.Commit();
        return saved;
    }

    public bool DeleteRoute(string id)
    {
        using var connection = OpenConnection();
        using var transaction = connection.BeginTransaction();

        ExecuteNonQuery(connection, transaction, "DELETE FROM dbo.RouteStops WHERE RouteId = ?;", id);
        var deleted = ExecuteNonQuery(connection, transaction, "DELETE FROM dbo.Routes WHERE Id = ?;", id) > 0;
        if (!deleted)
        {
            transaction.Rollback();
            return false;
        }

        AppendAuditLog(connection, transaction, "SYSTEM", "SYSTEM", "Xoa tuyen tham quan", id);

        transaction.Commit();
        return true;
    }

    public Promotion SavePromotion(string? id, PromotionUpsertRequest request)
    {
        using var connection = OpenConnection();
        using var transaction = connection.BeginTransaction();

        var existing = !string.IsNullOrWhiteSpace(id) ? GetPromotionById(connection, transaction, id) : null;
        var isNew = existing is null;
        var promotionId = existing?.Id ?? id ?? CreateId("promo");

        if (isNew)
        {
            ExecuteNonQuery(
                connection,
                transaction,
                """
                INSERT INTO dbo.Promotions (Id, PlaceId, Title, [Description], StartAt, EndAt, [Status])
                VALUES (?, ?, ?, ?, ?, ?, ?);
                """,
                promotionId,
                request.PlaceId,
                request.Title,
                request.Description,
                request.StartAt,
                request.EndAt,
                request.Status);
        }
        else
        {
            ExecuteNonQuery(
                connection,
                transaction,
                """
                UPDATE dbo.Promotions
                SET PlaceId = ?,
                    Title = ?,
                    [Description] = ?,
                    StartAt = ?,
                    EndAt = ?,
                    [Status] = ?
                WHERE Id = ?;
                """,
                request.PlaceId,
                request.Title,
                request.Description,
                request.StartAt,
                request.EndAt,
                request.Status,
                promotionId);
        }

        AppendAuditLog(
            connection,
            transaction,
            request.ActorName,
            request.ActorRole,
            isNew ? "Tao uu dai" : "Cap nhat uu dai",
            request.Title);

        var saved = GetPromotionById(connection, transaction, promotionId)
            ?? throw new InvalidOperationException("Khong the doc lai uu dai sau khi luu.");

        transaction.Commit();
        return saved;
    }

    public bool DeletePromotion(string id)
    {
        using var connection = OpenConnection();
        using var transaction = connection.BeginTransaction();

        var deleted = ExecuteNonQuery(connection, transaction, "DELETE FROM dbo.Promotions WHERE Id = ?;", id) > 0;
        if (deleted)
        {
            AppendAuditLog(connection, transaction, "SYSTEM", "SYSTEM", "Xoa uu dai", id);
        }

        transaction.Commit();
        return deleted;
    }

    public Review CreateReview(ReviewCreateRequest request)
    {
        using var connection = OpenConnection();
        using var transaction = connection.BeginTransaction();

        var review = new Review
        {
            Id = CreateId("review"),
            PlaceId = request.PlaceId,
            UserName = string.IsNullOrWhiteSpace(request.UserName) ? "Guest" : request.UserName,
            Rating = request.Rating,
            Comment = request.Comment,
            LanguageCode = string.IsNullOrWhiteSpace(request.LanguageCode) ? "vi" : request.LanguageCode,
            CreatedAt = DateTimeOffset.UtcNow,
            Status = "pending"
        };

        ExecuteNonQuery(
            connection,
            transaction,
            """
            INSERT INTO dbo.Reviews (Id, PlaceId, UserName, Rating, CommentText, LanguageCode, CreatedAt, [Status])
            VALUES (?, ?, ?, ?, ?, ?, ?, ?);
            """,
            review.Id,
            review.PlaceId,
            review.UserName,
            review.Rating,
            review.Comment,
            review.LanguageCode,
            review.CreatedAt,
            review.Status);

        AppendAuditLog(connection, transaction, review.UserName, "CUSTOMER", "Tao danh gia", review.PlaceId);

        transaction.Commit();
        return review;
    }

    public Review? UpdateReviewStatus(string id, ReviewStatusRequest request)
    {
        using var connection = OpenConnection();
        using var transaction = connection.BeginTransaction();

        var existing = GetReviewById(connection, transaction, id);
        if (existing is null)
        {
            transaction.Rollback();
            return null;
        }

        ExecuteNonQuery(connection, transaction, "UPDATE dbo.Reviews SET [Status] = ? WHERE Id = ?;", request.Status, id);
        AppendAuditLog(connection, transaction, request.ActorName, request.ActorRole, "Cap nhat trang thai danh gia", id);

        var saved = GetReviewById(connection, transaction, id);
        transaction.Commit();
        return saved;
    }

    public QRCodeRecord? UpdateQrState(string id, QrCodeStateRequest request)
    {
        using var connection = OpenConnection();
        using var transaction = connection.BeginTransaction();

        var existing = GetQrCodeById(connection, transaction, id);
        if (existing is null)
        {
            transaction.Rollback();
            return null;
        }

        ExecuteNonQuery(connection, transaction, "UPDATE dbo.QRCodes SET IsActive = ? WHERE Id = ?;", request.IsActive, id);
        AppendAuditLog(connection, transaction, request.ActorName, request.ActorRole, "Cap nhat trang thai QR", id);

        var saved = GetQrCodeById(connection, transaction, id);
        transaction.Commit();
        return saved;
    }

    public QRCodeRecord? UpdateQrImage(string id, QrCodeImageRequest request)
    {
        using var connection = OpenConnection();
        using var transaction = connection.BeginTransaction();

        var existing = GetQrCodeById(connection, transaction, id);
        if (existing is null)
        {
            transaction.Rollback();
            return null;
        }

        ExecuteNonQuery(connection, transaction, "UPDATE dbo.QRCodes SET QrImageUrl = ? WHERE Id = ?;", request.QrImageUrl, id);
        AppendAuditLog(connection, transaction, request.ActorName, request.ActorRole, "Cap nhat anh QR", id);

        var saved = GetQrCodeById(connection, transaction, id);
        transaction.Commit();
        return saved;
    }

    public SystemSetting SaveSettings(SystemSettingUpsertRequest request)
    {
        using var connection = OpenConnection();
        using var transaction = connection.BeginTransaction();

        var exists = ExecuteScalarInt(connection, transaction, "SELECT COUNT(*) FROM dbo.SystemSettings WHERE Id = 1;") > 0;
        if (exists)
        {
            ExecuteNonQuery(
                connection,
                transaction,
                """
                UPDATE dbo.SystemSettings
                SET AppName = ?,
                    SupportEmail = ?,
                    DefaultLanguage = ?,
                    FallbackLanguage = ?,
                    PremiumUnlockPriceUsd = ?,
                    MapProvider = ?,
                    StorageProvider = ?,
                    TtsProvider = ?,
                    GeofenceRadiusMeters = ?,
                    QrAutoPlay = ?,
                    GuestReviewEnabled = ?,
                    AnalyticsRetentionDays = ?
                WHERE Id = 1;
                """,
                request.AppName,
                request.SupportEmail,
                request.DefaultLanguage,
                request.FallbackLanguage,
                request.PremiumUnlockPriceUsd,
                request.MapProvider,
                request.StorageProvider,
                request.TtsProvider,
                request.GeofenceRadiusMeters,
                request.QrAutoPlay,
                request.GuestReviewEnabled,
                request.AnalyticsRetentionDays);
        }
        else
        {
            ExecuteNonQuery(
                connection,
                transaction,
                """
                INSERT INTO dbo.SystemSettings (
                    Id, AppName, SupportEmail, DefaultLanguage, FallbackLanguage, PremiumUnlockPriceUsd,
                    MapProvider, StorageProvider, TtsProvider, GeofenceRadiusMeters, QrAutoPlay, GuestReviewEnabled, AnalyticsRetentionDays
                )
                VALUES (1, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?);
                """,
                request.AppName,
                request.SupportEmail,
                request.DefaultLanguage,
                request.FallbackLanguage,
                request.PremiumUnlockPriceUsd,
                request.MapProvider,
                request.StorageProvider,
                request.TtsProvider,
                request.GeofenceRadiusMeters,
                request.QrAutoPlay,
                request.GuestReviewEnabled,
                request.AnalyticsRetentionDays);
        }

        ReplaceSettingLanguages(connection, transaction, "free", request.FreeLanguages);
        ReplaceSettingLanguages(connection, transaction, "premium", request.PremiumLanguages);

        AppendAuditLog(connection, transaction, request.ActorName, request.ActorRole, "Cap nhat cai dat he thong", request.AppName);

        var saved = GetSettings(connection, transaction);
        transaction.Commit();
        return saved;
    }

    private void ReplacePlaceTags(SqlConnection connection, SqlTransaction transaction, string placeId, IEnumerable<string>? tags)
    {
        ExecuteNonQuery(connection, transaction, "DELETE FROM dbo.PlaceTags WHERE PlaceId = ?;", placeId);

        foreach (var tag in NormalizeList(tags))
        {
            ExecuteNonQuery(connection, transaction, "INSERT INTO dbo.PlaceTags (PlaceId, TagValue) VALUES (?, ?);", placeId, tag);
        }
    }

    private void ReplaceRouteStops(SqlConnection connection, SqlTransaction transaction, string routeId, IEnumerable<string>? stopPlaceIds)
    {
        ExecuteNonQuery(connection, transaction, "DELETE FROM dbo.RouteStops WHERE RouteId = ?;", routeId);

        var order = 1;
        foreach (var stopPlaceId in NormalizeList(stopPlaceIds, distinct: false))
        {
            ExecuteNonQuery(
                connection,
                transaction,
                "INSERT INTO dbo.RouteStops (RouteId, StopOrder, PlaceId) VALUES (?, ?, ?);",
                routeId,
                order,
                stopPlaceId);
            order++;
        }
    }

    private void ReplaceSettingLanguages(SqlConnection connection, SqlTransaction transaction, string languageType, IEnumerable<string>? languageCodes)
    {
        ExecuteNonQuery(
            connection,
            transaction,
            "DELETE FROM dbo.SystemSettingLanguages WHERE SettingId = 1 AND LanguageType = ?;",
            languageType);

        foreach (var languageCode in NormalizeList(languageCodes))
        {
            ExecuteNonQuery(
                connection,
                transaction,
                "INSERT INTO dbo.SystemSettingLanguages (SettingId, LanguageType, LanguageCode) VALUES (1, ?, ?);",
                languageType,
                languageCode);
        }
    }

    private void UpsertPlaceQr(SqlConnection connection, SqlTransaction transaction, string placeId, string slug, string status)
    {
        var existing = GetQrCodeByEntity(connection, transaction, "place", placeId);
        var qrValue = $"https://guide.vinhkhanh.vn/scan/{slug}";
        var qrImageUrl = $"https://api.qrserver.com/v1/create-qr-code/?size=320x320&data={Uri.EscapeDataString(qrValue)}";
        var isActive = string.Equals(status, "published", StringComparison.OrdinalIgnoreCase);

        if (existing is null)
        {
            ExecuteNonQuery(
                connection,
                transaction,
                """
                INSERT INTO dbo.QRCodes (Id, EntityType, EntityId, QrValue, QrImageUrl, IsActive, LastScanAt)
                VALUES (?, ?, ?, ?, ?, ?, ?);
                """,
                CreateId("qr"),
                "place",
                placeId,
                qrValue,
                qrImageUrl,
                isActive,
                null);

            return;
        }

        ExecuteNonQuery(
            connection,
            transaction,
            """
            UPDATE dbo.QRCodes
            SET QrValue = ?,
                QrImageUrl = ?,
                IsActive = ?
            WHERE Id = ?;
            """,
            qrValue,
            qrImageUrl,
            isActive,
            existing.Id);
    }

    private AuthTokensResponse CreateSession(SqlConnection connection, SqlTransaction transaction, AdminUser user)
    {
        var accessToken = $"vk_access_{Guid.NewGuid():N}";
        var refreshToken = $"vk_refresh_{Guid.NewGuid():N}";
        var now = DateTimeOffset.UtcNow;
        var expiresAt = now.AddHours(8);
        var refreshExpiresAt = now.AddDays(30);

        ExecuteNonQuery(connection, transaction, "DELETE FROM dbo.RefreshSessions WHERE ExpiresAt <= ?;", now);
        ExecuteNonQuery(
            connection,
            transaction,
            "INSERT INTO dbo.RefreshSessions (RefreshToken, UserId, ExpiresAt) VALUES (?, ?, ?);",
            refreshToken,
            user.Id,
            refreshExpiresAt);

        return new AuthTokensResponse(
            user.Id,
            user.Name,
            user.Email,
            user.Role,
            accessToken,
            refreshToken,
            expiresAt);
    }

    private void AppendAuditLog(
        SqlConnection connection,
        SqlTransaction transaction,
        string actorName,
        string actorRole,
        string action,
        string target)
    {
        ExecuteNonQuery(
            connection,
            transaction,
            """
            INSERT INTO dbo.AuditLogs (Id, ActorName, ActorRole, [Action], TargetValue, CreatedAt)
            VALUES (?, ?, ?, ?, ?, ?);
            """,
            CreateId("audit"),
            actorName,
            actorRole,
            action,
            target,
            DateTimeOffset.UtcNow);

        ExecuteNonQuery(
            connection,
            transaction,
            """
            DELETE FROM dbo.AuditLogs
            WHERE Id IN (
                SELECT Id
                FROM (
                    SELECT Id, ROW_NUMBER() OVER (ORDER BY CreatedAt DESC, Id DESC) AS RowNumber
                    FROM dbo.AuditLogs
                ) AS OrderedLogs
                WHERE RowNumber > ?
            );
            """,
            MaxAuditLogs);
    }
}
