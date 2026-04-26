using Microsoft.Data.SqlClient;
using VinhKhanh.BackendApi.Models;

namespace VinhKhanh.BackendApi.Infrastructure;

public sealed partial class AdminDataRepository
{
    public Poi ApprovePoi(string id, AdminRequestContext actor)
        => ModeratePoi(
            id,
            actor,
            "published",
            true,
            false,
            null,
            DateTimeOffset.UtcNow,
            "Duyệt POI",
            "status=published; isActive=true; lockedBySuperAdmin=false");

    public Poi RejectPoi(string id, string reason, AdminRequestContext actor)
    {
        var normalizedReason = NormalizePoiRejectionReason(reason);
        return ModeratePoi(
            id,
            actor,
            "rejected",
            false,
            false,
            normalizedReason,
            null,
            "Từ chối POI",
            $"status=rejected; rejectionReason={normalizedReason}");
    }

    public Poi TogglePoiActive(string id, bool isActive, AdminRequestContext actor)
    {
        using var connection = OpenConnection();
        using var transaction = connection.BeginTransaction();

        var existing = GetPoiById(connection, transaction, id)
            ?? throw new ApiNotFoundException("Không tìm thấy POI.");

        EnsurePoiToggleAllowed(connection, transaction, existing, actor);

        var nextStatus = isActive ? "published" : "draft";
        var nextLockedBySuperAdmin = !isActive && actor.IsSuperAdmin;
        var nextApprovedAt = existing.ApprovedAt ?? DateTimeOffset.UtcNow;

        var now = DateTimeOffset.UtcNow;
        ExecuteNonQuery(
            connection,
            transaction,
            """
            UPDATE dbo.Pois
            SET [Status] = ?,
                IsActive = ?,
                LockedBySuperAdmin = ?,
                ApprovedAt = ?,
                UpdatedBy = ?,
                UpdatedAt = ?
            WHERE Id = ?;
            """,
            nextStatus,
            isActive,
            nextLockedBySuperAdmin,
            nextApprovedAt,
            actor.Name,
            now,
            existing.Id);

        TouchPoiLocalizedAssets(connection, transaction, existing.Id, now);

        AppendAdminAuditLog(
            connection,
            transaction,
            actor,
            isActive ? "Admin bat hoat dong POI" : "Admin tat hoat dong POI",
            "POI",
            existing.Id,
            existing.Slug,
            $"status={existing.Status}; isActive={existing.IsActive.ToString().ToLowerInvariant()}; lockedBySuperAdmin={existing.LockedBySuperAdmin.ToString().ToLowerInvariant()}",
            $"status={nextStatus}; isActive={isActive.ToString().ToLowerInvariant()}; lockedBySuperAdmin={nextLockedBySuperAdmin.ToString().ToLowerInvariant()}");

        var saved = GetPoiById(connection, transaction, existing.Id)
            ?? throw new InvalidOperationException("Không thể đọc lại POI sau khi cập nhật trạng thái hoạt động.");

        transaction.Commit();
        return saved;
    }

    private Poi ModeratePoi(
        string id,
        AdminRequestContext actor,
        string nextStatus,
        bool nextIsActive,
        bool nextLockedBySuperAdmin,
        string? rejectionReason,
        DateTimeOffset? approvedAt,
        string action,
        string afterSummary)
    {
        EnsureSuperAdminCanModeratePoi(actor);

        using var connection = OpenConnection();
        using var transaction = connection.BeginTransaction();

        var existing = GetPoiById(connection, transaction, id)
            ?? throw new ApiNotFoundException("Không tìm thấy POI.");

        EnsurePendingPoiModerationAllowed(existing, nextStatus);

        var now = DateTimeOffset.UtcNow;
        DateTimeOffset? rejectedAt = string.Equals(nextStatus, "rejected", StringComparison.OrdinalIgnoreCase)
            ? now
            : null;
        var nextApprovedAt = string.Equals(nextStatus, "published", StringComparison.OrdinalIgnoreCase)
            ? approvedAt ?? now
            : existing.ApprovedAt;

        ExecuteNonQuery(
            connection,
            transaction,
            """
            UPDATE dbo.Pois
            SET [Status] = ?,
                IsActive = ?,
                LockedBySuperAdmin = ?,
                ApprovedAt = ?,
                RejectionReason = ?,
                RejectedAt = ?,
                UpdatedBy = ?,
                UpdatedAt = ?
            WHERE Id = ?;
            """,
            nextStatus,
            nextIsActive,
            nextLockedBySuperAdmin,
            nextApprovedAt,
            rejectionReason,
            rejectedAt,
            actor.Name,
            now,
            existing.Id);

        TouchPoiLocalizedAssets(connection, transaction, existing.Id, now);

        AppendAdminAuditLog(
            connection,
            transaction,
            actor,
            action,
            "POI",
            existing.Id,
            existing.Slug,
            $"status={existing.Status}",
            afterSummary);

        var saved = GetPoiById(connection, transaction, existing.Id)
            ?? throw new InvalidOperationException("Không thể đọc lại POI sau khi xét duyệt.");

        transaction.Commit();
        return saved;
    }

    private static void EnsureSuperAdminCanModeratePoi(AdminRequestContext actor)
    {
        if (!actor.IsSuperAdmin)
        {
            throw new ApiForbiddenException("Chi Admin moi duoc bat/tat trang thai hoat dong cua POI.");
        }
    }
    private static void EnsurePendingPoiModerationAllowed(Poi poi, string nextStatus)
    {
        if (string.Equals(poi.Status, "pending", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var actionLabel = string.Equals(nextStatus, "rejected", StringComparison.OrdinalIgnoreCase)
            ? "từ chối"
            : "duyệt";
        throw new ApiBadRequestException($"Chỉ có thể {actionLabel} POI đang chờ duyệt.");
    }

    private void EnsurePoiToggleAllowed(
        SqlConnection connection,
        SqlTransaction transaction,
        Poi poi,
        AdminRequestContext actor)
    {
        if (poi.ApprovedAt is null)
        {
            throw new ApiBadRequestException("Chỉ có thể đổi trạng thái hoạt động cho POI đã từng được duyệt.");
        }

        if (actor.IsSuperAdmin)
        {
            if (string.Equals(poi.Status, "published", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(poi.Status, "draft", StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            throw new ApiBadRequestException("Super Admin chỉ có thể đổi trạng thái hoạt động của POI đã được duyệt.");
        }

        if (!actor.IsSuperAdmin)
        {
            throw new ApiForbiddenException("Chi Admin moi duoc bat/tat trang thai hoat dong cua POI.");
        }
    }
    private static string NormalizePoiRejectionReason(string reason)
    {
        var normalizedReason = reason?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(normalizedReason))
        {
            throw new ApiBadRequestException("Lý do từ chối là bắt buộc.");
        }

        if (normalizedReason.Length > 1000)
        {
            throw new ApiBadRequestException("Lý do từ chối không được vượt quá 1000 ký tự.");
        }

        return normalizedReason;
    }

    private void TouchPoiLocalizedAssets(
        SqlConnection connection,
        SqlTransaction transaction,
        string poiId,
        DateTimeOffset updatedAt)
    {
        ExecuteNonQuery(
            connection,
            transaction,
            """
            UPDATE dbo.AudioGuides
            SET UpdatedAt = ?
            WHERE EntityId = ?
              AND EntityType IN (N'poi', N'place');
            """,
            updatedAt,
            poiId);
    }

    private static PoiReviewMetadata ResolvePoiReviewMetadataForSave(
        Poi? existing,
        string nextStatus,
        bool isOwnerActor)
    {
        if (isOwnerActor)
        {
            return new(null, null);
        }

        if (!string.Equals(nextStatus, "rejected", StringComparison.OrdinalIgnoreCase))
        {
            return new(null, null);
        }

        if (existing is null)
        {
            throw new ApiBadRequestException("POI mới không thể lưu trực tiếp ở trạng thái bị từ chối.");
        }

        if (!string.Equals(existing.Status, "rejected", StringComparison.OrdinalIgnoreCase))
        {
            throw new ApiBadRequestException("Hãy dùng thao tác từ chối và nhập lý do để từ chối POI.");
        }

        return new(existing.RejectionReason, existing.RejectedAt ?? existing.UpdatedAt);
    }

    private static bool IsRejectedPoi(Poi? poi)
        => poi is not null &&
           string.Equals(poi.Status, "rejected", StringComparison.OrdinalIgnoreCase);

    private sealed record PoiReviewMetadata(
        string? RejectionReason,
        DateTimeOffset? RejectedAt);
}
