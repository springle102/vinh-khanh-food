using Microsoft.Data.SqlClient;
using System.Data;
using VinhKhanh.BackendApi.Contracts;
using VinhKhanh.BackendApi.Models;

namespace VinhKhanh.BackendApi.Infrastructure;

public sealed partial class AdminDataRepository
{
    private void EnsureActorCanManagePoiContent(
        SqlConnection connection,
        SqlTransaction? transaction,
        AdminRequestContext actor,
        Poi? poi,
        string action)
    {
        if (actor.IsSuperAdmin)
        {
            throw new ApiForbiddenException($"Super Admin không có quyền {action}.");
        }

        if (actor.IsPlaceOwner && poi?.LockedBySuperAdmin == true)
        {
            throw new ApiForbiddenException("POI này đang bị Super Admin ngừng hoạt động nên chủ quán không thể chỉnh sửa hoặc tự bật lại.");
        }
    }

    private void EnsureActorCanManagePoiContentEntity(
        SqlConnection connection,
        SqlTransaction? transaction,
        AdminRequestContext actor,
        string? entityType,
        string? entityId,
        string action)
    {
        var normalizedEntityType = NormalizePoiContentEntityType(entityType);
        if (normalizedEntityType is not ("poi" or "food_item" or "promotion"))
        {
            return;
        }

        var relatedPoi = ResolvePoiForContentEntity(connection, transaction, normalizedEntityType, entityId);
        EnsureActorCanManagePoiContent(connection, transaction, actor, relatedPoi, action);
    }

    private Poi? ResolvePoiForContentEntity(
        SqlConnection connection,
        SqlTransaction? transaction,
        string? entityType,
        string? entityId)
    {
        if (string.IsNullOrWhiteSpace(entityId))
        {
            return null;
        }

        switch (NormalizePoiContentEntityType(entityType))
        {
            case "poi":
                return GetPoiById(connection, transaction, entityId);
            case "food_item":
            {
                var foodItem = GetFoodItemById(connection, transaction, entityId);
                return foodItem is null ? null : GetPoiById(connection, transaction, foodItem.PoiId);
            }
            case "promotion":
            {
                var promotion = GetPromotionById(connection, transaction, entityId);
                return promotion is null ? null : GetPoiById(connection, transaction, promotion.PoiId);
            }
        }

        return null;
    }

    private static string NormalizePoiContentEntityType(string? entityType)
    {
        var normalizedEntityType = entityType?.Trim() ?? string.Empty;
        return string.Equals(normalizedEntityType, "food-item", StringComparison.OrdinalIgnoreCase)
            ? "food_item"
            : normalizedEntityType.ToLowerInvariant();
    }

    public CustomerUser? LoginCustomer(string identifier, string password)
    {
        using var connection = OpenConnection();
        using var transaction = connection.BeginTransaction();

        var customer = GetCustomerUserByCredentials(connection, transaction, identifier, password);
        if (customer is null)
        {
            transaction.Rollback();
            return null;
        }

        ExecuteNonQuery(
            connection,
            transaction,
            """
            UPDATE dbo.CustomerUsers
            SET LastActiveAt = ?
            WHERE Id = ?;
            """,
            DateTimeOffset.UtcNow,
            customer.Id);

        AppendAuditLog(
            connection,
            transaction,
            customer.Name,
            "CUSTOMER",
            "Dang nhap khach hang",
            customer.Id);

        var saved = GetCustomerUserById(connection, transaction, customer.Id) ?? customer;
        transaction.Commit();
        return saved;
    }

    public CustomerUser? UpdateCustomerProfile(string id, CustomerProfileUpdateRequest request)
    {
        using var connection = OpenConnection();
        using var transaction = connection.BeginTransaction(IsolationLevel.Serializable);

        var existing = GetCustomerUserById(connection, transaction, id);
        if (existing is null)
        {
            transaction.Rollback();
            return null;
        }

        var normalizedName = NormalizeCustomerProfileValue(request.Name, "Tên", 120);
        var normalizedEmail = NormalizeCustomerRegistrationEmail(request.Email);
        var normalizedPhone = NormalizeCustomerProfileValue(request.Phone, "Số điện thoại", 30);

        normalizedPhone = NormalizeCustomerRegistrationPhone(request.Phone);
        var normalizedUsername = NormalizeCustomerUsername(request.Username);
        var existingRows = GetCustomerIdentityRowsForRegistration(connection, transaction);
        EnsureCustomerIdentityIsUnique(existingRows, normalizedEmail, normalizedPhone, normalizedUsername, id);

        ExecuteNonQuery(
            connection,
            transaction,
            """
            UPDATE dbo.CustomerUsers
            SET Name = ?,
                Username = ?,
                Email = ?,
                Phone = ?
            WHERE Id = ?;
            """,
            normalizedName,
            normalizedUsername,
            normalizedEmail,
            normalizedPhone,
            id);

        AppendAuditLog(
            connection,
            transaction,
            normalizedName,
            "CUSTOMER",
            "Cập nhật hồ sơ khách hàng",
            id);

        var saved = GetCustomerUserById(connection, transaction, id);

        transaction.Commit();
        return saved;
    }

    public MediaAsset SaveMediaAsset(string? id, MediaAssetUpsertRequest request, AdminRequestContext actor)
    {
        using var connection = OpenConnection();
        using var transaction = connection.BeginTransaction();
        EnsureActorCanManagePoiContentEntity(connection, transaction, actor, request.EntityType, request.EntityId, "chỉnh sửa media của POI");

        var existing = !string.IsNullOrWhiteSpace(id) ? GetMediaAssetById(connection, transaction, id) : null;
        var isNew = existing is null;
        var mediaId = existing?.Id ?? id ?? CreateId("media");
        var createdAt = existing?.CreatedAt ?? DateTimeOffset.UtcNow;

        _logger.LogInformation(
            "SaveMediaAsset request received. mediaId={MediaId}, entityType={EntityType}, entityId={EntityId}, mediaType={MediaType}, isNew={IsNew}",
            mediaId,
            request.EntityType,
            request.EntityId,
            request.Type,
            isNew);

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

        AppendAdminAuditLog(
            connection,
            transaction,
            actor,
            isNew ? "Tao media asset" : "Cap nhat media asset",
            "MEDIA",
            mediaId,
            $"{request.EntityType}:{request.EntityId}:{request.Type}");

        var saved = GetMediaAssetById(connection, transaction, mediaId)
            ?? throw new InvalidOperationException("Không thể đọc lại media asset sau khi lưu.");

        _logger.LogInformation(
            "SaveMediaAsset completed. mediaId={MediaId}, entityType={EntityType}, entityId={EntityId}, mediaType={MediaType}, createdAt={CreatedAt}",
            saved.Id,
            saved.EntityType,
            saved.EntityId,
            saved.Type,
            saved.CreatedAt);

        transaction.Commit();
        return saved;
    }

    public bool DeleteMediaAsset(string id, AdminRequestContext actor)
    {
        using var connection = OpenConnection();
        using var transaction = connection.BeginTransaction();

        var existing = GetMediaAssetById(connection, transaction, id);
        EnsureActorCanManagePoiContentEntity(connection, transaction, actor, existing?.EntityType, existing?.EntityId, "xóa media của POI");
        var deleted = ExecuteNonQuery(connection, transaction, "DELETE FROM dbo.MediaAssets WHERE Id = ?;", id) > 0;
        if (deleted)
        {
            AppendAdminAuditLog(
                connection,
                transaction,
                actor,
                "Xoa media asset",
                "MEDIA",
                existing?.EntityId ?? id,
                $"{existing?.EntityType ?? "unknown"}:{existing?.EntityId ?? id}:{existing?.Type ?? "unknown"}");
        }

        transaction.Commit();
        return deleted;
    }

    public FoodItem SaveFoodItem(string? id, FoodItemUpsertRequest request, AdminRequestContext actor)
    {
        using var connection = OpenConnection();
        using var transaction = connection.BeginTransaction();

        var existing = !string.IsNullOrWhiteSpace(id) ? GetFoodItemById(connection, transaction, id) : null;
        var targetPoi = GetPoiById(connection, transaction, request.PoiId)
            ?? (existing is null ? null : GetPoiById(connection, transaction, existing.PoiId));
        EnsureActorCanManagePoiContent(connection, transaction, actor, targetPoi, "chỉnh sửa món ăn của POI");
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

        AppendAdminAuditLog(
            connection,
            transaction,
            actor,
            isNew ? "Tao mon an" : "Cap nhat mon an",
            "FOOD_ITEM",
            foodId,
            request.Name);

        var saved = GetFoodItemById(connection, transaction, foodId)
            ?? throw new InvalidOperationException("Không thể đọc lại món ăn sau khi lưu.");

        transaction.Commit();
        return saved;
    }

    public bool DeleteFoodItem(string id, AdminRequestContext actor)
    {
        using var connection = OpenConnection();
        using var transaction = connection.BeginTransaction();

        var existing = GetFoodItemById(connection, transaction, id);
        var targetPoi = existing is null ? null : GetPoiById(connection, transaction, existing.PoiId);
        EnsureActorCanManagePoiContent(connection, transaction, actor, targetPoi, "xóa món ăn của POI");
        var deleted = ExecuteNonQuery(connection, transaction, "DELETE FROM dbo.FoodItems WHERE Id = ?;", id) > 0;
        if (deleted)
        {
            AppendAdminAuditLog(
                connection,
                transaction,
                actor,
                "Xoa mon an",
                "FOOD_ITEM",
                existing?.Id ?? id,
                existing?.Name ?? id);
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
        var difficulty = string.IsNullOrWhiteSpace(request.Difficulty) ? "custom" : request.Difficulty.Trim();
        var coverImageUrl = (request.CoverImageUrl ?? string.Empty).Trim();
        var existing = !string.IsNullOrWhiteSpace(id) ? GetRouteById(connection, transaction, id) : null;
        var isNew = existing is null;
        var routeId = existing?.Id ?? id ?? CreateId("route");
        var actorName = request.ActorName?.Trim() ?? "SYSTEM";
        var actorRole = AdminRoleCatalog.NormalizeKnownRoleOrOriginal(request.ActorRole);
        var isOwnerActor = AdminRoleCatalog.IsPlaceOwner(actorRole);
        var actorUserId = request.ActorUserId?.Trim() ?? string.Empty;
        var actorUser = !string.IsNullOrWhiteSpace(actorUserId)
            ? GetUserById(connection, transaction, actorUserId)
            : null;
        var isFeatured = isOwnerActor ? false : request.IsFeatured;
        var isSystemRoute = isOwnerActor ? false : existing?.IsSystemRoute ?? true;
        var ownerUserId = isOwnerActor ? actorUserId : existing?.OwnerUserId;
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

        _logger.LogInformation(
            "SaveRoute request received. routeId={RouteId}, name={Name}, theme={Theme}, difficulty={Difficulty}, isFeatured={IsFeatured}, stopCount={StopCount}, actorRole={ActorRole}, isNew={IsNew}",
            routeId,
            name,
            theme,
            difficulty,
            request.IsFeatured,
            normalizedStopPoiIds.Count,
            actorRole,
            isNew);

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
            if (actorUser is null || !AdminRoleCatalog.IsPlaceOwner(actorUser.Role))
            {
                throw new InvalidOperationException("Không xác định được chủ quán thực hiện thao tác tour.");
            }

            var ownerPoiIds = GetOwnerPoiIds(connection, transaction, actorUser.Id);
            if (!isNew && existing is not null &&
                (!string.Equals(existing.OwnerUserId, actorUser.Id, StringComparison.OrdinalIgnoreCase) || existing.IsSystemRoute))
            {
                throw new InvalidOperationException("Chủ quán chỉ được cập nhật tour riêng do chính mình tạo.");
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
                    Difficulty, IsFeatured, IsActive, IsSystemRoute, OwnerUserId, UpdatedBy, UpdatedAt
                )
                VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?);
                """,
                routeId,
                name,
                theme,
                description,
                request.DurationMinutes,
                coverImageUrl,
                difficulty,
                isFeatured,
                request.IsActive,
                isSystemRoute,
                string.IsNullOrWhiteSpace(ownerUserId) ? DBNull.Value : ownerUserId,
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
                    IsSystemRoute = ?,
                    OwnerUserId = ?,
                    UpdatedBy = ?,
                    UpdatedAt = ?
                WHERE Id = ?;
                """,
                name,
                theme,
                description,
                request.DurationMinutes,
                coverImageUrl,
                difficulty,
                isFeatured,
                request.IsActive,
                isSystemRoute,
                string.IsNullOrWhiteSpace(ownerUserId) ? DBNull.Value : ownerUserId,
                actorName,
                now,
                routeId);
        }

        ReplaceRouteStops(connection, transaction, routeId, normalizedStopPoiIds);

        AppendAdminAuditLog(
            connection,
            transaction,
            actorUserId,
            actorName,
            actorRole,
            isNew ? "Tao tour" : "Cap nhat tour",
            "TOUR",
            routeId,
            name);

        var saved = GetRouteById(connection, transaction, routeId)
            ?? throw new InvalidOperationException("Không thể đọc lại tuyến tham quan sau khi lưu.");

        transaction.Commit();
        return saved;
    }

    public bool DeleteRoute(string id, AdminRequestContext actor)
    {
        using var connection = OpenConnection();
        using var transaction = connection.BeginTransaction();

        var existing = GetRouteById(connection, transaction, id);
        if (existing is null)
        {
            transaction.Rollback();
            return false;
        }

        if (actor.IsPlaceOwner &&
            (!string.Equals(existing.OwnerUserId, actor.UserId, StringComparison.OrdinalIgnoreCase) || existing.IsSystemRoute))
        {
            throw new ApiNotFoundException("Khong tim thay tour.");
        }

        ExecuteNonQuery(connection, transaction, "DELETE FROM dbo.RouteStops WHERE RouteId = ?;", id);
        var deleted = ExecuteNonQuery(connection, transaction, "DELETE FROM dbo.Routes WHERE Id = ?;", id) > 0;
        if (!deleted)
        {
            transaction.Rollback();
            return false;
        }

        AppendAdminAuditLog(connection, transaction, actor, "Xoa tour", "TOUR", existing.Id, existing.Name);

        transaction.Commit();
        return true;
    }

    public Promotion SavePromotion(string? id, PromotionUpsertRequest request, AdminRequestContext actor)
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

        AppendAdminAuditLog(
            connection,
            transaction,
            actor,
            isNew ? "Tao uu dai" : "Cap nhat uu dai",
            "PROMOTION",
            promotionId,
            request.Title);

        var saved = GetPromotionById(connection, transaction, promotionId)
            ?? throw new InvalidOperationException("Không thể đọc lại ưu đãi sau khi lưu.");

        transaction.Commit();
        return saved;
    }

    public bool DeletePromotion(string id, AdminRequestContext actor)
    {
        using var connection = OpenConnection();
        using var transaction = connection.BeginTransaction();

        var existing = GetPromotionById(connection, transaction, id);
        var deleted = ExecuteNonQuery(
            connection,
            transaction,
            "UPDATE dbo.Promotions SET [Status] = ? WHERE Id = ?;",
            "deleted",
            id) > 0;
        if (deleted)
        {
            AppendAdminAuditLog(
                connection,
                transaction,
                actor,
                "Xoa mem uu dai",
                "PROMOTION",
                existing?.Id ?? id,
                existing?.Title ?? id,
                existing is null ? null : $"status={existing.Status}",
                "status=deleted");
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

    public Review? UpdateReviewStatus(string id, ReviewStatusRequest request, AdminRequestContext actor)
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
        AppendAdminAuditLog(connection, transaction, actor, "Cap nhat trang thai danh gia", "REVIEW", id, request.Status);

        var saved = GetReviewById(connection, transaction, id);
        transaction.Commit();
        return saved;
    }

    public SystemSetting SaveSettings(SystemSettingUpsertRequest request, AdminRequestContext actor)
    {
        using var connection = OpenConnection();
        using var transaction = connection.BeginTransaction(IsolationLevel.Serializable);

        var normalizedRequest = request with
        {
            DefaultLanguage = PremiumAccessCatalog.NormalizeLanguageCode(request.DefaultLanguage),
            FallbackLanguage = PremiumAccessCatalog.NormalizeLanguageCode(request.FallbackLanguage),
            SupportedLanguages = request.SupportedLanguages
                .Where(code => !string.IsNullOrWhiteSpace(code))
                .Select(PremiumAccessCatalog.NormalizeLanguageCode)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList(),
            FreeLanguages = [],
            PremiumLanguages = [],
            TtsProvider = NormalizeTtsProvider(request.TtsProvider),
            PremiumUnlockPriceUsd = 0
        };

        if (normalizedRequest.SupportedLanguages.Count == 0)
        {
            normalizedRequest = normalizedRequest with
            {
                SupportedLanguages =
                [
                    normalizedRequest.DefaultLanguage,
                    normalizedRequest.FallbackLanguage
                ]
            };
        }

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
                normalizedRequest.AppName,
                normalizedRequest.SupportEmail,
                normalizedRequest.DefaultLanguage,
                normalizedRequest.FallbackLanguage,
                normalizedRequest.PremiumUnlockPriceUsd,
                normalizedRequest.MapProvider,
                normalizedRequest.StorageProvider,
                normalizedRequest.TtsProvider,
                normalizedRequest.GeofenceRadiusMeters,
                normalizedRequest.GuestReviewEnabled,
                normalizedRequest.AnalyticsRetentionDays);
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
                normalizedRequest.AppName,
                normalizedRequest.SupportEmail,
                normalizedRequest.DefaultLanguage,
                normalizedRequest.FallbackLanguage,
                normalizedRequest.PremiumUnlockPriceUsd,
                normalizedRequest.MapProvider,
                normalizedRequest.StorageProvider,
                normalizedRequest.TtsProvider,
                normalizedRequest.GeofenceRadiusMeters,
                normalizedRequest.GuestReviewEnabled,
                normalizedRequest.AnalyticsRetentionDays);
        }

        ReplaceSettingLanguages(connection, transaction, "supported", normalizedRequest.SupportedLanguages);
        ReplaceSettingLanguages(connection, transaction, "free", []);
        ReplaceSettingLanguages(connection, transaction, "premium", []);

        AppendAdminAuditLog(connection, transaction, actor, "Cap nhat cai dat he thong", "SETTINGS", "system", request.AppName);

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
        var accessTokenExpiresAt = now.AddHours(8);
        var refreshExpiresAt = now.AddDays(30);

        ExecuteNonQuery(connection, transaction, "DELETE FROM dbo.RefreshSessions WHERE ExpiresAt <= ? OR AccessTokenExpiresAt <= ?;", now, now);
        ExecuteNonQuery(
            connection,
            transaction,
            "INSERT INTO dbo.RefreshSessions (AccessToken, RefreshToken, UserId, AccessTokenExpiresAt, ExpiresAt) VALUES (?, ?, ?, ?, ?);",
            accessToken,
            refreshToken,
            user.Id,
            accessTokenExpiresAt,
            refreshExpiresAt);

        return new AuthTokensResponse(
            user.Id,
            user.Name,
            user.Email,
            user.Role,
            accessToken,
            refreshToken,
            accessTokenExpiresAt);
    }

    private void AppendAdminAuditLog(
        SqlConnection connection,
        SqlTransaction transaction,
        AdminRequestContext actor,
        string action,
        string module,
        string _legacyTargetValue,
        string targetId,
        string targetSummary)
        => AppendAdminAuditLog(
            connection,
            transaction,
            actor.UserId,
            actor.Name,
            actor.Role,
            action,
            module,
            targetId,
            targetSummary,
            null,
            null);

    private void AppendAdminAuditLog(
        SqlConnection connection,
        SqlTransaction transaction,
        AdminRequestContext actor,
        string action,
        string module,
        string targetId,
        string targetSummary,
        string? beforeSummary = null,
        string? afterSummary = null)
        => AppendAdminAuditLog(
            connection,
            transaction,
            actor.UserId,
            actor.Name,
            actor.Role,
            action,
            module,
            targetId,
            targetSummary,
            beforeSummary,
            afterSummary);

    private void AppendAdminAuditLog(
        SqlConnection connection,
        SqlTransaction transaction,
        string actorId,
        string actorName,
        string actorRole,
        string action,
        string module,
        string targetId,
        string targetSummary,
        string? beforeSummary = null,
        string? afterSummary = null)
    {
        var normalizedActorRole = AdminRoleCatalog.NormalizeKnownRoleOrOriginal(actorRole);
        if (!HasAdminAuditLogTable(connection, transaction))
        {
            AppendLegacyAuditLog(
                connection,
                transaction,
                actorName,
                normalizedActorRole,
                action,
                string.IsNullOrWhiteSpace(targetSummary) ? targetId : targetSummary);
            return;
        }

        ExecuteNonQuery(
            connection,
            transaction,
            """
            INSERT INTO dbo.AdminAuditLogs (
                Id, ActorId, ActorName, ActorRole, ActorType, [Action], [Module], TargetId,
                TargetSummary, BeforeSummary, AfterSummary, SourceApp, CreatedAt
            )
            VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?);
            """,
            CreateId("audit"),
            actorId,
            actorName,
            normalizedActorRole,
            AdminRoleCatalog.AdminActorType,
            action,
            module,
            targetId,
            targetSummary,
            beforeSummary,
            afterSummary,
            AdminRoleCatalog.AdminWebSource,
            DateTimeOffset.UtcNow);

        ExecuteNonQuery(
            connection,
            transaction,
            """
            DELETE FROM dbo.AdminAuditLogs
            WHERE Id IN (
                SELECT Id
                FROM (
                    SELECT Id, ROW_NUMBER() OVER (ORDER BY CreatedAt DESC, Id DESC) AS RowNumber
                    FROM dbo.AdminAuditLogs
                ) AS OrderedLogs
                WHERE RowNumber > ?
            );
            """,
            MaxAuditLogs);
    }

    private void AppendLegacyAuditLog(
        SqlConnection connection,
        SqlTransaction transaction,
        string actorName,
        string actorRole,
        string action,
        string targetValue)
    {
        if (!HasLegacyAuditLogTable(connection, transaction))
        {
            return;
        }

        var normalizedActorRole = AdminRoleCatalog.NormalizeKnownRoleOrOriginal(actorRole);
        ExecuteNonQuery(
            connection,
            transaction,
            """
            INSERT INTO dbo.AuditLogs (
                Id, ActorName, ActorRole, [Action], TargetValue, CreatedAt
            )
            VALUES (?, ?, ?, ?, ?, ?);
            """,
            CreateId("audit"),
            TruncateLegacyAuditValue(actorName, 120),
            TruncateLegacyAuditValue(normalizedActorRole, 30),
            TruncateLegacyAuditValue(action, 200),
            TruncateLegacyAuditValue(targetValue, 300),
            DateTimeOffset.UtcNow);
    }

    private void AppendUserActivityLog(
        SqlConnection connection,
        SqlTransaction transaction,
        string actorId,
        string eventType,
        string metadata,
        string sourceApp = AdminRoleCatalog.MobileAppSource)
    {
        if (!HasUserActivityLogTable(connection, transaction))
        {
            return;
        }

        ExecuteNonQuery(
            connection,
            transaction,
            """
            INSERT INTO dbo.UserActivityLogs (
                Id, ActorId, ActorType, EventType, Metadata, SourceApp, CreatedAt
            )
            VALUES (?, ?, ?, ?, ?, ?, ?);
            """,
            CreateId("ua"),
            actorId,
            AdminRoleCatalog.EndUserActorType,
            eventType,
            metadata,
            sourceApp,
            DateTimeOffset.UtcNow);

        ExecuteNonQuery(
            connection,
            transaction,
            """
            DELETE FROM dbo.UserActivityLogs
            WHERE Id IN (
                SELECT Id
                FROM (
                    SELECT Id, ROW_NUMBER() OVER (ORDER BY CreatedAt DESC, Id DESC) AS RowNumber
                    FROM dbo.UserActivityLogs
                ) AS OrderedLogs
                WHERE RowNumber > ?
            );
            """,
            MaxUserActivityLogs);
    }

    private void AppendAuditLog(
        SqlConnection connection,
        SqlTransaction transaction,
        string actorName,
        string actorRole,
        string action,
        string targetValue)
    {
        var normalizedActorRole = AdminRoleCatalog.NormalizeKnownRoleOrOriginal(actorRole);
        if (AdminRoleCatalog.IsAdminRole(normalizedActorRole) ||
            string.Equals(normalizedActorRole, "SYSTEM", StringComparison.OrdinalIgnoreCase))
        {
            var adminActor = ResolveLegacyAdminAuditActor(connection, transaction, actorName, normalizedActorRole);
            AppendAdminAuditLog(
                connection,
                transaction,
                adminActor.ActorId,
                adminActor.ActorName,
                adminActor.ActorRole,
                action,
                GuessLegacyAuditModule(action),
                targetValue,
                targetValue);
            return;
        }

        if (!HasUserActivityLogTable(connection, transaction))
        {
            AppendLegacyAuditLog(connection, transaction, actorName, normalizedActorRole, action, targetValue);
            return;
        }

        AppendUserActivityLog(
            connection,
            transaction,
            ResolveLegacyUserActivityActorId(connection, transaction, actorName, targetValue),
            action,
            targetValue);
    }

    private (string ActorId, string ActorName, string ActorRole) ResolveLegacyAdminAuditActor(
        SqlConnection connection,
        SqlTransaction transaction,
        string actorName,
        string actorRole)
    {
        if (string.Equals(actorRole, "SYSTEM", StringComparison.OrdinalIgnoreCase))
        {
            return ("system", string.IsNullOrWhiteSpace(actorName) ? "SYSTEM" : actorName, "SYSTEM");
        }

        var matchedAdmin = GetUsers(connection, transaction)
            .FirstOrDefault(user =>
                string.Equals(user.Name, actorName, StringComparison.OrdinalIgnoreCase) &&
                AdminRoleCatalog.RoleEquals(user.Role, actorRole));

        return matchedAdmin is null
            ? (actorName, actorName, actorRole)
            : (matchedAdmin.Id, matchedAdmin.Name, matchedAdmin.Role);
    }

    private string ResolveLegacyUserActivityActorId(
        SqlConnection connection,
        SqlTransaction transaction,
        string actorName,
        string targetValue)
    {
        if (!string.IsNullOrWhiteSpace(targetValue))
        {
            var customer = GetCustomerUserById(connection, transaction, targetValue);
            if (customer is not null)
            {
                return customer.Id;
            }
        }

        var matchedCustomer = GetCustomerUsers(connection, transaction)
            .FirstOrDefault(user => string.Equals(user.Name, actorName, StringComparison.OrdinalIgnoreCase));

        return matchedCustomer?.Id ?? targetValue;
    }

    private static string GuessLegacyAuditModule(string action)
    {
        var normalizedAction = (action ?? string.Empty).Trim();
        if (normalizedAction.Contains("đăng nhập", StringComparison.OrdinalIgnoreCase) ||
            normalizedAction.Contains("đăng xuất", StringComparison.OrdinalIgnoreCase))
        {
            return "AUTH";
        }

        if (normalizedAction.Contains("POI", StringComparison.OrdinalIgnoreCase))
        {
            return "POI";
        }

        if (normalizedAction.Contains("thuyết minh", StringComparison.OrdinalIgnoreCase))
        {
            return "TRANSLATION";
        }

        if (normalizedAction.Contains("audio", StringComparison.OrdinalIgnoreCase))
        {
            return "AUDIO_GUIDE";
        }

        if (normalizedAction.Contains("media", StringComparison.OrdinalIgnoreCase))
        {
            return "MEDIA";
        }

        if (normalizedAction.Contains("món ăn", StringComparison.OrdinalIgnoreCase))
        {
            return "FOOD_ITEM";
        }

        if (normalizedAction.Contains("tour", StringComparison.OrdinalIgnoreCase) ||
            normalizedAction.Contains("tuyến", StringComparison.OrdinalIgnoreCase))
        {
            return "TOUR";
        }

        if (normalizedAction.Contains("ưu đãi", StringComparison.OrdinalIgnoreCase) ||
            normalizedAction.Contains("khuyến mãi", StringComparison.OrdinalIgnoreCase))
        {
            return "PROMOTION";
        }

        if (normalizedAction.Contains("đánh giá", StringComparison.OrdinalIgnoreCase) ||
            normalizedAction.Contains("review", StringComparison.OrdinalIgnoreCase))
        {
            return "REVIEW";
        }

        if (normalizedAction.Contains("cài đặt", StringComparison.OrdinalIgnoreCase))
        {
            return "SETTINGS";
        }

        if (normalizedAction.Contains("admin", StringComparison.OrdinalIgnoreCase) ||
            normalizedAction.Contains("chủ quán", StringComparison.OrdinalIgnoreCase))
        {
            return "ADMIN_USER";
        }

        return "GENERAL";
    }

    private static string TruncateLegacyAuditValue(string? value, int maxLength)
    {
        var normalized = (value ?? string.Empty).Trim();
        return normalized.Length <= maxLength
            ? normalized
            : normalized[..maxLength];
    }

    private static string NormalizeCustomerProfileValue(string? value, string fieldName, int maxLength)
    {
        var normalized = value?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(normalized))
        {
            throw new ArgumentException($"{fieldName} là bắt buộc.");
        }

        if (normalized.Length > maxLength)
        {
            throw new ArgumentException($"{fieldName} không được vượt quá {maxLength} ký tự.");
        }

        return normalized;
    }

}
