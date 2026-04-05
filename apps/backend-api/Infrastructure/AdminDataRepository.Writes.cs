using Microsoft.Data.SqlClient;
using System.Linq;
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
            _logger.LogDebug(
                "Executing admin user insert. userId={UserId}, role={Role}, status={Status}, managedPoiId={ManagedPoiId}",
                userId,
                request.Role,
                request.Status,
                managedPoiId);
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
            _logger.LogDebug(
                "Executing admin user update. userId={UserId}, role={Role}, status={Status}, managedPoiId={ManagedPoiId}",
                userId,
                request.Role,
                request.Status,
                managedPoiId);
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

        _logger.LogInformation(
            "SaveUser completed. userId={UserId}, role={Role}, status={Status}",
            saved.Id,
            saved.Role,
            saved.Status);

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
        var currentPoiId = existing?.Id ?? id;
        var poiId = ResolveRequestedPoiId(request.RequestedId, currentPoiId);
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
            if (!isNew && !ownerPoiIds.Contains(existing!.Id))
            {
                throw new InvalidOperationException("Chủ quán chỉ được cập nhật POI của chính mình.");
            }
        }

        var nextStatus = NormalizePoiStatus(request.Status, isOwnerActor);
        var nextFeatured = isOwnerActor ? false : request.Featured;
        var nextOwnerUserId = isOwnerActor ? actorUserId : request.OwnerUserId;

        _logger.LogInformation(
            "SavePoi request received. currentPoiId={CurrentPoiId}, requestedPoiId={RequestedPoiId}, resolvedPoiId={ResolvedPoiId}, slug={Slug}, address={Address}, tags={Tags}, actorRole={ActorRole}, isNew={IsNew}",
            currentPoiId,
            request.RequestedId,
            poiId,
            request.Slug,
            request.Address,
            string.Join(", ", request.Tags ?? []),
            actorRole,
            isNew);

        if (isNew)
        {
            EnsurePoiIdAvailable(connection, transaction, poiId);
            _logger.LogDebug(
                "Executing POI insert. poiId={PoiId}, ownerUserId={OwnerUserId}, status={Status}, featured={Featured}",
                poiId,
                nextOwnerUserId,
                nextStatus,
                nextFeatured);
            InsertPoiRecord(
                connection,
                transaction,
                poiId,
                request,
                nextStatus,
                nextFeatured,
                nextOwnerUserId,
                createdAt,
                now);
        }
        else if (!string.Equals(poiId, existing!.Id, StringComparison.OrdinalIgnoreCase))
        {
            EnsurePoiIdAvailable(connection, transaction, poiId, existing.Id);
            RenamePoiRecord(
                connection,
                transaction,
                existing,
                poiId,
                request,
                nextStatus,
                nextFeatured,
                nextOwnerUserId,
                now);
        }
        else
        {
            _logger.LogDebug(
                "Executing POI update. poiId={PoiId}, ownerUserId={OwnerUserId}, status={Status}, featured={Featured}, updatedBy={UpdatedBy}",
                poiId,
                nextOwnerUserId,
                nextStatus,
                nextFeatured,
                request.UpdatedBy);
            UpdatePoiRecord(
                connection,
                transaction,
                poiId,
                request,
                nextStatus,
                nextFeatured,
                nextOwnerUserId,
                now);
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

        _logger.LogInformation(
            "SavePoi completed. poiId={PoiId}, slug={Slug}, updatedAt={UpdatedAt}",
            saved.Id,
            saved.Slug,
            saved.UpdatedAt);

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

    private static string ResolveRequestedPoiId(string? requestedId, string? currentPoiId)
    {
        var normalizedRequestedId = requestedId?.Trim();
        if (!string.IsNullOrWhiteSpace(normalizedRequestedId))
        {
            ValidatePoiId(normalizedRequestedId);
            return normalizedRequestedId;
        }

        return !string.IsNullOrWhiteSpace(currentPoiId)
            ? currentPoiId
            : CreateId("poi");
    }

    private static void ValidatePoiId(string poiId)
    {
        if (poiId.All(character => char.IsLetterOrDigit(character) || character is '-' or '_'))
        {
            return;
        }

        throw new InvalidOperationException("ID quan chi duoc chua chu cai, so, dau gach ngang hoac gach duoi.");
    }

    private void EnsurePoiIdAvailable(
        SqlConnection connection,
        SqlTransaction? transaction,
        string poiId,
        string? ignorePoiId = null)
    {
        var existingPoi = GetPoiById(connection, transaction, poiId);
        if (existingPoi is null)
        {
            return;
        }

        if (!string.IsNullOrWhiteSpace(ignorePoiId) &&
            string.Equals(existingPoi.Id, ignorePoiId, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        throw new InvalidOperationException($"ID quan '{poiId}' da ton tai.");
    }

    private void InsertPoiRecord(
        SqlConnection connection,
        SqlTransaction transaction,
        string poiId,
        PoiUpsertRequest request,
        string status,
        bool featured,
        string? ownerUserId,
        DateTimeOffset createdAt,
        DateTimeOffset updatedAt)
    {
        ExecuteNonQuery(
            connection,
            transaction,
            """
            INSERT INTO dbo.Pois (
                Id, Slug, AddressLine, Latitude, Longitude, CategoryId, [Status], IsFeatured,
                District, Ward, PriceRange, AverageVisitDurationMinutes, PopularityScore, OwnerUserId, UpdatedBy, CreatedAt, UpdatedAt
            )
            VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?);
            """,
            poiId,
            request.Slug,
            request.Address,
            request.Lat,
            request.Lng,
            request.CategoryId,
            status,
            featured,
            request.District,
            request.Ward,
            request.PriceRange,
            request.AverageVisitDuration,
            request.PopularityScore,
            ownerUserId,
            request.UpdatedBy,
            createdAt,
            updatedAt);
    }

    private void UpdatePoiRecord(
        SqlConnection connection,
        SqlTransaction transaction,
        string poiId,
        PoiUpsertRequest request,
        string status,
        bool featured,
        string? ownerUserId,
        DateTimeOffset updatedAt)
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
            status,
            featured,
            request.District,
            request.Ward,
            request.PriceRange,
            request.AverageVisitDuration,
            request.PopularityScore,
            ownerUserId,
            request.UpdatedBy,
            updatedAt,
            poiId);
    }

    private void RenamePoiRecord(
        SqlConnection connection,
        SqlTransaction transaction,
        Poi existing,
        string nextPoiId,
        PoiUpsertRequest request,
        string status,
        bool featured,
        string? ownerUserId,
        DateTimeOffset updatedAt)
    {
        _logger.LogDebug(
            "Executing POI rename. oldPoiId={OldPoiId}, newPoiId={NewPoiId}, ownerUserId={OwnerUserId}, status={Status}, featured={Featured}, updatedBy={UpdatedBy}",
            existing.Id,
            nextPoiId,
            ownerUserId,
            status,
            featured,
            request.UpdatedBy);

        InsertPoiRecord(
            connection,
            transaction,
            nextPoiId,
            request,
            status,
            featured,
            ownerUserId,
            existing.CreatedAt,
            updatedAt);

        UpdatePoiReferenceIds(connection, transaction, existing.Id, nextPoiId);
        ExecuteNonQuery(connection, transaction, "DELETE FROM dbo.Pois WHERE Id = ?;", existing.Id);
    }

    private void UpdatePoiReferenceIds(
        SqlConnection connection,
        SqlTransaction transaction,
        string currentPoiId,
        string nextPoiId)
    {
        ExecuteNonQuery(connection, transaction, "UPDATE dbo.CustomerFavoritePois SET PoiId = ? WHERE PoiId = ?;", nextPoiId, currentPoiId);
        ExecuteNonQuery(connection, transaction, "UPDATE dbo.UserPoiVisits SET PoiId = ? WHERE PoiId = ?;", nextPoiId, currentPoiId);
        ExecuteNonQuery(connection, transaction, "UPDATE dbo.PoiTags SET PoiId = ? WHERE PoiId = ?;", nextPoiId, currentPoiId);
        ExecuteNonQuery(connection, transaction, "UPDATE dbo.FoodItems SET PoiId = ? WHERE PoiId = ?;", nextPoiId, currentPoiId);
        ExecuteNonQuery(connection, transaction, "UPDATE dbo.RouteStops SET PoiId = ? WHERE PoiId = ?;", nextPoiId, currentPoiId);
        ExecuteNonQuery(connection, transaction, "UPDATE dbo.Promotions SET PoiId = ? WHERE PoiId = ?;", nextPoiId, currentPoiId);
        ExecuteNonQuery(connection, transaction, "UPDATE dbo.Reviews SET PoiId = ? WHERE PoiId = ?;", nextPoiId, currentPoiId);
        ExecuteNonQuery(connection, transaction, "UPDATE dbo.ViewLogs SET PoiId = ? WHERE PoiId = ?;", nextPoiId, currentPoiId);
        ExecuteNonQuery(connection, transaction, "UPDATE dbo.AudioListenLogs SET PoiId = ? WHERE PoiId = ?;", nextPoiId, currentPoiId);
        ExecuteNonQuery(connection, transaction, "UPDATE dbo.AdminUsers SET ManagedPoiId = ? WHERE ManagedPoiId = ?;", nextPoiId, currentPoiId);
        ExecuteNonQuery(
            connection,
            transaction,
            "UPDATE dbo.PoiTranslations SET EntityId = ? WHERE EntityType IN (N'poi', N'place') AND EntityId = ?;",
            nextPoiId,
            currentPoiId);
        ExecuteNonQuery(
            connection,
            transaction,
            "UPDATE dbo.AudioGuides SET EntityId = ? WHERE EntityType IN (N'poi', N'place') AND EntityId = ?;",
            nextPoiId,
            currentPoiId);
        ExecuteNonQuery(
            connection,
            transaction,
            "UPDATE dbo.MediaAssets SET EntityId = ? WHERE EntityType IN (N'poi', N'place') AND EntityId = ?;",
            nextPoiId,
            currentPoiId);
    }

    private static string NormalizeEntityType(string entityType) =>
        string.Equals(entityType?.Trim(), "place", StringComparison.OrdinalIgnoreCase)
            ? "poi"
            : entityType?.Trim() ?? string.Empty;

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
        var normalizedEntityType = NormalizeEntityType(request.EntityType);
        var existing = !string.IsNullOrWhiteSpace(id)
            ? GetTranslationById(connection, transaction, id)
            : null;

        existing ??= GetTranslationByKey(connection, transaction, normalizedEntityType, request.EntityId, request.LanguageCode);

        var isNew = existing is null;
        var translationId = existing?.Id ?? id ?? CreateId("trans");

        _logger.LogInformation(
            "SaveTranslation request received. translationId={TranslationId}, entityType={EntityType}, entityId={EntityId}, languageCode={LanguageCode}, title={Title}, shortTextLength={ShortTextLength}, fullTextLength={FullTextLength}, isNew={IsNew}",
            translationId,
            normalizedEntityType,
            request.EntityId,
            request.LanguageCode,
            request.Title,
            request.ShortText?.Length ?? 0,
            request.FullText?.Length ?? 0,
            isNew);

        if (isNew)
        {
            _logger.LogDebug(
                "Executing translation insert. translationId={TranslationId}, entityType={EntityType}, entityId={EntityId}, languageCode={LanguageCode}",
                translationId,
                normalizedEntityType,
                request.EntityId,
                request.LanguageCode);
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
                normalizedEntityType,
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
            _logger.LogDebug(
                "Executing translation update. translationId={TranslationId}, entityType={EntityType}, entityId={EntityId}, languageCode={LanguageCode}, updatedBy={UpdatedBy}",
                translationId,
                normalizedEntityType,
                request.EntityId,
                request.LanguageCode,
                request.UpdatedBy);
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
                normalizedEntityType,
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
            $"{normalizedEntityType}:{request.LanguageCode}:{request.EntityId}");

        var saved = GetTranslationById(connection, transaction, translationId)
            ?? throw new InvalidOperationException("Không thể đọc lại nội dung thuyết minh sau khi lưu.");

        _logger.LogInformation(
            "SaveTranslation completed. translationId={TranslationId}, entityType={EntityType}, entityId={EntityId}, languageCode={LanguageCode}, updatedAt={UpdatedAt}",
            saved.Id,
            saved.EntityType,
            saved.EntityId,
            saved.LanguageCode,
            saved.UpdatedAt);

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
        var normalizedEntityType = NormalizeEntityType(request.EntityType);
        var existing = !string.IsNullOrWhiteSpace(id)
            ? GetAudioGuideById(connection, transaction, id)
            : null;

        existing ??= GetAudioGuideByKey(connection, transaction, normalizedEntityType, request.EntityId, request.LanguageCode);

        var isNew = existing is null;
        var audioId = existing?.Id ?? id ?? CreateId("audio");

        _logger.LogInformation(
            "SaveAudioGuide request received. audioId={AudioId}, entityType={EntityType}, entityId={EntityId}, languageCode={LanguageCode}, sourceType={SourceType}, status={Status}, isNew={IsNew}",
            audioId,
            normalizedEntityType,
            request.EntityId,
            request.LanguageCode,
            request.SourceType,
            request.Status,
            isNew);

        if (isNew)
        {
            _logger.LogDebug(
                "Executing audio guide insert. audioId={AudioId}, entityType={EntityType}, entityId={EntityId}, languageCode={LanguageCode}",
                audioId,
                normalizedEntityType,
                request.EntityId,
                request.LanguageCode);
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
                normalizedEntityType,
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
            _logger.LogDebug(
                "Executing audio guide update. audioId={AudioId}, entityType={EntityType}, entityId={EntityId}, languageCode={LanguageCode}, status={Status}",
                audioId,
                normalizedEntityType,
                request.EntityId,
                request.LanguageCode,
                request.Status);
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
                normalizedEntityType,
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
            $"{normalizedEntityType}:{request.LanguageCode}:{request.EntityId}");

        var saved = GetAudioGuideById(connection, transaction, audioId)
            ?? throw new InvalidOperationException("Không thể đọc lại audio guide sau khi lưu.");

        _logger.LogInformation(
            "SaveAudioGuide completed. audioId={AudioId}, entityType={EntityType}, entityId={EntityId}, languageCode={LanguageCode}, updatedAt={UpdatedAt}",
            saved.Id,
            saved.EntityType,
            saved.EntityId,
            saved.LanguageCode,
            saved.UpdatedAt);

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
