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

    private string? ResolvePoiIdForContentEntity(
        SqlConnection connection,
        SqlTransaction? transaction,
        string? entityType,
        string? entityId)
        => ResolvePoiForContentEntity(connection, transaction, entityType, entityId)?.Id;

    private void TouchPoiUpdatedAt(
        SqlConnection connection,
        SqlTransaction transaction,
        string? poiId,
        DateTimeOffset updatedAt)
    {
        if (string.IsNullOrWhiteSpace(poiId))
        {
            return;
        }

        ExecuteNonQuery(
            connection,
            transaction,
            """
            UPDATE dbo.Pois
            SET UpdatedAt = ?
            WHERE Id = ?;
            """,
            updatedAt,
            poiId);
    }

    private void TouchPoiUpdatedAt(
        SqlConnection connection,
        SqlTransaction transaction,
        DateTimeOffset updatedAt,
        params string?[] poiIds)
    {
        var touchedPoiIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var poiId in poiIds)
        {
            var normalizedPoiId = poiId?.Trim();
            if (string.IsNullOrWhiteSpace(normalizedPoiId) ||
                !touchedPoiIds.Add(normalizedPoiId))
            {
                continue;
            }

            TouchPoiUpdatedAt(connection, transaction, normalizedPoiId, updatedAt);
        }
    }

    private void TouchRelatedPoisForContentEntityChange(
        SqlConnection connection,
        SqlTransaction transaction,
        string? previousEntityType,
        string? previousEntityId,
        string? nextEntityType,
        string? nextEntityId,
        DateTimeOffset updatedAt)
    {
        TouchPoiUpdatedAt(
            connection,
            transaction,
            updatedAt,
            ResolvePoiIdForContentEntity(connection, transaction, previousEntityType, previousEntityId),
            ResolvePoiIdForContentEntity(connection, transaction, nextEntityType, nextEntityId));
    }

    private static string NormalizePoiContentEntityType(string? entityType)
    {
        var normalizedEntityType = entityType?.Trim() ?? string.Empty;
        return string.Equals(normalizedEntityType, "food-item", StringComparison.OrdinalIgnoreCase)
            ? "food_item"
            : normalizedEntityType.ToLowerInvariant();
    }

    public MediaAsset SaveMediaAsset(string? id, MediaAssetUpsertRequest request, AdminRequestContext actor)
    {
        using var connection = OpenConnection();
        using var transaction = connection.BeginTransaction();
        EnsureActorCanManagePoiContentEntity(connection, transaction, actor, request.EntityType, request.EntityId, "chỉnh sửa media của POI");

        var now = DateTimeOffset.UtcNow;
        var existing = !string.IsNullOrWhiteSpace(id) ? GetMediaAssetById(connection, transaction, id) : null;
        var isNew = existing is null;
        var mediaId = existing?.Id ?? id ?? CreateId("media");
        var createdAt = existing?.CreatedAt ?? now;

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

        TouchRelatedPoisForContentEntityChange(
            connection,
            transaction,
            existing?.EntityType,
            existing?.EntityId,
            request.EntityType,
            request.EntityId,
            now);

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

        var now = DateTimeOffset.UtcNow;
        var existing = GetMediaAssetById(connection, transaction, id);
        EnsureActorCanManagePoiContentEntity(connection, transaction, actor, existing?.EntityType, existing?.EntityId, "xóa media của POI");
        var deleted = ExecuteNonQuery(connection, transaction, "DELETE FROM dbo.MediaAssets WHERE Id = ?;", id) > 0;
        if (deleted)
        {
            TouchRelatedPoisForContentEntityChange(
                connection,
                transaction,
                existing?.EntityType,
                existing?.EntityId,
                null,
                null,
                now);

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

        var now = DateTimeOffset.UtcNow;
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

        TouchPoiUpdatedAt(connection, transaction, now, existing?.PoiId, request.PoiId);

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

        var now = DateTimeOffset.UtcNow;
        var existing = GetFoodItemById(connection, transaction, id);
        var targetPoi = existing is null ? null : GetPoiById(connection, transaction, existing.PoiId);
        EnsureActorCanManagePoiContent(connection, transaction, actor, targetPoi, "xóa món ăn của POI");
        var deleted = ExecuteNonQuery(connection, transaction, "DELETE FROM dbo.FoodItems WHERE Id = ?;", id) > 0;
        if (deleted)
        {
            TouchPoiUpdatedAt(connection, transaction, existing?.PoiId, now);

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
        var description = (request.Description ?? string.Empty).Trim();
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
        var normalizedRouteStopPoiIds = NormalizeRouteStopPoiIds(request.StopPoiIds);
        var resolvedDurationMinutes = ResolveRouteDurationMinutes(normalizedRouteStopPoiIds.Count);
        var resolvedIsFeatured = isOwnerActor ? false : request.IsFeatured ?? existing?.IsFeatured ?? false;
        var resolvedIsActive = request.IsActive ?? existing?.IsActive ?? true;
        var isSystemRoute = isOwnerActor ? false : existing?.IsSystemRoute ?? true;
        var ownerUserId = isOwnerActor ? actorUserId : existing?.OwnerUserId;
        var availablePoiIds = GetPois(connection, transaction)
            .Select(item => item.Id)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        if (string.IsNullOrWhiteSpace(name))
        {
            throw new InvalidOperationException("Tên tour là bắt buộc.");
        }

        if (resolvedDurationMinutes <= 0)
        {
            throw new InvalidOperationException("Thời lượng tour phải lớn hơn 0 phút.");
        }

        if (normalizedRouteStopPoiIds.Count == 0)
        {
            throw new InvalidOperationException("Tour phải có ít nhất một điểm đến.");
        }

        _logger.LogInformation(
            "SaveRoute request received. routeId={RouteId}, name={Name}, isFeatured={IsFeatured}, stopCount={StopCount}, actorRole={ActorRole}, isNew={IsNew}",
            routeId,
            name,
            resolvedIsFeatured,
            normalizedRouteStopPoiIds.Count,
            actorRole,
            isNew);

        var missingPoiIds = normalizedRouteStopPoiIds
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

            if (normalizedRouteStopPoiIds.Any(stopPoiId => !ownerPoiIds.Contains(stopPoiId)))
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
                    Id, Name, [Description], IsFeatured, IsActive, IsSystemRoute, OwnerUserId, UpdatedBy, UpdatedAt
                )
                VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?);
                """,
                routeId,
                name,
                description,
                resolvedIsFeatured,
                resolvedIsActive,
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
                    [Description] = ?,
                    IsFeatured = ?,
                    IsActive = ?,
                    IsSystemRoute = ?,
                    OwnerUserId = ?,
                    UpdatedBy = ?,
                    UpdatedAt = ?
                WHERE Id = ?;
                """,
                name,
                description,
                resolvedIsFeatured,
                resolvedIsActive,
                isSystemRoute,
                string.IsNullOrWhiteSpace(ownerUserId) ? DBNull.Value : ownerUserId,
                actorName,
                now,
                routeId);
        }

        ReplaceRouteStops(connection, transaction, routeId, normalizedRouteStopPoiIds);

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

        var now = DateTimeOffset.UtcNow;
        var existing = !string.IsNullOrWhiteSpace(id) ? GetPromotionById(connection, transaction, id) : null;
        var targetPoi = GetPoiById(connection, transaction, request.PoiId)
            ?? (existing is null ? null : GetPoiById(connection, transaction, existing.PoiId));
        EnsureActorCanManagePoiContent(connection, transaction, actor, targetPoi, "chinh sua uu dai cua POI");
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

        TouchPoiUpdatedAt(connection, transaction, now, existing?.PoiId, request.PoiId);

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

        var now = DateTimeOffset.UtcNow;
        var existing = GetPromotionById(connection, transaction, id);
        var deleted = ExecuteNonQuery(
            connection,
            transaction,
            "UPDATE dbo.Promotions SET [Status] = ? WHERE Id = ?;",
            "deleted",
            id) > 0;
        if (deleted)
        {
            TouchPoiUpdatedAt(connection, transaction, existing?.PoiId, now);

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
            TtsProvider = NormalizeTtsProvider(request.TtsProvider)
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
                    MapProvider = ?,
                    StorageProvider = ?,
                    TtsProvider = ?,
                    GeofenceRadiusMeters = ?,
                    AnalyticsRetentionDays = ?
                WHERE Id = 1;
                """,
                normalizedRequest.AppName,
                normalizedRequest.SupportEmail,
                normalizedRequest.DefaultLanguage,
                normalizedRequest.FallbackLanguage,
                normalizedRequest.MapProvider,
                normalizedRequest.StorageProvider,
                normalizedRequest.TtsProvider,
                normalizedRequest.GeofenceRadiusMeters,
                normalizedRequest.AnalyticsRetentionDays);
        }
        else
        {
            ExecuteNonQuery(
                connection,
                transaction,
                """
                INSERT INTO dbo.SystemSettings (
                    Id, AppName, SupportEmail, DefaultLanguage, FallbackLanguage,
                    MapProvider, StorageProvider, TtsProvider, GeofenceRadiusMeters, AnalyticsRetentionDays
                )
                VALUES (1, ?, ?, ?, ?, ?, ?, ?, ?, ?);
                """,
                normalizedRequest.AppName,
                normalizedRequest.SupportEmail,
                normalizedRequest.DefaultLanguage,
                normalizedRequest.FallbackLanguage,
                normalizedRequest.MapProvider,
                normalizedRequest.StorageProvider,
                normalizedRequest.TtsProvider,
                normalizedRequest.GeofenceRadiusMeters,
                normalizedRequest.AnalyticsRetentionDays);
        }

        ReplaceSettingLanguages(connection, transaction, "supported", normalizedRequest.SupportedLanguages);

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

    private static List<string> NormalizeRouteStopPoiIds(IEnumerable<string>? stopPoiIds)
    {
        var orderedPoiIds = new List<string>();
        var seenPoiIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var stopPoiId in stopPoiIds ?? [])
        {
            var normalizedPoiId = stopPoiId?.Trim();
            if (string.IsNullOrWhiteSpace(normalizedPoiId) || !seenPoiIds.Add(normalizedPoiId))
            {
                continue;
            }

            orderedPoiIds.Add(normalizedPoiId);
        }

        return orderedPoiIds;
    }

    private const int DefaultRouteStopDurationMinutes = 15;
    private const string DefaultRouteDifficulty = "custom";

    private static int ResolveRouteDurationMinutes(int stopCount)
        => Math.Max(DefaultRouteStopDurationMinutes, stopCount * DefaultRouteStopDurationMinutes);

    private static string BuildRouteTheme(string name)
        => string.IsNullOrWhiteSpace(name) ? "Tour" : name.Trim();

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
        return string.IsNullOrWhiteSpace(targetValue) ? actorName : targetValue;
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
