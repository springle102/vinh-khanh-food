using Microsoft.Data.SqlClient;
using VinhKhanh.BackendApi.Contracts;
using VinhKhanh.BackendApi.Models;

namespace VinhKhanh.BackendApi.Infrastructure;

public sealed partial class AdminDataRepository
{
    public EndUser? UpdateEndUserStatus(string id, EndUserStatusUpdateRequest request)
    {
        using var connection = OpenConnection();
        using var transaction = connection.BeginTransaction();

        var existing = GetEndUserById(connection, transaction, id);
        if (existing is null)
        {
            transaction.Rollback();
            return null;
        }

        ExecuteNonQuery(
            connection,
            transaction,
            """
            UPDATE dbo.CustomerUsers
            SET IsBanned = ?,
                [Status] = CASE
                    WHEN ? = CAST(1 AS bit) THEN N'banned'
                    WHEN IsActive = CAST(1 AS bit) THEN N'active'
                    ELSE N'inactive'
                END
            WHERE Id = ?;
            """,
            request.IsBanned,
            request.IsBanned,
            id);

        AppendAuditLog(
            connection,
            transaction,
            request.ActorName,
            request.ActorRole,
            request.IsBanned ? "Khóa người dùng cuối" : "Mở khóa người dùng cuối",
            id);

        var saved = GetEndUserById(connection, transaction, id);
        transaction.Commit();
        return saved;
    }

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

        AppendAuditLog(connection, transaction, "SYSTEM", "SYSTEM", isNew ? "Tạo media asset" : "Cập nhật media asset", mediaId);

        var saved = GetMediaAssetById(connection, transaction, mediaId)
            ?? throw new InvalidOperationException("Không thể đọc lại media asset sau khi lưu.");

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
            AppendAuditLog(connection, transaction, "SYSTEM", "SYSTEM", "Xóa media asset", id);
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
                INSERT INTO dbo.FoodItems (Id, PoiId, Name, [Description], PriceRange, ImageUrl, SpicyLevel)
                VALUES (?, ?, ?, ?, ?, ?, ?);
                """,
                foodId,
                request.PoiId,
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
                SET PoiId = ?,
                    Name = ?,
                    [Description] = ?,
                    PriceRange = ?,
                    ImageUrl = ?,
                    SpicyLevel = ?
                WHERE Id = ?;
                """,
                request.PoiId,
                request.Name,
                request.Description,
                request.PriceRange,
                request.ImageUrl,
                request.SpicyLevel,
                foodId);
        }

        AppendAuditLog(connection, transaction, "SYSTEM", "SYSTEM", isNew ? "Tạo món ăn" : "Cập nhật món ăn", request.Name);

        var saved = GetFoodItemById(connection, transaction, foodId)
            ?? throw new InvalidOperationException("Không thể đọc lại món ăn sau khi lưu.");

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
            AppendAuditLog(connection, transaction, "SYSTEM", "SYSTEM", "Xóa món ăn", id);
        }

        transaction.Commit();
        return deleted;
    }

    public TourRoute SaveRoute(string? id, TourRouteUpsertRequest request)
    {
        using var connection = OpenConnection();
        using var transaction = connection.BeginTransaction();

        var name = (request.Name ?? string.Empty).Trim();
        var theme = string.IsNullOrWhiteSpace(request.Theme) ? "Tổng hợp" : request.Theme.Trim();
        var description = (request.Description ?? string.Empty).Trim();
        var coverImageUrl = (request.CoverImageUrl ?? string.Empty).Trim();
        var existing = !string.IsNullOrWhiteSpace(id) ? GetRouteById(connection, transaction, id) : null;
        var isNew = existing is null;
        var routeId = existing?.Id ?? id ?? CreateId("route");
        var actorName = request.ActorName?.Trim() ?? "SYSTEM";
        var actorRole = request.ActorRole?.Trim() ?? string.Empty;
        var isOwnerActor = string.Equals(actorRole, "PLACE_OWNER", StringComparison.OrdinalIgnoreCase);
        var actorUserId = request.ActorUserId?.Trim() ?? string.Empty;
        var actorUser = !string.IsNullOrWhiteSpace(actorUserId)
            ? GetUserById(connection, transaction, actorUserId)
            : null;
        var normalizedStopPoiIds = NormalizeList(request.StopPoiIds, distinct: false).ToList();
        var availablePoiIds = GetPois(connection, transaction)
            .Select(item => item.Id)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        if (string.IsNullOrWhiteSpace(name))
        {
            throw new InvalidOperationException("Tên tour là bắt buộc.");
        }

        if (request.DurationMinutes <= 0)
        {
            throw new InvalidOperationException("Thời lượng tour phải lớn hơn 0 phút.");
        }

        if (normalizedStopPoiIds.Count == 0)
        {
            throw new InvalidOperationException("Tour phải có ít nhất một điểm đến.");
        }

        var missingPoiIds = normalizedStopPoiIds
            .Where(stopPoiId => !availablePoiIds.Contains(stopPoiId))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (missingPoiIds.Count > 0)
        {
            throw new InvalidOperationException("Tour chứa POI không tồn tại hoặc đã bị xóa.");
        }

        if (isOwnerActor)
        {
            if (actorUser is null || !string.Equals(actorUser.Role, "PLACE_OWNER", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("Không xác định được chủ quán thực hiện thao tác tour.");
            }

            var ownerPoiIds = GetOwnerPoiIds(connection, transaction, actorUser.Id);
            if (!isNew && existing is not null && !existing.StopPoiIds.Any(ownerPoiIds.Contains))
            {
                throw new InvalidOperationException("Chủ quán chỉ được cập nhật tour có điểm đến của chính mình.");
            }

            if (normalizedStopPoiIds.Any(stopPoiId => !ownerPoiIds.Contains(stopPoiId)))
            {
                throw new InvalidOperationException("Chủ quán chỉ được tạo hoặc cập nhật tour bằng các POI của chính mình.");
            }
        }

        var now = DateTimeOffset.UtcNow;

        if (isNew)
        {
            ExecuteNonQuery(
                connection,
                transaction,
                """
                INSERT INTO dbo.Routes (
                    Id, Name, Theme, [Description], DurationMinutes, CoverImageUrl,
                    Difficulty, IsFeatured, IsActive, UpdatedBy, UpdatedAt
                )
                VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?);
                """,
                routeId,
                name,
                theme,
                description,
                request.DurationMinutes,
                coverImageUrl,
                "custom",
                false,
                request.IsActive,
                actorName,
                now);
        }
        else
        {
            ExecuteNonQuery(
                connection,
                transaction,
                """
                UPDATE dbo.Routes
                SET Name = ?,
                    Theme = ?,
                    [Description] = ?,
                    DurationMinutes = ?,
                    CoverImageUrl = ?,
                    Difficulty = ?,
                    IsFeatured = ?,
                    IsActive = ?,
                    UpdatedBy = ?,
                    UpdatedAt = ?
                WHERE Id = ?;
                """,
                name,
                theme,
                description,
                request.DurationMinutes,
                coverImageUrl,
                "custom",
                false,
                request.IsActive,
                actorName,
                now,
                routeId);
        }

        ReplaceRouteStops(connection, transaction, routeId, normalizedStopPoiIds);

        AppendAuditLog(
            connection,
            transaction,
            actorName,
            actorRole,
            isNew ? "Tạo tour" : "Cập nhật tour",
            name);

        var saved = GetRouteById(connection, transaction, routeId)
            ?? throw new InvalidOperationException("Không thể đọc lại tuyến tham quan sau khi lưu.");

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

        AppendAuditLog(connection, transaction, "SYSTEM", "SYSTEM", "Xóa tour", id);

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
                INSERT INTO dbo.Promotions (Id, PoiId, Title, [Description], StartAt, EndAt, [Status])
                VALUES (?, ?, ?, ?, ?, ?, ?);
                """,
                promotionId,
                request.PoiId,
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
                SET PoiId = ?,
                    Title = ?,
                    [Description] = ?,
                    StartAt = ?,
                    EndAt = ?,
                    [Status] = ?
                WHERE Id = ?;
                """,
                request.PoiId,
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
            isNew ? "Tạo ưu đãi" : "Cập nhật ưu đãi",
            request.Title);

        var saved = GetPromotionById(connection, transaction, promotionId)
            ?? throw new InvalidOperationException("Không thể đọc lại ưu đãi sau khi lưu.");

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
            AppendAuditLog(connection, transaction, "SYSTEM", "SYSTEM", "Xóa ưu đãi", id);
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
            PoiId = request.PoiId,
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
            INSERT INTO dbo.Reviews (Id, PoiId, UserName, Rating, CommentText, LanguageCode, CreatedAt, [Status])
            VALUES (?, ?, ?, ?, ?, ?, ?, ?);
            """,
            review.Id,
            review.PoiId,
            review.UserName,
            review.Rating,
            review.Comment,
            review.LanguageCode,
            review.CreatedAt,
            review.Status);

        AppendAuditLog(connection, transaction, review.UserName, "CUSTOMER", "Tạo đánh giá", review.PoiId);

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
        AppendAuditLog(connection, transaction, request.ActorName, request.ActorRole, "Cập nhật trạng thái đánh giá", id);

        var saved = GetReviewById(connection, transaction, id);
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
                    MapProvider, StorageProvider, TtsProvider, GeofenceRadiusMeters, GuestReviewEnabled, AnalyticsRetentionDays
                )
                VALUES (1, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?);
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
                request.GuestReviewEnabled,
                request.AnalyticsRetentionDays);
        }

        ReplaceSettingLanguages(connection, transaction, "free", request.FreeLanguages);
        ReplaceSettingLanguages(connection, transaction, "premium", request.PremiumLanguages);

        AppendAuditLog(connection, transaction, request.ActorName, request.ActorRole, "Cập nhật cài đặt hệ thống", request.AppName);

        var saved = GetSettings(connection, transaction);
        transaction.Commit();
        return saved;
    }

    private void ReplacePoiTags(SqlConnection connection, SqlTransaction transaction, string poiId, IEnumerable<string>? tags)
    {
        ExecuteNonQuery(connection, transaction, "DELETE FROM dbo.PoiTags WHERE PoiId = ?;", poiId);

        foreach (var tag in NormalizeList(tags))
        {
            ExecuteNonQuery(connection, transaction, "INSERT INTO dbo.PoiTags (PoiId, TagValue) VALUES (?, ?);", poiId, tag);
        }
    }

    private void ReplaceRouteStops(SqlConnection connection, SqlTransaction transaction, string routeId, IEnumerable<string>? stopPoiIds)
    {
        ExecuteNonQuery(connection, transaction, "DELETE FROM dbo.RouteStops WHERE RouteId = ?;", routeId);

        var order = 1;
        foreach (var stopPoiId in NormalizeList(stopPoiIds, distinct: false))
        {
            ExecuteNonQuery(
                connection,
                transaction,
                "INSERT INTO dbo.RouteStops (RouteId, StopOrder, PoiId) VALUES (?, ?, ?);",
                routeId,
                order,
                stopPoiId);
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
