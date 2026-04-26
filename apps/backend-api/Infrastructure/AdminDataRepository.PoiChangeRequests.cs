using Microsoft.Data.SqlClient;
using System.Text.Json;
using VinhKhanh.BackendApi.Contracts;
using VinhKhanh.BackendApi.Models;
using VinhKhanh.Core.Pois;

namespace VinhKhanh.BackendApi.Infrastructure;

public sealed partial class AdminDataRepository
{
    private static readonly JsonSerializerOptions PoiChangeRequestJsonOptions = new(JsonSerializerDefaults.Web);

    public IReadOnlyList<PoiChangeRequest> GetPoiChangeRequests(AdminRequestContext actor)
    {
        using var connection = OpenConnection();
        var requests = GetPoiChangeRequests(connection, null);

        if (actor.IsSuperAdmin)
        {
            return requests;
        }

        if (!actor.IsPlaceOwner)
        {
            return [];
        }

        var ownerPoiIds = GetOwnerPoiIds(connection, null, actor.UserId);
        return requests
            .Where(request =>
                string.Equals(request.SubmittedByUserId, actor.UserId, StringComparison.OrdinalIgnoreCase) ||
                ownerPoiIds.Contains(request.PoiId))
            .ToList();
    }

    public PoiChangeRequest SubmitPoiChangeRequest(
        string poiId,
        PoiChangeRequestCreateRequest request,
        AdminRequestContext actor)
    {
        if (!actor.IsPlaceOwner)
        {
            throw new ApiForbiddenException("Chi chu quan moi gui yeu cau sua POI qua luong duyet.");
        }

        ArgumentNullException.ThrowIfNull(request);
        if (request.Poi is null)
        {
            throw new ApiBadRequestException("Noi dung POI thay doi la bat buoc.");
        }

        using var connection = OpenConnection();
        using var transaction = connection.BeginTransaction();

        var existing = GetPoiById(connection, transaction, poiId)
            ?? throw new ApiNotFoundException("Khong tim thay POI.");
        if (!GetOwnerPoiIds(connection, transaction, actor.UserId).Contains(existing.Id))
        {
            throw new ApiForbiddenException("Chu quan chi duoc gui yeu cau sua POI cua minh.");
        }

        var normalizedPoi = NormalizePoiRequestForPersistence(request.Poi with
        {
            RequestedId = existing.Id,
            Status = existing.Status,
            OwnerUserId = existing.OwnerUserId,
            UpdatedBy = actor.Name,
            ActorRole = actor.Role,
            ActorUserId = actor.UserId
        });

        ValidatePoiChangeRequest(normalizedPoi, request);

        var now = DateTimeOffset.UtcNow;
        var changeRequestId = CreateId("poi-change");
        var beforePayload = BuildPoiChangeRequestPayload(existing, GetSettings(connection, transaction).DefaultLanguage);
        var afterPayload = new PoiChangeRequestPayload(
            normalizedPoi,
            PremiumAccessCatalog.NormalizeLanguageCode(request.LanguageCode),
            (request.Title ?? string.Empty).Trim(),
            (request.FullText ?? string.Empty).Trim(),
            string.IsNullOrWhiteSpace(request.SeoTitle) ? null : request.SeoTitle.Trim(),
            string.IsNullOrWhiteSpace(request.SeoDescription) ? null : request.SeoDescription.Trim());

        ExecuteNonQuery(
            connection,
            transaction,
            """
            INSERT INTO dbo.PoiChangeRequests (
                Id, PoiId, SubmittedByUserId, SubmittedByName, BeforeJson, AfterJson, [Status],
                RejectionReason, SubmittedAt, ReviewedAt, ReviewedByUserId, ReviewedByName, AppliedAt
            )
            VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?);
            """,
            changeRequestId,
            existing.Id,
            actor.UserId,
            actor.Name,
            JsonSerializer.Serialize(beforePayload, PoiChangeRequestJsonOptions),
            JsonSerializer.Serialize(afterPayload, PoiChangeRequestJsonOptions),
            "pending",
            null,
            now,
            null,
            null,
            null,
            null);

        AppendAdminAuditLog(
            connection,
            transaction,
            actor,
            "Chu quan gui yeu cau sua POI",
            "POI_CHANGE_REQUEST",
            changeRequestId,
            $"{existing.Id}:{existing.Slug}",
            $"poiUpdatedAt={existing.UpdatedAt:O}",
            "status=pending");

        var saved = GetPoiChangeRequestById(connection, transaction, changeRequestId)
            ?? throw new InvalidOperationException("Khong the doc lai yeu cau sua POI sau khi tao.");

        transaction.Commit();
        return saved;
    }

    public Poi ApprovePoiChangeRequest(string id, AdminRequestContext actor)
    {
        if (!actor.IsSuperAdmin)
        {
            throw new ApiForbiddenException("Chi Admin moi duoc duyet yeu cau sua POI.");
        }

        using var connection = OpenConnection();
        using var transaction = connection.BeginTransaction();

        var changeRequest = GetPoiChangeRequestById(connection, transaction, id)
            ?? throw new ApiNotFoundException("Khong tim thay yeu cau sua POI.");
        EnsurePoiChangeRequestPending(changeRequest);

        var existing = GetPoiById(connection, transaction, changeRequest.PoiId)
            ?? throw new ApiNotFoundException("Khong tim thay POI can ap dung thay doi.");
        var payload = DeserializePoiChangeRequestPayload(changeRequest.AfterJson);
        var now = DateTimeOffset.UtcNow;
        var normalizedPoi = NormalizePoiRequestForPersistence(payload.Poi with
        {
            RequestedId = existing.Id,
            Status = existing.Status,
            OwnerUserId = existing.OwnerUserId,
            UpdatedBy = actor.Name,
            ActorRole = actor.Role,
            ActorUserId = actor.UserId
        });

        UpdatePoiRecord(
            connection,
            transaction,
            existing.Id,
            normalizedPoi,
            actor.Name,
            existing.Status,
            existing.Featured,
            existing.IsActive,
            existing.LockedBySuperAdmin,
            existing.OwnerUserId,
            existing.ApprovedAt,
            existing.RejectionReason,
            existing.RejectedAt,
            now);
        ReplacePoiTags(connection, transaction, existing.Id, normalizedPoi.Tags);

        var settings = GetSettings(connection, transaction);
        var sourceLanguageCode = PremiumAccessCatalog.NormalizeLanguageCode(settings.DefaultLanguage);
        SavePoiSourceContent(
            connection,
            transaction,
            existing.Id,
            sourceLanguageCode,
            payload.Title,
            string.Empty,
            payload.FullText,
            actor.Name,
            now);
        MarkPoiAudioGuidesOutdated(
            connection,
            transaction,
            existing.Id,
            now,
            actor.Name,
            "POI change request approved; generated audio must be refreshed.");

        ExecuteNonQuery(
            connection,
            transaction,
            """
            UPDATE dbo.PoiChangeRequests
            SET [Status] = ?,
                ReviewedAt = ?,
                ReviewedByUserId = ?,
                ReviewedByName = ?,
                AppliedAt = ?,
                RejectionReason = NULL
            WHERE Id = ?;
            """,
            "approved",
            now,
            actor.UserId,
            actor.Name,
            now,
            changeRequest.Id);

        AppendAdminAuditLog(
            connection,
            transaction,
            actor,
            "Admin duyet yeu cau sua POI",
            "POI_CHANGE_REQUEST",
            changeRequest.Id,
            $"{existing.Id}:{existing.Slug}",
            "status=pending",
            "status=approved");

        var saved = GetPoiById(connection, transaction, existing.Id)
            ?? throw new InvalidOperationException("Khong the doc lai POI sau khi duyet yeu cau sua.");

        transaction.Commit();
        return saved;
    }

    public PoiChangeRequest RejectPoiChangeRequest(string id, string? reason, AdminRequestContext actor)
    {
        if (!actor.IsSuperAdmin)
        {
            throw new ApiForbiddenException("Chi Admin moi duoc tu choi yeu cau sua POI.");
        }

        var normalizedReason = (reason ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(normalizedReason))
        {
            throw new ApiBadRequestException("Ly do tu choi la bat buoc.");
        }

        using var connection = OpenConnection();
        using var transaction = connection.BeginTransaction();

        var changeRequest = GetPoiChangeRequestById(connection, transaction, id)
            ?? throw new ApiNotFoundException("Khong tim thay yeu cau sua POI.");
        EnsurePoiChangeRequestPending(changeRequest);

        var now = DateTimeOffset.UtcNow;
        ExecuteNonQuery(
            connection,
            transaction,
            """
            UPDATE dbo.PoiChangeRequests
            SET [Status] = ?,
                RejectionReason = ?,
                ReviewedAt = ?,
                ReviewedByUserId = ?,
                ReviewedByName = ?
            WHERE Id = ?;
            """,
            "rejected",
            normalizedReason,
            now,
            actor.UserId,
            actor.Name,
            changeRequest.Id);

        AppendAdminAuditLog(
            connection,
            transaction,
            actor,
            "Admin tu choi yeu cau sua POI",
            "POI_CHANGE_REQUEST",
            changeRequest.Id,
            changeRequest.PoiId,
            "status=pending",
            $"status=rejected; reason={normalizedReason}");

        var saved = GetPoiChangeRequestById(connection, transaction, changeRequest.Id)
            ?? throw new InvalidOperationException("Khong the doc lai yeu cau sua POI sau khi tu choi.");

        transaction.Commit();
        return saved;
    }

    private IReadOnlyList<PoiChangeRequest> GetPoiChangeRequests(SqlConnection connection, SqlTransaction? transaction)
    {
        if (!TableExists(connection, transaction, "PoiChangeRequests"))
        {
            return [];
        }

        const string sql = """
            SELECT Id, PoiId, SubmittedByUserId, SubmittedByName, BeforeJson, AfterJson, [Status],
                   RejectionReason, SubmittedAt, ReviewedAt, ReviewedByUserId, ReviewedByName, AppliedAt
            FROM dbo.PoiChangeRequests
            ORDER BY SubmittedAt DESC, Id DESC;
            """;

        using var command = CreateCommand(connection, transaction, sql);
        using var reader = command.ExecuteReader();

        var items = new List<PoiChangeRequest>();
        while (reader.Read())
        {
            items.Add(MapPoiChangeRequest(reader));
        }

        return items;
    }

    private PoiChangeRequest? GetPoiChangeRequestById(SqlConnection connection, SqlTransaction? transaction, string id)
    {
        if (!TableExists(connection, transaction, "PoiChangeRequests"))
        {
            return null;
        }

        const string sql = """
            SELECT TOP 1 Id, PoiId, SubmittedByUserId, SubmittedByName, BeforeJson, AfterJson, [Status],
                   RejectionReason, SubmittedAt, ReviewedAt, ReviewedByUserId, ReviewedByName, AppliedAt
            FROM dbo.PoiChangeRequests
            WHERE Id = ?;
            """;

        using var command = CreateCommand(connection, transaction, sql, id);
        using var reader = command.ExecuteReader();
        return reader.Read() ? MapPoiChangeRequest(reader) : null;
    }

    private static PoiChangeRequest MapPoiChangeRequest(SqlDataReader reader)
        => new()
        {
            Id = ReadString(reader, "Id"),
            PoiId = ReadString(reader, "PoiId"),
            SubmittedByUserId = ReadString(reader, "SubmittedByUserId"),
            SubmittedByName = ReadString(reader, "SubmittedByName"),
            BeforeJson = ReadString(reader, "BeforeJson"),
            AfterJson = ReadString(reader, "AfterJson"),
            Status = NormalizePoiChangeRequestStatus(ReadString(reader, "Status")),
            RejectionReason = ReadNullableString(reader, "RejectionReason"),
            SubmittedAt = ReadDateTimeOffset(reader, "SubmittedAt"),
            ReviewedAt = ReadNullableDateTimeOffset(reader, "ReviewedAt"),
            ReviewedByUserId = ReadNullableString(reader, "ReviewedByUserId"),
            ReviewedByName = ReadNullableString(reader, "ReviewedByName"),
            AppliedAt = ReadNullableDateTimeOffset(reader, "AppliedAt")
        };

    private static void ValidatePoiChangeRequest(PoiUpsertRequest poi, PoiChangeRequestCreateRequest request)
    {
        if (string.IsNullOrWhiteSpace(poi.Slug) ||
            string.IsNullOrWhiteSpace(poi.Address) ||
            string.IsNullOrWhiteSpace(poi.CategoryId))
        {
            throw new ApiBadRequestException("Slug, dia chi va phan loai POI la bat buoc.");
        }

        if (string.IsNullOrWhiteSpace(request.Title))
        {
            throw new ApiBadRequestException("Ten POI la bat buoc.");
        }
    }

    private static void EnsurePoiChangeRequestPending(PoiChangeRequest request)
    {
        if (!string.Equals(request.Status, "pending", StringComparison.OrdinalIgnoreCase))
        {
            throw new ApiBadRequestException("Chi co the xu ly yeu cau sua POI dang pending.");
        }
    }

    private static PoiChangeRequestPayload DeserializePoiChangeRequestPayload(string json)
        => JsonSerializer.Deserialize<PoiChangeRequestPayload>(json, PoiChangeRequestJsonOptions)
           ?? throw new ApiBadRequestException("Noi dung yeu cau sua POI khong hop le.");

    private static string NormalizePoiChangeRequestStatus(string? status)
        => status?.Trim().ToLowerInvariant() switch
        {
            "approved" => "approved",
            "rejected" => "rejected",
            _ => "pending"
        };

    private static PoiChangeRequestPayload BuildPoiChangeRequestPayload(Poi poi, string sourceLanguageCode)
        => new(
            new PoiUpsertRequest(
                poi.Slug,
                poi.Address,
                poi.Lat,
                poi.Lng,
                poi.CategoryId,
                poi.Status,
                poi.District,
                poi.Ward,
                poi.PriceRange,
                poi.TriggerRadius,
                poi.Priority,
                poi.PlaceTier,
                [.. poi.Tags],
                poi.OwnerUserId,
                poi.UpdatedBy,
                string.Empty,
                string.Empty,
                poi.Id),
            PremiumAccessCatalog.NormalizeLanguageCode(sourceLanguageCode),
            string.IsNullOrWhiteSpace(poi.Title) ? poi.Slug : poi.Title,
            string.IsNullOrWhiteSpace(poi.AudioScript) ? poi.Description : poi.AudioScript,
            string.IsNullOrWhiteSpace(poi.Title) ? poi.Slug : poi.Title,
            string.IsNullOrWhiteSpace(poi.ShortDescription) ? poi.Description : poi.ShortDescription);

    private sealed record PoiChangeRequestPayload(
        PoiUpsertRequest Poi,
        string LanguageCode,
        string Title,
        string FullText,
        string? SeoTitle,
        string? SeoDescription);
}
