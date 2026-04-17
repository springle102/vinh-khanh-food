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
                CanUserStartAdminSession(user) &&
                CanAccessPortal(user.Role, normalizedPortal))
            .OrderByDescending(user => AdminRoleCatalog.IsSuperAdmin(user.Role))
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

        var user = GetUserByCredentialsIgnoringStatus(connection, transaction, email, password);
        if (user is null || !CanAccessPortal(user.Role, portal))
        {
            transaction.Rollback();
            return null;
        }

        EnsureUserCanStartSession(user);

        var now = DateTimeOffset.UtcNow;
        ExecuteNonQuery(
            connection,
            transaction,
            "UPDATE dbo.AdminUsers SET LastLoginAt = ? WHERE Id = ?;",
            now,
            user.Id);

        user.LastLoginAt = now;
        AppendAdminAuditLog(connection, transaction, user.Id, user.Name, user.Role, "Đăng nhập admin", "AUTH", user.Id, user.Email);
        var session = CreateSession(connection, transaction, user);

        transaction.Commit();
        return session;
    }

    private static bool CanAccessPortal(string role, string? portal)
    {
        if (!AdminRoleCatalog.IsAdminRole(role))
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(portal))
        {
            return true;
        }

        if (string.Equals(portal, "admin", StringComparison.OrdinalIgnoreCase))
        {
            return AdminRoleCatalog.IsSuperAdmin(role);
        }

        if (string.Equals(portal, "restaurant", StringComparison.OrdinalIgnoreCase))
        {
            return AdminRoleCatalog.IsPlaceOwner(role);
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
        if (user is null || !CanUserStartAdminSession(user))
        {
            transaction.Rollback();
            return null;
        }

        ExecuteNonQuery(
            connection,
            transaction,
            "DELETE FROM dbo.RefreshSessions WHERE RefreshToken = ?;",
            refreshToken);

        AppendAdminAuditLog(connection, transaction, user.Id, user.Name, user.Role, "Làm mới phiên đăng nhập", "AUTH", user.Id, user.Email);
        var refreshed = CreateSession(connection, transaction, user);

        transaction.Commit();
        return refreshed;
    }

    public void Logout(string refreshToken)
    {
        using var connection = OpenConnection();
        using var transaction = connection.BeginTransaction();

        var session = GetRefreshSession(connection, transaction, refreshToken, DateTimeOffset.UtcNow);
        var user = session is null ? null : GetUserById(connection, transaction, session.UserId);
        ExecuteNonQuery(
            connection,
            transaction,
            "DELETE FROM dbo.RefreshSessions WHERE RefreshToken = ?;",
            refreshToken);

        if (user is not null)
        {
            AppendAdminAuditLog(connection, transaction, user.Id, user.Name, user.Role, "Đăng xuất admin", "AUTH", user.Id, user.Email);
        }

        transaction.Commit();
    }

    public AdminUser SaveUser(string? id, AdminUserUpsertRequest request, AdminRequestContext actor)
    {
        using var connection = OpenConnection();
        using var transaction = connection.BeginTransaction();

        var now = DateTimeOffset.UtcNow;
        var existing = !string.IsNullOrWhiteSpace(id) ? GetUserById(connection, transaction, id) : null;
        var isNew = existing is null;
        var userId = existing?.Id ?? id ?? CreateId("user");
        var isSelfUpdate = !isNew && string.Equals(userId, actor.UserId, StringComparison.OrdinalIgnoreCase);
        if (isNew && !actor.IsSuperAdmin)
        {
            throw new ApiForbiddenException("Chỉ super admin mới được tạo tài khoản quản trị.");
        }

        if (!isNew && !actor.IsSuperAdmin && !isSelfUpdate)
        {
            throw new ApiForbiddenException("Bạn không có quyền cập nhật tài khoản quản trị này.");
        }

        var nextRole = actor.IsSuperAdmin
            ? AdminRoleCatalog.NormalizeRequiredRole(request.Role)
            : AdminRoleCatalog.NormalizeKnownRoleOrOriginal(existing?.Role ?? actor.Role);
        if (actor.IsSuperAdmin && AdminRoleCatalog.IsSuperAdmin(nextRole) && !AdminRoleCatalog.IsSuperAdmin(existing?.Role))
        {
            throw new ApiForbiddenException("He thong chi ho tro duy nhat 1 super admin.");
        }

        if (AdminRoleCatalog.IsSuperAdmin(existing?.Role) && !AdminRoleCatalog.IsSuperAdmin(nextRole))
        {
            throw new ApiForbiddenException("He thong phai duy tri dung 1 super admin. Khong the doi vai tro tai khoan nay.");
        }

        if (actor.IsSuperAdmin && AdminRoleCatalog.IsPlaceOwner(nextRole))
        {
            if (isNew)
            {
                throw new ApiForbiddenException("Super admin khong the tao truc tiep tai khoan chu quan. Hay su dung luong dang ky chu quan.");
            }

            if (!AdminRoleCatalog.IsPlaceOwner(existing?.Role))
            {
                throw new ApiForbiddenException("Khong the chuyen doi tai khoan nay thanh chu quan tai man hinh quan ly admin.");
            }

            if (!isSelfUpdate)
            {
                throw new ApiForbiddenException("Super admin khong duoc chinh sua ho so chu quan tai man hinh nay. Chi duoc duyet, tu choi va cap nhat trang thai hoat dong.");
            }
        }

        var nextStatus = AdminRoleCatalog.IsSuperAdmin(nextRole)
            ? existing?.Status ?? actor.Status
            : actor.IsSuperAdmin
                ? NormalizeAdminUserStatus(request.Status)
                : existing?.Status ?? actor.Status;
        var password = !string.IsNullOrWhiteSpace(request.Password)
            ? request.Password
            : existing?.Password is { Length: > 0 } ? existing.Password : "Admin@123";
        var managedPoiId = actor.IsSuperAdmin
            ? AdminRoleCatalog.IsPlaceOwner(nextRole)
                ? request.ManagedPoiId
                : null
            : existing?.ManagedPoiId ?? actor.ManagedPoiId;
        var approvalStatus = ResolveApprovalStatusForAdminUserSave(existing, nextRole);
        var rejectionReason = AdminApprovalCatalog.IsRejected(approvalStatus)
            ? existing?.RejectionReason
            : null;
        var registrationSubmittedAt = existing?.RegistrationSubmittedAt ?? now;
        var registrationReviewedAt = existing?.RegistrationReviewedAt ??
            (AdminApprovalCatalog.IsApproved(approvalStatus) ? now : null);

        if (isNew)
        {
            _logger.LogDebug(
                "Executing admin user insert. userId={UserId}, role={Role}, status={Status}, managedPoiId={ManagedPoiId}",
                userId,
                nextRole,
                nextStatus,
                managedPoiId);
            ExecuteNonQuery(
                connection,
                transaction,
                """
                INSERT INTO dbo.AdminUsers (
                    Id, Name, Email, Phone, Role, [Password], [Status], CreatedAt, LastLoginAt, AvatarColor, ManagedPoiId,
                    ApprovalStatus, RejectionReason, RegistrationSubmittedAt, RegistrationReviewedAt
                )
                VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?);
                """,
                userId,
                request.Name,
                request.Email,
                request.Phone,
                nextRole,
                password,
                nextStatus,
                now,
                null,
                request.AvatarColor,
                managedPoiId,
                approvalStatus,
                rejectionReason,
                registrationSubmittedAt,
                registrationReviewedAt);
        }
        else
        {
            _logger.LogDebug(
                "Executing admin user update. userId={UserId}, role={Role}, status={Status}, managedPoiId={ManagedPoiId}",
                userId,
                nextRole,
                nextStatus,
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
                    ManagedPoiId = ?,
                    ApprovalStatus = ?,
                    RejectionReason = ?,
                    RegistrationSubmittedAt = ?,
                    RegistrationReviewedAt = ?
                WHERE Id = ?;
                """,
                request.Name,
                request.Email,
                request.Phone,
                nextRole,
                password,
                nextStatus,
                request.AvatarColor,
                managedPoiId,
                approvalStatus,
                rejectionReason,
                registrationSubmittedAt,
                registrationReviewedAt,
                userId);
        }

        AppendAdminAuditLog(
            connection,
            transaction,
            actor,
            isNew ? "Tạo tài khoản admin" : isSelfUpdate ? "Cập nhật hồ sơ admin" : "Cập nhật tài khoản admin",
            "ADMIN_USER",
            isNew ? "Tạo tài khoản admin" : "Cập nhật tài khoản admin",
            userId,
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

    public AdminUser SaveUserStatus(string id, AdminUserStatusUpdateRequest request, AdminRequestContext actor)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!actor.IsSuperAdmin)
        {
            throw new ApiForbiddenException("Chi super admin moi duoc cap nhat trang thai hoat dong cua chu quan.");
        }

        using var connection = OpenConnection();
        using var transaction = connection.BeginTransaction();

        var existing = GetUserById(connection, transaction, id);
        EnsurePlaceOwnerRegistrationExists(existing);

        if (!AdminApprovalCatalog.IsApproved(existing!.ApprovalStatus))
        {
            throw new InvalidOperationException("Tai khoan chu quan nay chua duoc duyet. Hay xu ly ho so o muc dang ky chu quan.");
        }

        var currentStatus = NormalizeAdminUserStatus(existing.Status);
        var nextStatus = NormalizeAdminUserStatus(request.Status);

        if (!string.Equals(currentStatus, nextStatus, StringComparison.Ordinal))
        {
            ExecuteNonQuery(
                connection,
                transaction,
                """
                UPDATE dbo.AdminUsers
                SET [Status] = ?
                WHERE Id = ?;
                """,
                nextStatus,
                existing.Id);

            AppendAdminAuditLog(
                connection,
                transaction,
                actor,
                nextStatus == "active" ? "Mo khoa tai khoan chu quan" : "Khoa tai khoan chu quan",
                "ADMIN_USER",
                existing.Id,
                existing.Email,
                $"status={currentStatus}",
                $"status={nextStatus}");
        }

        var saved = GetUserById(connection, transaction, existing.Id)
            ?? throw new InvalidOperationException("Khong the doc lai tai khoan chu quan sau khi cap nhat trang thai.");

        transaction.Commit();
        return saved;
    }

    public Poi SavePoi(string? id, PoiUpsertRequest request, AdminRequestContext actor)
    {
        using var connection = OpenConnection();
        using var transaction = connection.BeginTransaction();

        request = NormalizePoiRequestForPersistence(request);

        var now = DateTimeOffset.UtcNow;
        var existing = !string.IsNullOrWhiteSpace(id) ? GetPoiById(connection, transaction, id) : null;
        EnsureActorCanManagePoiContent(connection, transaction, actor, existing, "chỉnh sửa nội dung POI");
        var isNew = existing is null;
        var currentPoiId = existing?.Id ?? id;
        var poiId = ResolveRequestedPoiId(request.RequestedId, currentPoiId);
        var createdAt = existing?.CreatedAt ?? now;
        var isOwnerActor = actor.IsPlaceOwner;
        if (isOwnerActor)
        {
            if (false)
            {
                throw new InvalidOperationException("Không xác định được chủ quán thực hiện thao tác POI.");
            }

            var ownerPoiIds = GetOwnerPoiIds(connection, transaction, actor.UserId);
            if (!isNew && !ownerPoiIds.Contains(existing!.Id))
            {
                throw new InvalidOperationException("Chủ quán chỉ được cập nhật POI của chính mình.");
            }
        }

        var nextStatus = NormalizePoiStatus(request.Status, isOwnerActor);
        var currentFeatured = existing?.Featured ?? false;
        var nextPublicationMetadata = ResolvePoiPublicationMetadataForSave(existing, nextStatus, isOwnerActor);
        var nextOwnerUserId = isOwnerActor ? actor.UserId : request.OwnerUserId;
        var nextReviewMetadata = ResolvePoiReviewMetadataForSave(existing, nextStatus, isOwnerActor);

        _logger.LogInformation(
            "SavePoi request received. currentPoiId={CurrentPoiId}, requestedPoiId={RequestedPoiId}, resolvedPoiId={ResolvedPoiId}, slug={Slug}, address={Address}, tags={Tags}, actorRole={ActorRole}, isNew={IsNew}",
            currentPoiId,
            request.RequestedId,
            poiId,
            request.Slug,
            request.Address,
            string.Join(", ", request.Tags ?? []),
            actor.Role,
            isNew);

        if (isNew)
        {
            EnsurePoiIdAvailable(connection, transaction, poiId);
            _logger.LogDebug(
                "Executing POI insert. poiId={PoiId}, ownerUserId={OwnerUserId}, status={Status}",
                poiId,
                nextOwnerUserId,
                nextStatus);
            InsertPoiRecord(
                connection,
                transaction,
                poiId,
                request,
                actor.Name,
                nextStatus,
                false,
                nextPublicationMetadata.IsActive,
                nextPublicationMetadata.LockedBySuperAdmin,
                nextOwnerUserId,
                nextPublicationMetadata.ApprovedAt,
                nextReviewMetadata.RejectionReason,
                nextReviewMetadata.RejectedAt,
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
                actor.Name,
                nextStatus,
                currentFeatured,
                nextPublicationMetadata.IsActive,
                nextPublicationMetadata.LockedBySuperAdmin,
                nextOwnerUserId,
                nextPublicationMetadata.ApprovedAt,
                nextReviewMetadata.RejectionReason,
                nextReviewMetadata.RejectedAt,
                now);
        }
        else
        {
            _logger.LogDebug(
                "Executing POI update. poiId={PoiId}, ownerUserId={OwnerUserId}, status={Status}, updatedBy={UpdatedBy}",
                poiId,
                nextOwnerUserId,
                nextStatus,
                actor.Name);
            UpdatePoiRecord(
                connection,
                transaction,
                poiId,
                request,
                actor.Name,
                nextStatus,
                currentFeatured,
                nextPublicationMetadata.IsActive,
                nextPublicationMetadata.LockedBySuperAdmin,
                nextOwnerUserId,
                nextPublicationMetadata.ApprovedAt,
                nextReviewMetadata.RejectionReason,
                nextReviewMetadata.RejectedAt,
                now);
        }

        ReplacePoiTags(connection, transaction, poiId, request.Tags);

        var beforeStatusSummary = existing is null ? null : $"status={existing.Status}";
        var afterStatusSummary = $"status={nextStatus}";

        if (isOwnerActor && IsRejectedPoi(existing) && string.Equals(nextStatus, "pending", StringComparison.OrdinalIgnoreCase))
        {
            AppendAdminAuditLog(
                connection,
                transaction,
                actor,
                "Chỉnh sửa POI bị từ chối",
                "POI",
                poiId,
                request.Slug,
                beforeStatusSummary,
                afterStatusSummary);
            AppendAdminAuditLog(
                connection,
                transaction,
                actor,
                "Gửi duyệt lại POI",
                "POI",
                poiId,
                request.Slug,
                beforeStatusSummary,
                afterStatusSummary);
        }
        else
        {
            AppendAdminAuditLog(
                connection,
                transaction,
                actor,
                ResolvePoiAuditAction(existing, nextStatus, isOwnerActor),
                "POI",
                poiId,
                request.Slug);
        }

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
            "rejected" => "rejected",
            "archived" => "archived",
            "deleted" => "deleted",
            _ => "draft",
        };
    }

    private static string NormalizeAdminUserStatus(string? requestedStatus)
    {
        return requestedStatus?.Trim().ToLowerInvariant() switch
        {
            "active" => "active",
            "locked" => "locked",
            _ => throw new InvalidOperationException("Trang thai tai khoan khong hop le.")
        };
    }

    private static PoiPublicationMetadata ResolvePoiPublicationMetadataForSave(
        Poi? existing,
        string nextStatus,
        bool isOwnerActor)
    {
        if (existing is null)
        {
            return new(
                IsActive: !isOwnerActor && string.Equals(nextStatus, "published", StringComparison.OrdinalIgnoreCase),
                LockedBySuperAdmin: false,
                ApprovedAt: null);
        }

        if (isOwnerActor)
        {
            return new(
                IsActive: false,
                LockedBySuperAdmin: false,
                ApprovedAt: existing.ApprovedAt);
        }

        return new(existing.IsActive, existing.LockedBySuperAdmin, existing.ApprovedAt);
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

    private sealed record PoiPublicationMetadata(
        bool IsActive,
        bool LockedBySuperAdmin,
        DateTimeOffset? ApprovedAt);

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
        string updatedBy,
        string status,
        bool featured,
        bool isActive,
        bool lockedBySuperAdmin,
        string? ownerUserId,
        DateTimeOffset? approvedAt,
        string? rejectionReason,
        DateTimeOffset? rejectedAt,
        DateTimeOffset createdAt,
        DateTimeOffset updatedAt)
    {
        ExecuteNonQuery(
            connection,
            transaction,
            """
            INSERT INTO dbo.Pois (
                Id, Slug, AddressLine, Latitude, Longitude, CategoryId, [Status], IsFeatured, IsActive, LockedBySuperAdmin,
                District, Ward, PriceRange, TriggerRadius, Priority, OwnerUserId,
                ApprovedAt, RejectionReason, RejectedAt, UpdatedBy, CreatedAt, UpdatedAt
            )
            VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?);
            """,
            poiId,
            request.Slug,
            request.Address,
            request.Lat,
            request.Lng,
            request.CategoryId,
            status,
            featured,
            isActive,
            lockedBySuperAdmin,
            request.District,
            request.Ward,
            request.PriceRange,
            request.TriggerRadius,
            request.Priority,
            ownerUserId,
            approvedAt,
            rejectionReason,
            rejectedAt,
            updatedBy,
            createdAt,
            updatedAt);
    }

    private void UpdatePoiRecord(
        SqlConnection connection,
        SqlTransaction transaction,
        string poiId,
        PoiUpsertRequest request,
        string updatedBy,
        string status,
        bool featured,
        bool isActive,
        bool lockedBySuperAdmin,
        string? ownerUserId,
        DateTimeOffset? approvedAt,
        string? rejectionReason,
        DateTimeOffset? rejectedAt,
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
                IsActive = ?,
                LockedBySuperAdmin = ?,
                District = ?,
                Ward = ?,
                PriceRange = ?,
                TriggerRadius = ?,
                Priority = ?,
                OwnerUserId = ?,
                ApprovedAt = ?,
                RejectionReason = ?,
                RejectedAt = ?,
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
            isActive,
            lockedBySuperAdmin,
            request.District,
            request.Ward,
            request.PriceRange,
            request.TriggerRadius,
            request.Priority,
            ownerUserId,
            approvedAt,
            rejectionReason,
            rejectedAt,
            updatedBy,
            updatedAt,
            poiId);
    }

    private void RenamePoiRecord(
        SqlConnection connection,
        SqlTransaction transaction,
        Poi existing,
        string nextPoiId,
        PoiUpsertRequest request,
        string updatedBy,
        string status,
        bool featured,
        bool isActive,
        bool lockedBySuperAdmin,
        string? ownerUserId,
        DateTimeOffset? approvedAt,
        string? rejectionReason,
        DateTimeOffset? rejectedAt,
        DateTimeOffset updatedAt)
    {
        _logger.LogDebug(
            "Executing POI rename. oldPoiId={OldPoiId}, newPoiId={NewPoiId}, ownerUserId={OwnerUserId}, status={Status}, updatedBy={UpdatedBy}",
            existing.Id,
            nextPoiId,
            ownerUserId,
            status,
            updatedBy);

        InsertPoiRecord(
            connection,
            transaction,
            nextPoiId,
            request,
            updatedBy,
            status,
            featured,
            isActive,
            lockedBySuperAdmin,
            ownerUserId,
            approvedAt,
            rejectionReason,
            rejectedAt,
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
        ExecuteNonQuery(connection, transaction, "UPDATE dbo.PoiTags SET PoiId = ? WHERE PoiId = ?;", nextPoiId, currentPoiId);
        ExecuteNonQuery(connection, transaction, "UPDATE dbo.FoodItems SET PoiId = ? WHERE PoiId = ?;", nextPoiId, currentPoiId);
        ExecuteNonQuery(connection, transaction, "UPDATE dbo.RouteStops SET PoiId = ? WHERE PoiId = ?;", nextPoiId, currentPoiId);
        ExecuteNonQuery(connection, transaction, "UPDATE dbo.Promotions SET PoiId = ? WHERE PoiId = ?;", nextPoiId, currentPoiId);
        ExecuteNonQuery(connection, transaction, "UPDATE dbo.ViewLogs SET PoiId = ? WHERE PoiId = ?;", nextPoiId, currentPoiId);
        ExecuteNonQuery(connection, transaction, "UPDATE dbo.AudioListenLogs SET PoiId = ? WHERE PoiId = ?;", nextPoiId, currentPoiId);
        ExecuteNonQuery(connection, transaction, "UPDATE dbo.AppUsageEvents SET PoiId = ? WHERE PoiId = ?;", nextPoiId, currentPoiId);
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

public bool DeletePoi(string id, AdminRequestContext actor)
    {
        using var connection = OpenConnection();
        using var transaction = connection.BeginTransaction();

        var poi = GetPoiById(connection, transaction, id);
        if (poi is null)
        {
            transaction.Rollback();
            return false;
        }

        if (actor.IsPlaceOwner && !GetOwnerPoiIds(connection, transaction, actor.UserId).Contains(poi.Id))
        {
            throw new ApiNotFoundException("Khong tim thay POI.");
        }

        ExecuteNonQuery(
            connection,
            transaction,
            """
            UPDATE dbo.Pois
            SET [Status] = ?,
                IsActive = ?,
                LockedBySuperAdmin = ?,
                ApprovedAt = ?,
                RejectionReason = NULL,
                RejectedAt = NULL,
                UpdatedBy = ?,
                UpdatedAt = ?
            WHERE Id = ?;
            """,
            "deleted",
            false,
            false,
            poi.ApprovedAt,
            actor.Name,
            DateTimeOffset.UtcNow,
            id);

        AppendAdminAuditLog(
            connection,
            transaction,
            actor,
            "Xoa mem POI",
            "POI",
            poi.Id,
            poi.Slug,
            $"status={poi.Status}",
            "status=deleted");

        transaction.Commit();
        return true;
    }

    public Translation SaveTranslation(string? id, TranslationUpsertRequest request, AdminRequestContext actor)
    {
        using var connection = OpenConnection();
        using var transaction = connection.BeginTransaction();
        EnsureActorCanManagePoiContentEntity(connection, transaction, actor, request.EntityType, request.EntityId, "chỉnh sửa nội dung POI");

        var now = DateTimeOffset.UtcNow;
        var settings = GetSettings(connection, transaction);
        var normalizedEntityType = NormalizeEntityType(request.EntityType);
        var normalizedLanguageCode = PremiumAccessCatalog.NormalizeLanguageCode(request.LanguageCode);
        var existing = !string.IsNullOrWhiteSpace(id)
            ? GetTranslationById(connection, transaction, id)
            : null;

        existing ??= GetTranslationByKey(connection, transaction, normalizedEntityType, request.EntityId, request.LanguageCode);

        var isNew = existing is null;
        var translationId = existing?.Id ?? id ?? CreateId("trans");
        var pendingTranslation = new Translation
        {
            Id = translationId,
            EntityType = normalizedEntityType,
            EntityId = request.EntityId,
            LanguageCode = request.LanguageCode,
            Title = request.Title,
            ShortText = request.ShortText,
            FullText = request.FullText,
            SeoTitle = request.SeoTitle,
            SeoDescription = request.SeoDescription,
            SourceLanguageCode = existing?.SourceLanguageCode,
            SourceHash = existing?.SourceHash,
            SourceUpdatedAt = existing?.SourceUpdatedAt,
            UpdatedBy = actor.Name,
            UpdatedAt = now
        };
        var sourceSnapshot = ResolveTranslationSourceSnapshot(
            connection,
            transaction,
            normalizedEntityType,
            request.EntityId,
            settings.DefaultLanguage,
            settings.FallbackLanguage,
            pendingTranslation);
        var isSourceTranslation = sourceSnapshot is not null &&
            string.Equals(normalizedLanguageCode, sourceSnapshot.LanguageCode, StringComparison.OrdinalIgnoreCase);

        _logger.LogInformation(
            "SaveTranslation request received. translationId={TranslationId}, entityType={EntityType}, entityId={EntityId}, languageCode={LanguageCode}, title={Title}, shortTextLength={ShortTextLength}, fullTextLength={FullTextLength}, isNew={IsNew}, sourceLanguageCode={SourceLanguageCode}, sourceHash={SourceHash}",
            translationId,
            normalizedEntityType,
            request.EntityId,
            request.LanguageCode,
            request.Title,
            request.ShortText?.Length ?? 0,
            request.FullText?.Length ?? 0,
            isNew,
            sourceSnapshot?.LanguageCode,
            sourceSnapshot?.Hash);

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
                    Id, EntityType, EntityId, LanguageCode, Title, ShortText, FullText, SeoTitle, SeoDescription, IsPremium,
                    SourceLanguageCode, SourceHash, SourceUpdatedAt, UpdatedBy, UpdatedAt
                )
                VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?);
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
                sourceSnapshot?.LanguageCode,
                sourceSnapshot?.Hash,
                sourceSnapshot?.UpdatedAt,
                actor.Name,
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
                actor.Name);
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
                    SourceLanguageCode = ?,
                    SourceHash = ?,
                    SourceUpdatedAt = ?,
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
                sourceSnapshot?.LanguageCode,
                sourceSnapshot?.Hash,
                sourceSnapshot?.UpdatedAt,
                actor.Name,
                now,
                translationId);
        }

        if (isSourceTranslation)
        {
            InvalidateDependentTranslations(
                connection,
                transaction,
                normalizedEntityType,
                request.EntityId,
                request.LanguageCode);

            _logger.LogDebug(
                "Invalidated dependent translations after source translation change. entityType={EntityType}, entityId={EntityId}, sourceLanguageCode={SourceLanguageCode}",
                normalizedEntityType,
                request.EntityId,
                request.LanguageCode);
        }

        TouchRelatedPoisForContentEntityChange(
            connection,
            transaction,
            existing?.EntityType,
            existing?.EntityId,
            normalizedEntityType,
            request.EntityId,
            now);

        AppendAdminAuditLog(
            connection,
            transaction,
            actor,
            isNew ? "Tao noi dung thuyet minh" : "Cap nhat noi dung thuyet minh",
            "TRANSLATION",
            request.EntityId,
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

    public bool DeleteTranslation(string id, AdminRequestContext actor)
    {
        using var connection = OpenConnection();
        using var transaction = connection.BeginTransaction();

        var now = DateTimeOffset.UtcNow;
        var settings = GetSettings(connection, transaction);
        var existing = GetTranslationById(connection, transaction, id);
        var normalizedExistingLanguageCode = PremiumAccessCatalog.NormalizeLanguageCode(existing?.LanguageCode);
        var invalidatesDependents =
            !string.IsNullOrWhiteSpace(existing?.EntityType) &&
            !string.IsNullOrWhiteSpace(existing?.EntityId) &&
            (
                string.Equals(normalizedExistingLanguageCode, PremiumAccessCatalog.NormalizeLanguageCode(settings.DefaultLanguage), StringComparison.OrdinalIgnoreCase) ||
                string.Equals(normalizedExistingLanguageCode, PremiumAccessCatalog.NormalizeLanguageCode(settings.FallbackLanguage), StringComparison.OrdinalIgnoreCase)
            );
        EnsureActorCanManagePoiContentEntity(connection, transaction, actor, existing?.EntityType, existing?.EntityId, "xóa nội dung POI");
        var deleted = ExecuteNonQuery(connection, transaction, "DELETE FROM dbo.PoiTranslations WHERE Id = ?;", id) > 0;
        if (deleted)
        {
            if (invalidatesDependents)
            {
                InvalidateDependentTranslations(
                    connection,
                    transaction,
                    NormalizeEntityType(existing!.EntityType),
                    existing.EntityId,
                    existing.LanguageCode);
            }

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
                "Xoa noi dung thuyet minh",
                "TRANSLATION",
                existing?.EntityId ?? id,
                $"{existing?.EntityType ?? "unknown"}:{existing?.LanguageCode ?? "unknown"}:{existing?.EntityId ?? id}");
        }

        transaction.Commit();
        return deleted;
    }

    public AudioGuide SaveAudioGuide(string? id, AudioGuideUpsertRequest request, AdminRequestContext actor)
    {
        using var connection = OpenConnection();
        using var transaction = connection.BeginTransaction();
        EnsureActorCanManagePoiContentEntity(connection, transaction, actor, request.EntityType, request.EntityId, "chỉnh sửa audio của POI");

        var now = DateTimeOffset.UtcNow;
        var normalizedEntityType = NormalizeEntityType(request.EntityType);
        var normalizedSourceType = string.Equals(request.SourceType?.Trim(), "uploaded", StringComparison.OrdinalIgnoreCase)
            ? "uploaded"
            : "tts";
        const string normalizedVoiceType = "standard";
        var normalizedAudioUrl = normalizedSourceType == "uploaded"
            ? (request.AudioUrl ?? string.Empty).Trim()
            : string.Empty;
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
            normalizedSourceType,
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
                normalizedAudioUrl,
                normalizedVoiceType,
                normalizedSourceType,
                request.Status,
                actor.Name,
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
                normalizedAudioUrl,
                normalizedVoiceType,
                normalizedSourceType,
                request.Status,
                actor.Name,
                now,
                audioId);
        }

        TouchRelatedPoisForContentEntityChange(
            connection,
            transaction,
            existing?.EntityType,
            existing?.EntityId,
            normalizedEntityType,
            request.EntityId,
            now);

        AppendAdminAuditLog(
            connection,
            transaction,
            actor,
            isNew ? "Tao audio guide" : "Cap nhat audio guide",
            "AUDIO_GUIDE",
            request.EntityId,
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

    public bool DeleteAudioGuide(string id, AdminRequestContext actor)
    {
        using var connection = OpenConnection();
        using var transaction = connection.BeginTransaction();

        var now = DateTimeOffset.UtcNow;
        var existing = GetAudioGuideById(connection, transaction, id);
        EnsureActorCanManagePoiContentEntity(connection, transaction, actor, existing?.EntityType, existing?.EntityId, "xóa audio của POI");
        var deleted = ExecuteNonQuery(connection, transaction, "DELETE FROM dbo.AudioGuides WHERE Id = ?;", id) > 0;
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
                "Xoa audio guide",
                "AUDIO_GUIDE",
                existing?.EntityId ?? id,
                $"{existing?.EntityType ?? "unknown"}:{existing?.LanguageCode ?? "unknown"}:{existing?.EntityId ?? id}");
        }

        transaction.Commit();
        return deleted;
    }
}
