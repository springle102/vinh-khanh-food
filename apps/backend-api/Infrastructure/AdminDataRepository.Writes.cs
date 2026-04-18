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
                Id, Slug, Title, ShortDescription, [Description], AudioScript, SourceLanguageCode,
                AddressLine, Latitude, Longitude, CategoryId, [Status], IsFeatured, IsActive, LockedBySuperAdmin,
                District, Ward, PriceRange, TriggerRadius, Priority, OwnerUserId,
                ApprovedAt, RejectionReason, RejectedAt, UpdatedBy, CreatedAt, UpdatedAt
            )
            VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?);
            """,
            poiId,
            request.Slug,
            request.Slug,
            string.Empty,
            string.Empty,
            string.Empty,
            "vi",
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

        ExecuteNonQuery(
            connection,
            transaction,
            """
            UPDATE dbo.Pois
            SET Title = ?,
                ShortDescription = ?,
                [Description] = ?,
                AudioScript = ?,
                SourceLanguageCode = ?
            WHERE Id = ?;
            """,
            existing.Title,
            existing.ShortDescription,
            existing.Description,
            existing.AudioScript,
            existing.SourceLanguageCode,
            nextPoiId);

        UpdatePoiReferenceIds(connection, transaction, existing.Id, nextPoiId);
        ExecuteNonQuery(connection, transaction, "DELETE FROM dbo.Pois WHERE Id = ?;", existing.Id);
    }

    private void UpdatePoiReferenceIds(
        SqlConnection connection,
        SqlTransaction transaction,
        string currentPoiId,
        string nextPoiId)
    {
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
        var saved = SaveTranslationSourceContentOnly(
            connection,
            transaction,
            request,
            actor);

        transaction.Commit();
        return saved;

    }

    public bool DeleteTranslation(string id, AdminRequestContext actor)
    {
        using var connection = OpenConnection();
        using var transaction = connection.BeginTransaction();

        var existing = GetTranslationById(connection, transaction, id);
        if (existing is null && (id.StartsWith("source-", StringComparison.OrdinalIgnoreCase) || id.StartsWith("runtime-", StringComparison.OrdinalIgnoreCase)))
        {
            throw new ApiBadRequestException("Noi dung goc va ban dich runtime khong the xoa qua endpoint translation.");
        }

        EnsureActorCanManagePoiContentEntity(connection, transaction, actor, existing?.EntityType, existing?.EntityId, "xóa nội dung POI");
        var deleted = ExecuteNonQuery(connection, transaction, "DELETE FROM dbo.PoiTranslations WHERE Id = ?;", id) > 0;
        if (deleted)
        {
            AppendAdminAuditLog(
                connection,
                transaction,
                actor,
                "Xoa ban dich cu da deprecated",
                "TRANSLATION",
                existing?.EntityId ?? id,
                $"{existing?.EntityType ?? "unknown"}:{existing?.LanguageCode ?? "unknown"}:{existing?.EntityId ?? id}");
        }

        transaction.Commit();
        return deleted;
    }

    private Translation SaveTranslationSourceContentOnly(
        SqlConnection connection,
        SqlTransaction transaction,
        TranslationUpsertRequest request,
        AdminRequestContext actor)
    {
        EnsureActorCanManagePoiContentEntity(connection, transaction, actor, request.EntityType, request.EntityId, "chỉnh sửa nội dung gốc");

        var now = DateTimeOffset.UtcNow;
        var settings = GetSettings(connection, transaction);
        var normalizedEntityType = NormalizeEntityType(request.EntityType);
        var normalizedLanguageCode = PremiumAccessCatalog.NormalizeLanguageCode(request.LanguageCode);
        var sourceLanguageCode = PremiumAccessCatalog.NormalizeLanguageCode(settings.DefaultLanguage);
        if (string.IsNullOrWhiteSpace(sourceLanguageCode))
        {
            sourceLanguageCode = "vi";
        }

        if (!string.Equals(normalizedLanguageCode, sourceLanguageCode, StringComparison.OrdinalIgnoreCase))
        {
            throw new ApiBadRequestException(
                $"Khong luu ban dich {normalizedLanguageCode} vao database. Backend chi luu noi dung goc {sourceLanguageCode} va dich runtime theo languageCode cua client.");
        }

        var title = (request.Title ?? string.Empty).Trim();
        var shortText = (request.ShortText ?? string.Empty).Trim();
        var fullText = (request.FullText ?? string.Empty).Trim();

        _logger.LogInformation(
            "Saving source-only content. entityType={EntityType}; entityId={EntityId}; sourceLanguage={SourceLanguage}; titleLength={TitleLength}; shortLength={ShortLength}; fullLength={FullLength}",
            normalizedEntityType,
            request.EntityId,
            sourceLanguageCode,
            title.Length,
            shortText.Length,
            fullText.Length);

        var saved = normalizedEntityType switch
        {
            "poi" => SavePoiSourceContent(connection, transaction, request.EntityId, sourceLanguageCode, title, shortText, fullText, actor.Name, now),
            "food_item" => SaveFoodItemSourceContent(connection, transaction, request.EntityId, sourceLanguageCode, title, shortText, fullText, actor.Name, now),
            "promotion" => SavePromotionSourceContent(connection, transaction, request.EntityId, sourceLanguageCode, title, shortText, fullText, actor.Name, now),
            "route" => SaveRouteSourceContent(connection, transaction, request.EntityId, sourceLanguageCode, title, shortText, fullText, actor.Name, now),
            _ => throw new ApiBadRequestException("Loai noi dung khong ho tro cap nhat source translation.")
        };

        if (string.Equals(normalizedEntityType, "poi", StringComparison.OrdinalIgnoreCase))
        {
            MarkPoiAudioGuidesOutdated(
                connection,
                transaction,
                request.EntityId,
                now,
                actor.Name,
                $"Source narration updated for {sourceLanguageCode}; generated audio must be refreshed.");
        }

        TouchRelatedPoisForContentEntityChange(
            connection,
            transaction,
            null,
            null,
            normalizedEntityType,
            request.EntityId,
            now);

        AppendAdminAuditLog(
            connection,
            transaction,
            actor,
            "Cap nhat noi dung goc",
            "TRANSLATION",
            request.EntityId,
            $"{normalizedEntityType}:{sourceLanguageCode}:{request.EntityId}");

        return saved;
    }

    private Translation SavePoiSourceContent(
        SqlConnection connection,
        SqlTransaction transaction,
        string poiId,
        string sourceLanguageCode,
        string title,
        string shortText,
        string fullText,
        string updatedBy,
        DateTimeOffset updatedAt)
    {
        var poi = GetPoiById(connection, transaction, poiId)
            ?? throw new ApiNotFoundException("Khong tim thay POI de cap nhat noi dung goc.");
        var nextTitle = string.IsNullOrWhiteSpace(title) ? poi.Slug : title;

        ExecuteNonQuery(
            connection,
            transaction,
            """
            UPDATE dbo.Pois
            SET Title = ?,
                ShortDescription = ?,
                [Description] = ?,
                AudioScript = ?,
                SourceLanguageCode = ?,
                UpdatedBy = ?,
                UpdatedAt = ?
            WHERE Id = ?;
            """,
            nextTitle,
            shortText,
            fullText,
            fullText,
            sourceLanguageCode,
            updatedBy,
            updatedAt,
            poiId);

        var saved = GetPoiById(connection, transaction, poiId)
            ?? throw new InvalidOperationException("Khong the doc lai POI sau khi cap nhat noi dung goc.");
        return CreateSourceTranslation(
            "poi",
            saved.Id,
            saved.SourceLanguageCode,
            string.IsNullOrWhiteSpace(saved.Title) ? saved.Slug : saved.Title,
            saved.ShortDescription,
            string.IsNullOrWhiteSpace(saved.AudioScript) ? saved.Description : saved.AudioScript,
            saved.UpdatedBy,
            saved.UpdatedAt);
    }

    private Translation SaveFoodItemSourceContent(
        SqlConnection connection,
        SqlTransaction transaction,
        string foodItemId,
        string sourceLanguageCode,
        string title,
        string shortText,
        string fullText,
        string updatedBy,
        DateTimeOffset updatedAt)
    {
        var foodItem = GetFoodItemById(connection, transaction, foodItemId)
            ?? throw new ApiNotFoundException("Khong tim thay mon an de cap nhat noi dung goc.");

        ExecuteNonQuery(
            connection,
            transaction,
            """
            UPDATE dbo.FoodItems
            SET Name = ?,
                [Description] = ?
            WHERE Id = ?;
            """,
            string.IsNullOrWhiteSpace(title) ? foodItem.Name : title,
            string.IsNullOrWhiteSpace(fullText) ? shortText : fullText,
            foodItemId);

        var saved = GetFoodItemById(connection, transaction, foodItemId)
            ?? throw new InvalidOperationException("Khong the doc lai mon an sau khi cap nhat noi dung goc.");
        return CreateSourceTranslation(
            "food_item",
            saved.Id,
            sourceLanguageCode,
            saved.Name,
            string.Empty,
            saved.Description,
            updatedBy,
            updatedAt);
    }

    private Translation SavePromotionSourceContent(
        SqlConnection connection,
        SqlTransaction transaction,
        string promotionId,
        string sourceLanguageCode,
        string title,
        string shortText,
        string fullText,
        string updatedBy,
        DateTimeOffset updatedAt)
    {
        var promotion = GetPromotionById(connection, transaction, promotionId)
            ?? throw new ApiNotFoundException("Khong tim thay uu dai de cap nhat noi dung goc.");

        ExecuteNonQuery(
            connection,
            transaction,
            """
            UPDATE dbo.Promotions
            SET Title = ?,
                [Description] = ?
            WHERE Id = ?;
            """,
            string.IsNullOrWhiteSpace(title) ? promotion.Title : title,
            string.IsNullOrWhiteSpace(fullText) ? shortText : fullText,
            promotionId);

        var saved = GetPromotionById(connection, transaction, promotionId)
            ?? throw new InvalidOperationException("Khong the doc lai uu dai sau khi cap nhat noi dung goc.");
        return CreateSourceTranslation(
            "promotion",
            saved.Id,
            sourceLanguageCode,
            saved.Title,
            string.Empty,
            saved.Description,
            updatedBy,
            updatedAt);
    }

    private Translation SaveRouteSourceContent(
        SqlConnection connection,
        SqlTransaction transaction,
        string routeId,
        string sourceLanguageCode,
        string title,
        string shortText,
        string fullText,
        string updatedBy,
        DateTimeOffset updatedAt)
    {
        var route = GetRouteById(connection, transaction, routeId)
            ?? throw new ApiNotFoundException("Khong tim thay tour de cap nhat noi dung goc.");

        ExecuteNonQuery(
            connection,
            transaction,
            """
            UPDATE dbo.Routes
            SET Name = ?,
                Theme = ?,
                [Description] = ?,
                UpdatedBy = ?,
                UpdatedAt = ?
            WHERE Id = ?;
            """,
            string.IsNullOrWhiteSpace(title) ? route.Name : title,
            string.IsNullOrWhiteSpace(shortText) ? route.Theme : shortText,
            fullText,
            updatedBy,
            updatedAt,
            routeId);

        var saved = GetRouteById(connection, transaction, routeId)
            ?? throw new InvalidOperationException("Khong the doc lai tour sau khi cap nhat noi dung goc.");
        return CreateSourceTranslation(
            "route",
            saved.Id,
            sourceLanguageCode,
            saved.Name,
            saved.Theme,
            saved.Description,
            saved.UpdatedBy,
            saved.UpdatedAt);
    }

    public AudioGuide SaveAudioGuide(string? id, AudioGuideUpsertRequest request, AdminRequestContext actor)
    {
        using var connection = OpenConnection();
        using var transaction = connection.BeginTransaction();
        EnsureActorCanManagePoiContentEntity(connection, transaction, actor, request.EntityType, request.EntityId, "chỉnh sửa audio của POI");

        var now = DateTimeOffset.UtcNow;
        var normalizedEntityType = NormalizeEntityType(request.EntityType);
        var normalizedLanguageCode = PremiumAccessCatalog.NormalizeLanguageCode(request.LanguageCode);
        var existing = !string.IsNullOrWhiteSpace(id)
            ? GetAudioGuideById(connection, transaction, id)
            : null;

        existing ??= GetAudioGuideByKey(connection, transaction, normalizedEntityType, request.EntityId, normalizedLanguageCode);

        var isNew = existing is null;
        var audioId = existing?.Id ?? id ?? CreateId("audio");
        var normalizedSourceType = AudioGuideCatalog.NormalizeSourceType(request.SourceType);
        var normalizedAudioUrl = (request.AudioUrl ?? string.Empty).Trim();
        var normalizedVoiceType = NormalizeAudioGuideVoiceType(request.VoiceType, existing);
        var transcriptText = NormalizeAudioGuideOptionalString(request.TranscriptText, existing?.TranscriptText);
        var audioFilePath = NormalizeAudioGuideOptionalString(request.AudioFilePath, existing?.AudioFilePath);
        var audioFileName = ResolveAudioGuideFileName(request.AudioFileName, audioFilePath, normalizedAudioUrl, existing);
        var provider = ResolveAudioGuideProvider(request.Provider, existing?.Provider, normalizedSourceType);
        var voiceId = NormalizeAudioGuideOptionalString(request.VoiceId, existing?.VoiceId);
        var modelId = NormalizeAudioGuideOptionalString(request.ModelId, existing?.ModelId);
        var outputFormat = ResolveAudioGuideOutputFormat(request.OutputFormat, existing?.OutputFormat, audioFileName, normalizedAudioUrl);
        var durationInSeconds = request.DurationInSeconds ?? existing?.DurationInSeconds;
        var fileSizeBytes = request.FileSizeBytes ?? existing?.FileSizeBytes;
        var textHash = NormalizeAudioGuideOptionalString(request.TextHash, existing?.TextHash);
        var contentVersion = NormalizeAudioGuideOptionalString(
            request.ContentVersion,
            !string.IsNullOrWhiteSpace(textHash) ? textHash : existing?.ContentVersion);
        var errorMessage = NormalizeAudioGuideNullableString(request.ErrorMessage);
        var isOutdated = ResolveAudioGuideOutdatedFlag(request.IsOutdated, existing, request.GenerationStatus, errorMessage);
        var generationStatus = ResolveAudioGuideGenerationStatus(
            request.GenerationStatus,
            request.Status,
            request.SourceType,
            normalizedAudioUrl,
            audioFilePath,
            isOutdated,
            errorMessage,
            existing);
        var normalizedStatus = AudioGuideCatalog.ResolvePublicStatus(
            generationStatus,
            HasAudioGuidePlaybackAsset(normalizedAudioUrl, audioFilePath),
            isOutdated);
        var generatedAt = ResolveAudioGuideGeneratedAt(
            request.GeneratedAt,
            existing?.GeneratedAt,
            generationStatus,
            normalizedStatus,
            now);

        _logger.LogInformation(
            "SaveAudioGuide request received. audioId={AudioId}, entityType={EntityType}, entityId={EntityId}, languageCode={LanguageCode}, sourceType={SourceType}, generationStatus={GenerationStatus}, status={Status}, isNew={IsNew}",
            audioId,
            normalizedEntityType,
            request.EntityId,
            normalizedLanguageCode,
            normalizedSourceType,
            generationStatus,
            normalizedStatus,
            isNew);

        if (isNew)
        {
            _logger.LogDebug(
                "Executing audio guide insert. audioId={AudioId}, entityType={EntityType}, entityId={EntityId}, languageCode={LanguageCode}",
                audioId,
                normalizedEntityType,
                request.EntityId,
                normalizedLanguageCode);
            ExecuteNonQuery(
                connection,
                transaction,
                """
                INSERT INTO dbo.AudioGuides (
                    Id, EntityType, EntityId, LanguageCode, TranscriptText, AudioUrl, AudioFilePath, AudioFileName,
                    VoiceType, SourceType, Provider, VoiceId, ModelId, OutputFormat, DurationInSeconds, FileSizeBytes,
                    TextHash, ContentVersion, GeneratedAt, GenerationStatus, ErrorMessage, IsOutdated, [Status], UpdatedBy, UpdatedAt
                )
                VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?);
                """,
                audioId,
                normalizedEntityType,
                request.EntityId,
                normalizedLanguageCode,
                transcriptText,
                normalizedAudioUrl,
                audioFilePath,
                audioFileName,
                normalizedVoiceType,
                normalizedSourceType,
                provider,
                voiceId,
                modelId,
                outputFormat,
                durationInSeconds,
                fileSizeBytes,
                textHash,
                contentVersion,
                generatedAt,
                generationStatus,
                errorMessage,
                isOutdated,
                normalizedStatus,
                actor.Name,
                now);
        }
        else
        {
            _logger.LogDebug(
                "Executing audio guide update. audioId={AudioId}, entityType={EntityType}, entityId={EntityId}, languageCode={LanguageCode}, generationStatus={GenerationStatus}, status={Status}",
                audioId,
                normalizedEntityType,
                request.EntityId,
                normalizedLanguageCode,
                generationStatus,
                normalizedStatus);
            ExecuteNonQuery(
                connection,
                transaction,
                """
                UPDATE dbo.AudioGuides
                SET EntityType = ?,
                    EntityId = ?,
                    LanguageCode = ?,
                    TranscriptText = ?,
                    AudioUrl = ?,
                    AudioFilePath = ?,
                    AudioFileName = ?,
                    VoiceType = ?,
                    SourceType = ?,
                    Provider = ?,
                    VoiceId = ?,
                    ModelId = ?,
                    OutputFormat = ?,
                    DurationInSeconds = ?,
                    FileSizeBytes = ?,
                    TextHash = ?,
                    ContentVersion = ?,
                    GeneratedAt = ?,
                    GenerationStatus = ?,
                    ErrorMessage = ?,
                    IsOutdated = ?,
                    [Status] = ?,
                    UpdatedBy = ?,
                    UpdatedAt = ?
                WHERE Id = ?;
                """,
                normalizedEntityType,
                request.EntityId,
                normalizedLanguageCode,
                transcriptText,
                normalizedAudioUrl,
                audioFilePath,
                audioFileName,
                normalizedVoiceType,
                normalizedSourceType,
                provider,
                voiceId,
                modelId,
                outputFormat,
                durationInSeconds,
                fileSizeBytes,
                textHash,
                contentVersion,
                generatedAt,
                generationStatus,
                errorMessage,
                isOutdated,
                normalizedStatus,
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
            $"{normalizedEntityType}:{normalizedLanguageCode}:{request.EntityId}");

        var saved = GetAudioGuideById(connection, transaction, audioId)
            ?? throw new InvalidOperationException("Không thể đọc lại audio guide sau khi lưu.");

        _logger.LogInformation(
            "SaveAudioGuide completed. audioId={AudioId}, entityType={EntityType}, entityId={EntityId}, languageCode={LanguageCode}, generationStatus={GenerationStatus}, status={Status}, updatedAt={UpdatedAt}",
            saved.Id,
            saved.EntityType,
            saved.EntityId,
            saved.LanguageCode,
            saved.GenerationStatus,
            saved.Status,
            saved.UpdatedAt);

        transaction.Commit();
        return saved;
    }

    private void MarkPoiAudioGuidesOutdated(
        SqlConnection connection,
        SqlTransaction transaction,
        string poiId,
        DateTimeOffset updatedAt,
        string updatedBy,
        string reason)
    {
        ExecuteNonQuery(
            connection,
            transaction,
            """
            UPDATE dbo.AudioGuides
            SET IsOutdated = CAST(1 AS bit),
                GenerationStatus = N'outdated',
                [Status] = N'missing',
                UpdatedBy = ?,
                UpdatedAt = ?,
                ErrorMessage = CASE
                    WHEN NULLIF(LTRIM(RTRIM(?)), N'') IS NULL THEN ErrorMessage
                    ELSE LEFT(?, 2000)
                END
            WHERE EntityType = N'poi'
              AND EntityId = ?
              AND (
                    COALESCE(IsOutdated, CAST(0 AS bit)) = CAST(0 AS bit)
                    OR LOWER(COALESCE(LTRIM(RTRIM(GenerationStatus)), N'')) <> N'outdated'
                    OR LOWER(COALESCE(LTRIM(RTRIM([Status])), N'')) <> N'missing'
                  );
            """,
            updatedBy,
            updatedAt,
            reason,
            reason,
            poiId);
    }

    private static string NormalizeAudioGuideVoiceType(string? requestedValue, AudioGuide? existing)
        => NormalizeAudioGuideOptionalString(requestedValue, existing?.VoiceType, "standard");

    private static string ResolveAudioGuideProvider(string? requestedValue, string? existingValue, string sourceType)
    {
        var provided = NormalizeAudioGuideNullableString(requestedValue);
        if (!string.IsNullOrWhiteSpace(provided))
        {
            return AudioGuideCatalog.NormalizeProvider(provided);
        }

        var existing = NormalizeAudioGuideNullableString(existingValue);
        if (!string.IsNullOrWhiteSpace(existing))
        {
            return AudioGuideCatalog.NormalizeProvider(existing);
        }

        return string.Equals(sourceType, AudioGuideCatalog.SourceTypeUploaded, StringComparison.OrdinalIgnoreCase)
            ? "uploaded"
            : AudioGuideCatalog.ProviderElevenLabs;
    }

    private static string ResolveAudioGuideOutputFormat(
        string? requestedValue,
        string? existingValue,
        string? audioFileName,
        string? audioUrl)
    {
        var explicitValue = NormalizeAudioGuideNullableString(requestedValue);
        if (!string.IsNullOrWhiteSpace(explicitValue))
        {
            return explicitValue;
        }

        var existing = NormalizeAudioGuideNullableString(existingValue);
        if (!string.IsNullOrWhiteSpace(existing))
        {
            return existing;
        }

        var fileName = !string.IsNullOrWhiteSpace(audioFileName)
            ? audioFileName
            : NormalizeAudioGuideNullableString(audioUrl);
        var extension = Path.GetExtension(fileName ?? string.Empty)?.ToLowerInvariant();
        return extension switch
        {
            ".wav" => "wav",
            ".ogg" => "ogg",
            ".opus" => "opus",
            _ => "mp3_44100_128"
        };
    }

    private static string ResolveAudioGuideFileName(
        string? requestedValue,
        string? audioFilePath,
        string? audioUrl,
        AudioGuide? existing)
    {
        var explicitValue = NormalizeAudioGuideNullableString(requestedValue);
        if (!string.IsNullOrWhiteSpace(explicitValue))
        {
            return explicitValue;
        }

        var fromPath = Path.GetFileName(NormalizeAudioGuideNullableString(audioFilePath) ?? string.Empty);
        if (!string.IsNullOrWhiteSpace(fromPath))
        {
            return fromPath;
        }

        var urlPath = NormalizeAudioGuideNullableString(audioUrl);
        if (!string.IsNullOrWhiteSpace(urlPath))
        {
            var withoutQuery = urlPath.Split('?', '#')[0];
            var fromUrl = Path.GetFileName(withoutQuery.Replace('/', Path.DirectorySeparatorChar));
            if (!string.IsNullOrWhiteSpace(fromUrl))
            {
                return fromUrl;
            }
        }

        return NormalizeAudioGuideOptionalString(null, existing?.AudioFileName);
    }

    private static bool ResolveAudioGuideOutdatedFlag(
        bool? requestedValue,
        AudioGuide? existing,
        string? requestedGenerationStatus,
        string? errorMessage)
    {
        if (requestedValue.HasValue)
        {
            return requestedValue.Value;
        }

        var generationStatus = AudioGuideCatalog.NormalizeGenerationStatus(requestedGenerationStatus);
        if (string.Equals(generationStatus, AudioGuideCatalog.GenerationStatusOutdated, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (!string.IsNullOrWhiteSpace(errorMessage))
        {
            return false;
        }

        return existing?.IsOutdated ?? false;
    }

    private static string ResolveAudioGuideGenerationStatus(
        string? requestedGenerationStatus,
        string? requestedStatus,
        string? requestedSourceType,
        string audioUrl,
        string audioFilePath,
        bool isOutdated,
        string? errorMessage,
        AudioGuide? existing)
    {
        if (isOutdated)
        {
            return AudioGuideCatalog.GenerationStatusOutdated;
        }

        var explicitGenerationStatus = NormalizeAudioGuideNullableString(requestedGenerationStatus);
        if (!string.IsNullOrWhiteSpace(explicitGenerationStatus))
        {
            return AudioGuideCatalog.NormalizeGenerationStatus(explicitGenerationStatus);
        }

        var hasPlaybackAsset = HasAudioGuidePlaybackAsset(audioUrl, audioFilePath);
        if (AudioGuideCatalog.NormalizePublicStatus(requestedStatus) == AudioGuideCatalog.PublicStatusProcessing)
        {
            return AudioGuideCatalog.GenerationStatusPending;
        }

        if (!string.IsNullOrWhiteSpace(errorMessage))
        {
            return AudioGuideCatalog.GenerationStatusFailed;
        }

        if (hasPlaybackAsset)
        {
            return AudioGuideCatalog.GenerationStatusSuccess;
        }

        if (string.Equals(requestedSourceType?.Trim(), AudioGuideCatalog.SourceTypeLegacyTts, StringComparison.OrdinalIgnoreCase))
        {
            return AudioGuideCatalog.GenerationStatusOutdated;
        }

        if (existing is not null &&
            string.IsNullOrWhiteSpace(audioUrl) &&
            string.IsNullOrWhiteSpace(audioFilePath))
        {
            return AudioGuideCatalog.NormalizeGenerationStatus(existing.GenerationStatus);
        }

        return AudioGuideCatalog.GenerationStatusNone;
    }

    private static DateTimeOffset? ResolveAudioGuideGeneratedAt(
        DateTimeOffset? requestedValue,
        DateTimeOffset? existingValue,
        string generationStatus,
        string publicStatus,
        DateTimeOffset now)
    {
        if (requestedValue.HasValue)
        {
            return requestedValue;
        }

        if (string.Equals(generationStatus, AudioGuideCatalog.GenerationStatusSuccess, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(publicStatus, AudioGuideCatalog.PublicStatusReady, StringComparison.OrdinalIgnoreCase))
        {
            return existingValue ?? now;
        }

        return existingValue;
    }

    private static string NormalizeAudioGuideOptionalString(string? requestedValue, string? existingValue, string fallback = "")
    {
        if (requestedValue is not null)
        {
            return requestedValue.Trim();
        }

        if (!string.IsNullOrWhiteSpace(existingValue))
        {
            return existingValue.Trim();
        }

        return fallback;
    }

    private static string? NormalizeAudioGuideNullableString(string? value)
    {
        if (value is null)
        {
            return null;
        }

        var trimmed = value.Trim();
        return trimmed.Length == 0 ? string.Empty : trimmed;
    }

    private static bool HasAudioGuidePlaybackAsset(string? audioUrl, string? audioFilePath)
        => !string.IsNullOrWhiteSpace(audioUrl) || !string.IsNullOrWhiteSpace(audioFilePath);

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
