using VinhKhanh.BackendApi.Contracts;
using VinhKhanh.BackendApi.Models;

namespace VinhKhanh.BackendApi.Infrastructure;

public sealed partial class AdminDataRepository
{
    public AuthTokensResponse? Login(string email, string password)
    {
        using var connection = OpenConnection();
        using var transaction = connection.BeginTransaction();

        var user = GetUserByCredentials(connection, transaction, email, password);
        if (user is null)
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
        AppendAuditLog(connection, transaction, user.Name, user.Role, "Dang nhap admin", user.Email);
        var session = CreateSession(connection, transaction, user);

        transaction.Commit();
        return session;
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

        AppendAuditLog(connection, transaction, user.Name, user.Role, "Lam moi phien dang nhap", user.Email);
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
        var managedPlaceId = string.Equals(request.Role, "PLACE_OWNER", StringComparison.OrdinalIgnoreCase)
            ? request.ManagedPlaceId
            : null;

        if (isNew)
        {
            ExecuteNonQuery(
                connection,
                transaction,
                """
                INSERT INTO dbo.AdminUsers (
                    Id, Name, Email, Phone, Role, [Password], [Status], CreatedAt, LastLoginAt, AvatarColor, ManagedPlaceId
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
                managedPlaceId);
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
                    ManagedPlaceId = ?
                WHERE Id = ?;
                """,
                request.Name,
                request.Email,
                request.Phone,
                request.Role,
                password,
                request.Status,
                request.AvatarColor,
                managedPlaceId,
                userId);
        }

        AppendAuditLog(
            connection,
            transaction,
            request.ActorName,
            request.ActorRole,
            isNew ? "Tao tai khoan admin" : "Cap nhat tai khoan admin",
            request.Email);

        var saved = GetUserById(connection, transaction, userId)
            ?? throw new InvalidOperationException("Khong the doc lai tai khoan admin sau khi luu.");

        transaction.Commit();
        return saved;
    }

    public Place SavePlace(string? id, PlaceUpsertRequest request)
    {
        using var connection = OpenConnection();
        using var transaction = connection.BeginTransaction();

        var now = DateTimeOffset.UtcNow;
        var existing = !string.IsNullOrWhiteSpace(id) ? GetPlaceById(connection, transaction, id) : null;
        var isNew = existing is null;
        var placeId = existing?.Id ?? id ?? CreateId("place");
        var createdAt = existing?.CreatedAt ?? now;

        if (isNew)
        {
            ExecuteNonQuery(
                connection,
                transaction,
                """
                INSERT INTO dbo.Places (
                    Id, Slug, AddressLine, Latitude, Longitude, CategoryId, [Status], IsFeatured, DefaultLanguageCode,
                    District, Ward, PriceRange, AverageVisitDurationMinutes, PopularityScore, OwnerUserId, UpdatedBy, CreatedAt, UpdatedAt
                )
                VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?);
                """,
                placeId,
                request.Slug,
                request.Address,
                request.Lat,
                request.Lng,
                request.CategoryId,
                request.Status,
                request.Featured,
                request.DefaultLanguageCode,
                request.District,
                request.Ward,
                request.PriceRange,
                request.AverageVisitDuration,
                request.PopularityScore,
                request.OwnerUserId,
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
                UPDATE dbo.Places
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
                request.Status,
                request.Featured,
                request.DefaultLanguageCode,
                request.District,
                request.Ward,
                request.PriceRange,
                request.AverageVisitDuration,
                request.PopularityScore,
                request.OwnerUserId,
                request.UpdatedBy,
                now,
                placeId);
        }

        ReplacePlaceTags(connection, transaction, placeId, request.Tags);
        UpsertPlaceQr(connection, transaction, placeId, request.Slug, request.Status);

        AppendAuditLog(
            connection,
            transaction,
            request.UpdatedBy,
            "SYSTEM",
            isNew ? "Tao dia diem" : "Cap nhat dia diem",
            request.Slug);

        var saved = GetPlaceById(connection, transaction, placeId)
            ?? throw new InvalidOperationException("Khong the doc lai dia diem sau khi luu.");

        transaction.Commit();
        return saved;
    }

    public bool DeletePlace(string id)
    {
        using var connection = OpenConnection();
        using var transaction = connection.BeginTransaction();

        var place = GetPlaceById(connection, transaction, id);
        if (place is null)
        {
            transaction.Rollback();
            return false;
        }

        ExecuteNonQuery(connection, transaction, "DELETE FROM dbo.CustomerFavoritePlaces WHERE PlaceId = ?;", id);
        ExecuteNonQuery(connection, transaction, "DELETE FROM dbo.RouteStops WHERE PlaceId = ?;", id);
        ExecuteNonQuery(connection, transaction, "UPDATE dbo.AdminUsers SET ManagedPlaceId = NULL WHERE ManagedPlaceId = ?;", id);
        ExecuteNonQuery(connection, transaction, "DELETE FROM dbo.PlaceTags WHERE PlaceId = ?;", id);
        ExecuteNonQuery(connection, transaction, "DELETE FROM dbo.PlaceTranslations WHERE EntityType = ? AND EntityId = ?;", "place", id);
        ExecuteNonQuery(connection, transaction, "DELETE FROM dbo.AudioGuides WHERE EntityType = ? AND EntityId = ?;", "place", id);
        ExecuteNonQuery(connection, transaction, "DELETE FROM dbo.MediaAssets WHERE EntityType = ? AND EntityId = ?;", "place", id);
        ExecuteNonQuery(connection, transaction, "DELETE FROM dbo.FoodItems WHERE PlaceId = ?;", id);
        ExecuteNonQuery(connection, transaction, "DELETE FROM dbo.Promotions WHERE PlaceId = ?;", id);
        ExecuteNonQuery(connection, transaction, "DELETE FROM dbo.Reviews WHERE PlaceId = ?;", id);
        ExecuteNonQuery(connection, transaction, "DELETE FROM dbo.ViewLogs WHERE PlaceId = ?;", id);
        ExecuteNonQuery(connection, transaction, "DELETE FROM dbo.AudioListenLogs WHERE PlaceId = ?;", id);
        ExecuteNonQuery(connection, transaction, "DELETE FROM dbo.QRCodes WHERE EntityType = ? AND EntityId = ?;", "place", id);
        ExecuteNonQuery(connection, transaction, "DELETE FROM dbo.Places WHERE Id = ?;", id);

        AppendAuditLog(connection, transaction, "SYSTEM", "SYSTEM", "Xoa dia diem", place.Slug);

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
                INSERT INTO dbo.PlaceTranslations (
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
                UPDATE dbo.PlaceTranslations
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
            isNew ? "Tao noi dung thuyet minh" : "Cap nhat noi dung thuyet minh",
            $"{request.EntityType}:{request.LanguageCode}:{request.EntityId}");

        var saved = GetTranslationById(connection, transaction, translationId)
            ?? throw new InvalidOperationException("Khong the doc lai noi dung thuyet minh sau khi luu.");

        transaction.Commit();
        return saved;
    }

    public bool DeleteTranslation(string id)
    {
        using var connection = OpenConnection();
        using var transaction = connection.BeginTransaction();

        var deleted = ExecuteNonQuery(connection, transaction, "DELETE FROM dbo.PlaceTranslations WHERE Id = ?;", id) > 0;
        if (deleted)
        {
            AppendAuditLog(connection, transaction, "SYSTEM", "SYSTEM", "Xoa noi dung thuyet minh", id);
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
            isNew ? "Tao audio guide" : "Cap nhat audio guide",
            $"{request.EntityType}:{request.LanguageCode}:{request.EntityId}");

        var saved = GetAudioGuideById(connection, transaction, audioId)
            ?? throw new InvalidOperationException("Khong the doc lai audio guide sau khi luu.");

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
            AppendAuditLog(connection, transaction, "SYSTEM", "SYSTEM", "Xoa audio guide", id);
        }

        transaction.Commit();
        return deleted;
    }
}
