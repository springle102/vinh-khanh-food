using VinhKhanh.BackendApi.Contracts;
using VinhKhanh.BackendApi.Models;

namespace VinhKhanh.BackendApi.Infrastructure;

public sealed partial class AdminDataRepository
{
    public IReadOnlyList<LoginAccountOptionResponse> GetLoginAccountOptions(string? portal)
    {
        using var connection = OpenConnection();
        var normalizedPortal = string.IsNullOrWhiteSpace(portal) ? null : portal.Trim().ToLowerInvariant();

        return GetUsers(connection, null)
            .Where(user =>
                string.Equals(user.Status, "active", StringComparison.OrdinalIgnoreCase) &&
                CanAccessPortal(user.Role, normalizedPortal))
            .OrderByDescending(user => string.Equals(user.Role, "SUPER_ADMIN", StringComparison.OrdinalIgnoreCase))
            .ThenBy(user => user.Name, StringComparer.OrdinalIgnoreCase)
            .Select(user => new LoginAccountOptionResponse(
                user.Id,
                user.Name,
                user.Email,
                user.Password,
                user.Role,
                user.Status,
                user.ManagedPoiId))
            .ToList();
    }

    public AuthTokensResponse? Login(string email, string password, string? portal)
    {
        using var connection = OpenConnection();
        using var transaction = connection.BeginTransaction();

        var user = GetUserByCredentials(connection, transaction, email, password);
        if (user is null || !CanAccessPortal(user.Role, portal))
        {
            transaction.Rollback();
            return null;
        }

        var now = DateTimeOffset.UtcNow;
        ExecuteNonQuery(
            connection,
            transaction,
            "UPDATE dbo.AdminUsers SET LastLoginAt = ? WHERE Id = ?;",
            now,
            user.Id);

        user.LastLoginAt = now;
        AppendAuditLog(connection, transaction, user.Name, user.Role, "Đăng nhập admin", user.Email);
        var session = CreateSession(connection, transaction, user);

        transaction.Commit();
        return session;
    }

    private static bool CanAccessPortal(string role, string? portal)
    {
        if (string.IsNullOrWhiteSpace(portal))
        {
            return true;
        }

        if (string.Equals(portal, "admin", StringComparison.OrdinalIgnoreCase))
        {
            return string.Equals(role, "SUPER_ADMIN", StringComparison.OrdinalIgnoreCase);
        }

        if (string.Equals(portal, "restaurant", StringComparison.OrdinalIgnoreCase))
        {
            return string.Equals(role, "PLACE_OWNER", StringComparison.OrdinalIgnoreCase);
        }

        return false;
    }

    public AuthTokensResponse? Refresh(string refreshToken)
    {
        using var connection = OpenConnection();
        using var transaction = connection.BeginTransaction();

        var now = DateTimeOffset.UtcNow;
        var session = GetRefreshSession(connection, transaction, refreshToken, now);
        if (session is null)
        {
            transaction.Rollback();
            return null;
        }

        var user = GetUserById(connection, transaction, session.UserId);
        if (user is null || !string.Equals(user.Status, "active", StringComparison.OrdinalIgnoreCase))
        {
            transaction.Rollback();
            return null;
        }

        ExecuteNonQuery(
            connection,
            transaction,
            "DELETE FROM dbo.RefreshSessions WHERE RefreshToken = ?;",
            refreshToken);

        AppendAuditLog(connection, transaction, user.Name, user.Role, "Làm mới phiên đăng nhập", user.Email);
        var refreshed = CreateSession(connection, transaction, user);

        transaction.Commit();
        return refreshed;
    }

    public void Logout(string refreshToken)
    {
        using var connection = OpenConnection();
        ExecuteNonQuery(
            connection,
            null,
            "DELETE FROM dbo.RefreshSessions WHERE RefreshToken = ?;",
            refreshToken);
    }

    public AdminUser SaveUser(string? id, AdminUserUpsertRequest request)
    {
        using var connection = OpenConnection();
        using var transaction = connection.BeginTransaction();

        var now = DateTimeOffset.UtcNow;
        var existing = !string.IsNullOrWhiteSpace(id) ? GetUserById(connection, transaction, id) : null;
        var isNew = existing is null;
        var userId = existing?.Id ?? id ?? CreateId("user");
        var password = !string.IsNullOrWhiteSpace(request.Password)
            ? request.Password
            : existing?.Password is { Length: > 0 } ? existing.Password : "Admin@123";
        var managedPoiId = string.Equals(request.Role, "PLACE_OWNER", StringComparison.OrdinalIgnoreCase)
            ? request.ManagedPoiId
            : null;

        if (isNew)
        {
            ExecuteNonQuery(
                connection,
                transaction,
                """
                INSERT INTO dbo.AdminUsers (
                    Id, Name, Email, Phone, Role, [Password], [Status], CreatedAt, LastLoginAt, AvatarColor, ManagedPoiId
                )
                VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?);
                """,
                userId,
                request.Name,
                request.Email,
                request.Phone,
                request.Role,
                password,
                request.Status,
                now,
                null,
                request.AvatarColor,
                managedPoiId);
        }
        else
        {
            ExecuteNonQuery(
                connection,
                transaction,
                """
                UPDATE dbo.AdminUsers
                SET Name = ?,
                    Email = ?,
                    Phone = ?,
                    Role = ?,
                    [Password] = ?,
                    [Status] = ?,
                    AvatarColor = ?,
                    ManagedPoiId = ?
                WHERE Id = ?;
                """,
                request.Name,
                request.Email,
                request.Phone,
                request.Role,
                password,
                request.Status,
                request.AvatarColor,
                managedPoiId,
                userId);
        }

        AppendAuditLog(
            connection,
            transaction,
            request.ActorName,
            request.ActorRole,
            isNew ? "Tạo tài khoản admin" : "Cập nhật tài khoản admin",
            request.Email);

        var saved = GetUserById(connection, transaction, userId)
            ?? throw new InvalidOperationException("Không thể đọc lại tài khoản admin sau khi lưu.");

        transaction.Commit();
        return saved;
    }

    public Poi SavePoi(string? id, PoiUpsertRequest request)
    {
        using var connection = OpenConnection();
        using var transaction = connection.BeginTransaction();

        var now = DateTimeOffset.UtcNow;
        var existing = !string.IsNullOrWhiteSpace(id) ? GetPoiById(connection, transaction, id) : null;
        var isNew = existing is null;
        var poiId = existing?.Id ?? id ?? CreateId("poi");
        var createdAt = existing?.CreatedAt ?? now;
        var actorRole = request.ActorRole?.Trim() ?? string.Empty;
        var isOwnerActor = string.Equals(actorRole, "PLACE_OWNER", StringComparison.OrdinalIgnoreCase);
        var actorUserId = request.ActorUserId?.Trim() ?? string.Empty;
        var actorUser = !string.IsNullOrWhiteSpace(actorUserId)
            ? GetUserById(connection, transaction, actorUserId)
            : null;

        if (isOwnerActor)
        {
            if (actorUser is null || !string.Equals(actorUser.Role, "PLACE_OWNER", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("Không xác định được chủ quán thực hiện thao tác POI.");
            }

            var ownerPoiIds = GetOwnerPoiIds(connection, transaction, actorUser.Id);
            if (!isNew && !ownerPoiIds.Contains(poiId))
            {
                throw new InvalidOperationException("Chủ quán chỉ được cập nhật POI của chính mình.");
            }
        }

        var nextStatus = NormalizePoiStatus(request.Status, isOwnerActor);
        var nextFeatured = isOwnerActor ? false : request.Featured;
        var nextOwnerUserId = isOwnerActor ? actorUserId : request.OwnerUserId;

        if (isNew)
        {
            ExecuteNonQuery(
                connection,
                transaction,
                """
                INSERT INTO dbo.Pois (
                    Id, Slug, AddressLine, Latitude, Longitude, CategoryId, [Status], IsFeatured, DefaultLanguageCode,
                    District, Ward, PriceRange, AverageVisitDurationMinutes, PopularityScore, OwnerUserId, UpdatedBy, CreatedAt, UpdatedAt
                )
                VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?);
                """,
                poiId,
                request.Slug,
                request.Address,
                request.Lat,
                request.Lng,
                request.CategoryId,
                nextStatus,
                nextFeatured,
                request.DefaultLanguageCode,
                request.District,
                request.Ward,
                request.PriceRange,
                request.AverageVisitDuration,
                request.PopularityScore,
                nextOwnerUserId,
                request.UpdatedBy,
                createdAt,
                now);
        }
        else
        {
            ExecuteNonQuery(
                connection,
                transaction,
                """
                UPDATE dbo.Pois
                SET Slug = ?,
                    AddressLine = ?,
                    Latitude = ?,
                    Longitude = ?,
                    CategoryId = ?,
                    [Status] = ?,
                    IsFeatured = ?,
                    DefaultLanguageCode = ?,
                    District = ?,
                    Ward = ?,
                    PriceRange = ?,
                    AverageVisitDurationMinutes = ?,
                    PopularityScore = ?,
                    OwnerUserId = ?,
                    UpdatedBy = ?,
                    UpdatedAt = ?
                WHERE Id = ?;
                """,
                request.Slug,
                request.Address,
                request.Lat,
                request.Lng,
                request.CategoryId,
                nextStatus,
                nextFeatured,
                request.DefaultLanguageCode,
                request.District,
                request.Ward,
                request.PriceRange,
                request.AverageVisitDuration,
                request.PopularityScore,
                nextOwnerUserId,
                request.UpdatedBy,
                now,
                poiId);
        }

        ReplacePoiTags(connection, transaction, poiId, request.Tags);

        AppendAuditLog(
            connection,
            transaction,
            request.UpdatedBy,
            actorRole,
            ResolvePoiAuditAction(existing, nextStatus, isOwnerActor),
            request.Slug);

        var saved = GetPoiById(connection, transaction, poiId)
            ?? throw new InvalidOperationException("Không thể đọc lại POI sau khi lưu.");

        transaction.Commit();
        return saved;
    }

    private static string NormalizePoiStatus(string requestedStatus, bool isOwnerActor)
    {
        if (isOwnerActor)
        {
            return "pending";
        }

        return requestedStatus?.Trim().ToLowerInvariant() switch
        {
            "draft" => "draft",
            "pending" => "pending",
            "published" => "published",
            "archived" => "archived",
            _ => "draft",
        };
    }

    private static string ResolvePoiAuditAction(Poi? existing, string nextStatus, bool isOwnerActor)
    {
        if (isOwnerActor)
        {
            return existing is null ? "Gửi duyệt POI mới" : "Cập nhật POI chờ duyệt";
        }

        if (!string.Equals(existing?.Status, "published", StringComparison.OrdinalIgnoreCase) &&
            string.Equals(nextStatus, "published", StringComparison.OrdinalIgnoreCase))
        {
            return "Duyệt POI";
        }

        return existing is null ? "Tạo POI" : "Cập nhật POI";
    }

    public bool DeletePoi(string id)
    {
        using var connection = OpenConnection();
        using var transaction = connection.BeginTransaction();

        var poi = GetPoiById(connection, transaction, id);
        if (poi is null)
        {
            transaction.Rollback();
            return false;
        }

        ExecuteNonQuery(connection, transaction, "DELETE FROM dbo.CustomerFavoritePois WHERE PoiId = ?;", id);
        ExecuteNonQuery(connection, transaction, "DELETE FROM dbo.RouteStops WHERE PoiId = ?;", id);
        ExecuteNonQuery(connection, transaction, "UPDATE dbo.AdminUsers SET ManagedPoiId = NULL WHERE ManagedPoiId = ?;", id);
        ExecuteNonQuery(connection, transaction, "DELETE FROM dbo.PoiTags WHERE PoiId = ?;", id);
        ExecuteNonQuery(connection, transaction, "DELETE FROM dbo.PoiTranslations WHERE EntityType = ? AND EntityId = ?;", "poi", id);
        ExecuteNonQuery(connection, transaction, "DELETE FROM dbo.AudioGuides WHERE EntityType = ? AND EntityId = ?;", "poi", id);
        ExecuteNonQuery(connection, transaction, "DELETE FROM dbo.MediaAssets WHERE EntityType = ? AND EntityId = ?;", "poi", id);
        ExecuteNonQuery(connection, transaction, "DELETE FROM dbo.FoodItems WHERE PoiId = ?;", id);
        ExecuteNonQuery(connection, transaction, "DELETE FROM dbo.Promotions WHERE PoiId = ?;", id);
        ExecuteNonQuery(connection, transaction, "DELETE FROM dbo.Reviews WHERE PoiId = ?;", id);
        ExecuteNonQuery(connection, transaction, "DELETE FROM dbo.UserPoiVisits WHERE PoiId = ?;", id);
        ExecuteNonQuery(connection, transaction, "DELETE FROM dbo.ViewLogs WHERE PoiId = ?;", id);
        ExecuteNonQuery(connection, transaction, "DELETE FROM dbo.AudioListenLogs WHERE PoiId = ?;", id);
        ExecuteNonQuery(connection, transaction, "DELETE FROM dbo.Pois WHERE Id = ?;", id);

        AppendAuditLog(connection, transaction, "SYSTEM", "SYSTEM", "Xóa POI", poi.Slug);

        transaction.Commit();
        return true;
    }

    public Translation SaveTranslation(string? id, TranslationUpsertRequest request)
    {
        using var connection = OpenConnection();
        using var transaction = connection.BeginTransaction();

        var now = DateTimeOffset.UtcNow;
        var existing = !string.IsNullOrWhiteSpace(id)
            ? GetTranslationById(connection, transaction, id)
            : null;

        existing ??= GetTranslationByKey(connection, transaction, request.EntityType, request.EntityId, request.LanguageCode);

        var isNew = existing is null;
        var translationId = existing?.Id ?? id ?? CreateId("trans");

        if (isNew)
        {
            ExecuteNonQuery(
                connection,
                transaction,
                """
                INSERT INTO dbo.PoiTranslations (
                    Id, EntityType, EntityId, LanguageCode, Title, ShortText, FullText, SeoTitle, SeoDescription, IsPremium, UpdatedBy, UpdatedAt
                )
                VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?);
                """,
                translationId,
                request.EntityType,
                request.EntityId,
                request.LanguageCode,
                request.Title,
                request.ShortText,
                request.FullText,
                request.SeoTitle,
                request.SeoDescription,
                request.IsPremium,
                request.UpdatedBy,
                now);
        }
        else
        {
            ExecuteNonQuery(
                connection,
                transaction,
                """
                UPDATE dbo.PoiTranslations
                SET EntityType = ?,
                    EntityId = ?,
                    LanguageCode = ?,
                    Title = ?,
                    ShortText = ?,
                    FullText = ?,
                    SeoTitle = ?,
                    SeoDescription = ?,
                    IsPremium = ?,
                    UpdatedBy = ?,
                    UpdatedAt = ?
                WHERE Id = ?;
                """,
                request.EntityType,
                request.EntityId,
                request.LanguageCode,
                request.Title,
                request.ShortText,
                request.FullText,
                request.SeoTitle,
                request.SeoDescription,
                request.IsPremium,
                request.UpdatedBy,
                now,
                translationId);
        }

        AppendAuditLog(
            connection,
            transaction,
            request.UpdatedBy,
            "SYSTEM",
            isNew ? "Tạo nội dung thuyết minh" : "Cập nhật nội dung thuyết minh",
            $"{request.EntityType}:{request.LanguageCode}:{request.EntityId}");

        var saved = GetTranslationById(connection, transaction, translationId)
            ?? throw new InvalidOperationException("Không thể đọc lại nội dung thuyết minh sau khi lưu.");

        transaction.Commit();
        return saved;
    }

    public bool DeleteTranslation(string id)
    {
        using var connection = OpenConnection();
        using var transaction = connection.BeginTransaction();

        var deleted = ExecuteNonQuery(connection, transaction, "DELETE FROM dbo.PoiTranslations WHERE Id = ?;", id) > 0;
        if (deleted)
        {
            AppendAuditLog(connection, transaction, "SYSTEM", "SYSTEM", "Xóa nội dung thuyết minh", id);
        }

        transaction.Commit();
        return deleted;
    }

    public AudioGuide SaveAudioGuide(string? id, AudioGuideUpsertRequest request)
    {
        using var connection = OpenConnection();
        using var transaction = connection.BeginTransaction();

        var now = DateTimeOffset.UtcNow;
        var existing = !string.IsNullOrWhiteSpace(id)
            ? GetAudioGuideById(connection, transaction, id)
            : null;

        existing ??= GetAudioGuideByKey(connection, transaction, request.EntityType, request.EntityId, request.LanguageCode);

        var isNew = existing is null;
        var audioId = existing?.Id ?? id ?? CreateId("audio");

        if (isNew)
        {
            ExecuteNonQuery(
                connection,
                transaction,
                """
                INSERT INTO dbo.AudioGuides (
                    Id, EntityType, EntityId, LanguageCode, AudioUrl, VoiceType, SourceType, [Status], UpdatedBy, UpdatedAt
                )
                VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?);
                """,
                audioId,
                request.EntityType,
                request.EntityId,
                request.LanguageCode,
                request.AudioUrl,
                request.VoiceType,
                request.SourceType,
                request.Status,
                request.UpdatedBy,
                now);
        }
        else
        {
            ExecuteNonQuery(
                connection,
                transaction,
                """
                UPDATE dbo.AudioGuides
                SET EntityType = ?,
                    EntityId = ?,
                    LanguageCode = ?,
                    AudioUrl = ?,
                    VoiceType = ?,
                    SourceType = ?,
                    [Status] = ?,
                    UpdatedBy = ?,
                    UpdatedAt = ?
                WHERE Id = ?;
                """,
                request.EntityType,
                request.EntityId,
                request.LanguageCode,
                request.AudioUrl,
                request.VoiceType,
                request.SourceType,
                request.Status,
                request.UpdatedBy,
                now,
                audioId);
        }

        AppendAuditLog(
            connection,
            transaction,
            request.UpdatedBy,
            "SYSTEM",
            isNew ? "Tạo audio guide" : "Cập nhật audio guide",
            $"{request.EntityType}:{request.LanguageCode}:{request.EntityId}");

        var saved = GetAudioGuideById(connection, transaction, audioId)
            ?? throw new InvalidOperationException("Không thể đọc lại audio guide sau khi lưu.");

        transaction.Commit();
        return saved;
    }

    public bool DeleteAudioGuide(string id)
    {
        using var connection = OpenConnection();
        using var transaction = connection.BeginTransaction();

        var deleted = ExecuteNonQuery(connection, transaction, "DELETE FROM dbo.AudioGuides WHERE Id = ?;", id) > 0;
        if (deleted)
        {
            AppendAuditLog(connection, transaction, "SYSTEM", "SYSTEM", "Xóa audio guide", id);
        }

        transaction.Commit();
        return deleted;
    }
}
