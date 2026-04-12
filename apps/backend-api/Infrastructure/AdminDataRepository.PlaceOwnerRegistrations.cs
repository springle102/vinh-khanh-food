using System.Data;
using System.Net.Mail;
using Microsoft.Data.SqlClient;
using VinhKhanh.BackendApi.Contracts;
using VinhKhanh.BackendApi.Models;

namespace VinhKhanh.BackendApi.Infrastructure;

public sealed partial class AdminDataRepository
{
    private const string DefaultAdminAvatarColor = "#f97316";

    public AdminUser CreatePlaceOwnerRegistration(PlaceOwnerRegistrationRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        using var connection = OpenConnection();
        using var transaction = connection.BeginTransaction(IsolationLevel.Serializable);

        var normalizedName = NormalizePlaceOwnerRegistrationValue(request.Name, "Họ tên", 120);
        var normalizedEmail = NormalizePlaceOwnerRegistrationEmail(request.Email);
        var normalizedPhone = NormalizePlaceOwnerRegistrationPhone(request.Phone);
        var normalizedPassword = NormalizePlaceOwnerRegistrationPassword(request.Password);
        EnsureRegistrationPasswordsMatch(request.Password, request.ConfirmPassword);

        var existingRows = GetAdminIdentityRowsForRegistration(connection, transaction);
        EnsureAdminEmailIsAvailable(existingRows, normalizedEmail);

        var now = DateTimeOffset.UtcNow;
        var userId = CreateId("user");

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
            normalizedName,
            normalizedEmail,
            normalizedPhone,
            AdminRoleCatalog.PlaceOwner,
            normalizedPassword,
            "locked",
            now,
            null,
            DefaultAdminAvatarColor,
            null,
            AdminApprovalCatalog.Pending,
            null,
            now,
            null);

        AppendAdminAuditLog(
            connection,
            transaction,
            userId,
            normalizedName,
            AdminRoleCatalog.PlaceOwner,
            "Gửi đăng ký chủ quán",
            "OWNER_REGISTRATION",
            userId,
            normalizedEmail,
            null,
            BuildPlaceOwnerApprovalSummary(AdminApprovalCatalog.Pending, null));

        var saved = GetUserById(connection, transaction, userId)
            ?? throw new InvalidOperationException("Không thể đọc lại hồ sơ đăng ký sau khi lưu.");

        transaction.Commit();
        return saved;
    }

    public AdminUser AccessPlaceOwnerRegistration(PlaceOwnerRegistrationAccessRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        using var connection = OpenConnection();
        var normalizedEmail = NormalizePlaceOwnerRegistrationEmail(request.Email);
        var normalizedPassword = NormalizePlaceOwnerRegistrationPassword(request.Password);
        var user = GetUserByCredentialsIgnoringStatus(connection, null, normalizedEmail, normalizedPassword);

        if (user is null || !AdminRoleCatalog.IsPlaceOwner(user.Role))
        {
            throw new ApiUnauthorizedException("Email hoặc mật khẩu không đúng.");
        }

        return user;
    }

    public AdminUser ResubmitPlaceOwnerRegistration(string id, PlaceOwnerRegistrationResubmitRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        using var connection = OpenConnection();
        using var transaction = connection.BeginTransaction(IsolationLevel.Serializable);

        var existing = GetUserById(connection, transaction, id);
        EnsurePlaceOwnerRegistrationExists(existing);

        var normalizedCurrentPassword = NormalizePlaceOwnerRegistrationPassword(request.CurrentPassword);
        if (!string.Equals(existing!.Password, normalizedCurrentPassword, StringComparison.Ordinal))
        {
            throw new ApiUnauthorizedException("Mật khẩu hiện tại không đúng.");
        }

        if (!AdminApprovalCatalog.IsRejected(existing.ApprovalStatus))
        {
            throw new InvalidOperationException("Chỉ hồ sơ bị từ chối mới có thể chỉnh sửa và gửi lại.");
        }

        var normalizedName = NormalizePlaceOwnerRegistrationValue(request.Name, "Họ tên", 120);
        var normalizedEmail = NormalizePlaceOwnerRegistrationEmail(request.Email);
        var normalizedPhone = NormalizePlaceOwnerRegistrationPhone(request.Phone);
        var normalizedPassword = NormalizePlaceOwnerRegistrationPassword(request.Password);
        EnsureRegistrationPasswordsMatch(request.Password, request.ConfirmPassword);

        var existingRows = GetAdminIdentityRowsForRegistration(connection, transaction);
        EnsureAdminEmailIsAvailable(existingRows, normalizedEmail, existing.Id);

        var now = DateTimeOffset.UtcNow;
        ExecuteNonQuery(
            connection,
            transaction,
            """
            UPDATE dbo.AdminUsers
            SET Name = ?,
                Email = ?,
                Phone = ?,
                [Password] = ?,
                [Status] = ?,
                AvatarColor = COALESCE(NULLIF(AvatarColor, N''), ?),
                ApprovalStatus = ?,
                RejectionReason = NULL,
                RegistrationSubmittedAt = ?,
                RegistrationReviewedAt = NULL
            WHERE Id = ?;
            """,
            normalizedName,
            normalizedEmail,
            normalizedPhone,
            normalizedPassword,
            "locked",
            DefaultAdminAvatarColor,
            AdminApprovalCatalog.Pending,
            now,
            existing.Id);

        AppendAdminAuditLog(
            connection,
            transaction,
            existing.Id,
            normalizedName,
            AdminRoleCatalog.PlaceOwner,
            "Chỉnh sửa hồ sơ đăng ký chủ quán sau khi bị từ chối",
            "OWNER_REGISTRATION",
            existing.Id,
            normalizedEmail,
            BuildPlaceOwnerProfileSummary(existing),
            BuildPlaceOwnerProfileSummary(normalizedName, normalizedEmail, normalizedPhone));

        AppendAdminAuditLog(
            connection,
            transaction,
            existing.Id,
            normalizedName,
            AdminRoleCatalog.PlaceOwner,
            "Gửi lại hồ sơ đăng ký chủ quán",
            "OWNER_REGISTRATION",
            existing.Id,
            normalizedEmail,
            BuildPlaceOwnerApprovalSummary(existing.ApprovalStatus, existing.RejectionReason),
            BuildPlaceOwnerApprovalSummary(AdminApprovalCatalog.Pending, null));

        var saved = GetUserById(connection, transaction, existing.Id)
            ?? throw new InvalidOperationException("Không thể đọc lại hồ sơ đăng ký sau khi gửi lại.");

        transaction.Commit();
        return saved;
    }

    public AdminUser ApprovePlaceOwnerRegistration(string id, AdminRequestContext actor)
    {
        if (!actor.IsSuperAdmin)
        {
            throw new ApiForbiddenException("Chỉ super admin mới được phê duyệt hồ sơ chủ quán.");
        }

        using var connection = OpenConnection();
        using var transaction = connection.BeginTransaction();

        var existing = GetUserById(connection, transaction, id);
        EnsurePlaceOwnerRegistrationExists(existing);

        var beforeSummary = BuildPlaceOwnerApprovalSummary(existing!.ApprovalStatus, existing.RejectionReason);
        var reviewedAt = DateTimeOffset.UtcNow;

        ExecuteNonQuery(
            connection,
            transaction,
            """
            UPDATE dbo.AdminUsers
            SET [Status] = ?,
                ApprovalStatus = ?,
                RejectionReason = NULL,
                RegistrationSubmittedAt = COALESCE(RegistrationSubmittedAt, CreatedAt),
                RegistrationReviewedAt = ?
            WHERE Id = ?;
            """,
            "active",
            AdminApprovalCatalog.Approved,
            reviewedAt,
            existing.Id);

        AppendAdminAuditLog(
            connection,
            transaction,
            actor,
            "Duyệt hồ sơ đăng ký chủ quán",
            "OWNER_REGISTRATION",
            existing.Id,
            existing.Email,
            beforeSummary,
            BuildPlaceOwnerApprovalSummary(AdminApprovalCatalog.Approved, null));

        var saved = GetUserById(connection, transaction, existing.Id)
            ?? throw new InvalidOperationException("Không thể đọc lại hồ sơ đăng ký sau khi phê duyệt.");

        transaction.Commit();
        return saved;
    }

    public AdminUser RejectPlaceOwnerRegistration(string id, string? reason, AdminRequestContext actor)
    {
        if (!actor.IsSuperAdmin)
        {
            throw new ApiForbiddenException("Chỉ super admin mới được từ chối hồ sơ chủ quán.");
        }

        using var connection = OpenConnection();
        using var transaction = connection.BeginTransaction();

        var existing = GetUserById(connection, transaction, id);
        EnsurePlaceOwnerRegistrationExists(existing);

        if (AdminApprovalCatalog.IsApproved(existing!.ApprovalStatus))
        {
            throw new InvalidOperationException("Hồ sơ đã được phê duyệt. Hãy quản lý khóa tài khoản ở trang tài khoản chủ quán.");
        }

        var normalizedReason = NormalizePlaceOwnerRegistrationValue(reason, "Lý do từ chối", 1000);
        var beforeSummary = BuildPlaceOwnerApprovalSummary(existing.ApprovalStatus, existing.RejectionReason);
        var reviewedAt = DateTimeOffset.UtcNow;

        ExecuteNonQuery(
            connection,
            transaction,
            """
            UPDATE dbo.AdminUsers
            SET [Status] = ?,
                ApprovalStatus = ?,
                RejectionReason = ?,
                RegistrationSubmittedAt = COALESCE(RegistrationSubmittedAt, CreatedAt),
                RegistrationReviewedAt = ?
            WHERE Id = ?;
            """,
            "locked",
            AdminApprovalCatalog.Rejected,
            normalizedReason,
            reviewedAt,
            existing.Id);

        AppendAdminAuditLog(
            connection,
            transaction,
            actor,
            "Từ chối hồ sơ đăng ký chủ quán",
            "OWNER_REGISTRATION",
            existing.Id,
            existing.Email,
            beforeSummary,
            BuildPlaceOwnerApprovalSummary(AdminApprovalCatalog.Rejected, normalizedReason));

        var saved = GetUserById(connection, transaction, existing.Id)
            ?? throw new InvalidOperationException("Không thể đọc lại hồ sơ đăng ký sau khi từ chối.");

        transaction.Commit();
        return saved;
    }

    private static bool CanUserStartAdminSession(AdminUser? user)
    {
        if (user is null ||
            !AdminRoleCatalog.IsAdminRole(user.Role) ||
            !string.Equals(user.Status, "active", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return !AdminRoleCatalog.IsPlaceOwner(user.Role) ||
               AdminApprovalCatalog.IsApproved(user.ApprovalStatus);
    }

    private static void EnsureUserCanStartSession(AdminUser user)
    {
        if (!AdminRoleCatalog.IsAdminRole(user.Role))
        {
            throw new ApiUnauthorizedException("Thông tin đăng nhập không hợp lệ.");
        }

        if (AdminRoleCatalog.IsPlaceOwner(user.Role))
        {
            if (AdminApprovalCatalog.IsPending(user.ApprovalStatus))
            {
                throw new ApiUnauthorizedException("Tài khoản của bạn đang chờ admin phê duyệt.");
            }

            if (AdminApprovalCatalog.IsRejected(user.ApprovalStatus))
            {
                var message = string.IsNullOrWhiteSpace(user.RejectionReason)
                    ? "Tài khoản của bạn đã bị từ chối."
                    : $"Tài khoản của bạn đã bị từ chối. Lý do: {user.RejectionReason}";
                throw new ApiUnauthorizedException(message);
            }
        }

        if (!string.Equals(user.Status, "active", StringComparison.OrdinalIgnoreCase))
        {
            throw new ApiUnauthorizedException("Tài khoản của bạn hiện đang bị khóa.");
        }
    }

    private AdminUser? GetUserByCredentialsIgnoringStatus(
        SqlConnection connection,
        SqlTransaction? transaction,
        string email,
        string password)
    {
        const string sql = """
            SELECT TOP 1 Id, Name, Email, Phone, Role, [Password], [Status], CreatedAt, LastLoginAt, AvatarColor, ManagedPoiId,
                   ApprovalStatus, RejectionReason, RegistrationSubmittedAt, RegistrationReviewedAt
            FROM dbo.AdminUsers
            WHERE LOWER(Email) = LOWER(?) AND [Password] = ?
            ORDER BY CreatedAt DESC;
            """;

        using var command = CreateCommand(connection, transaction, sql, email, password);
        using var reader = command.ExecuteReader();
        return reader.Read() ? MapAdminUser(reader) : null;
    }

    private static string ResolveApprovalStatusForAdminUserSave(AdminUser? existing, string nextRole)
    {
        if (!AdminRoleCatalog.IsPlaceOwner(nextRole))
        {
            return AdminApprovalCatalog.Approved;
        }

        if (existing is null)
        {
            return AdminApprovalCatalog.Approved;
        }

        return AdminApprovalCatalog.NormalizeKnownOrDefault(existing.ApprovalStatus);
    }

    private static void EnsurePlaceOwnerRegistrationExists(AdminUser? user)
    {
        if (user is null || !AdminRoleCatalog.IsPlaceOwner(user.Role))
        {
            throw new ApiNotFoundException("Không tìm thấy hồ sơ đăng ký chủ quán.");
        }
    }

    private static void EnsureRegistrationPasswordsMatch(string? password, string? confirmPassword)
    {
        if (!string.Equals(password?.Trim(), confirmPassword?.Trim(), StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Mật khẩu và xác nhận mật khẩu phải khớp nhau.");
        }
    }

    private IReadOnlyList<AdminIdentityRow> GetAdminIdentityRowsForRegistration(
        SqlConnection connection,
        SqlTransaction transaction)
    {
        const string sql = """
            SELECT Id, Email, Role, ApprovalStatus, CreatedAt
            FROM dbo.AdminUsers WITH (UPDLOCK, HOLDLOCK)
            ORDER BY CreatedAt DESC, Id DESC;
            """;

        using var command = CreateCommand(connection, transaction, sql);
        using var reader = command.ExecuteReader();

        var items = new List<AdminIdentityRow>();
        while (reader.Read())
        {
            items.Add(new AdminIdentityRow
            {
                Id = ReadString(reader, "Id"),
                Email = ReadString(reader, "Email"),
                Role = ReadString(reader, "Role"),
                ApprovalStatus = ReadNullableString(reader, "ApprovalStatus"),
                CreatedAt = ReadDateTimeOffset(reader, "CreatedAt")
            });
        }

        return items;
    }

    private static void EnsureAdminEmailIsAvailable(
        IReadOnlyList<AdminIdentityRow> existingRows,
        string normalizedEmail,
        string? excludedUserId = null)
    {
        var existing = existingRows.FirstOrDefault(row =>
            !string.Equals(row.Id, excludedUserId, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(row.Email, normalizedEmail, StringComparison.OrdinalIgnoreCase));

        if (existing is null)
        {
            return;
        }

        if (AdminRoleCatalog.IsPlaceOwner(existing.Role))
        {
            var approvalStatus = AdminApprovalCatalog.NormalizeKnownOrDefault(existing.ApprovalStatus);
            if (AdminApprovalCatalog.IsPending(approvalStatus))
            {
                throw new InvalidOperationException("Email này đã có hồ sơ chờ duyệt.");
            }

            if (AdminApprovalCatalog.IsRejected(approvalStatus))
            {
                throw new InvalidOperationException("Email này đã tồn tại trong hồ sơ bị từ chối. Hãy mở lại hồ sơ để chỉnh sửa và gửi lại.");
            }
        }

        throw new InvalidOperationException("Email này đã được sử dụng.");
    }

    private static string NormalizePlaceOwnerRegistrationValue(string? value, string fieldName, int maxLength)
    {
        var normalized = value?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(normalized))
        {
            throw new InvalidOperationException($"{fieldName} là bắt buộc.");
        }

        if (normalized.Length > maxLength)
        {
            throw new InvalidOperationException($"{fieldName} không được vượt quá {maxLength} ký tự.");
        }

        return normalized;
    }

    private static string NormalizePlaceOwnerRegistrationEmail(string? value)
    {
        var normalized = NormalizePlaceOwnerRegistrationValue(value, "Email", 200).ToLowerInvariant();

        try
        {
            var address = new MailAddress(normalized);
            if (!string.Equals(address.Address, normalized, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("Email không hợp lệ.");
            }
        }
        catch (FormatException)
        {
            throw new InvalidOperationException("Email không hợp lệ.");
        }

        return normalized;
    }

    private static string NormalizePlaceOwnerRegistrationPhone(string? value)
    {
        var normalized = NormalizePlaceOwnerRegistrationValue(value, "Số điện thoại", 30);
        var digits = string.Concat(normalized.Where(char.IsDigit));
        if (digits.Length < 8 || digits.Length > 15)
        {
            throw new InvalidOperationException("Số điện thoại không hợp lệ.");
        }

        return normalized;
    }

    private static string NormalizePlaceOwnerRegistrationPassword(string? value)
    {
        var normalized = NormalizePlaceOwnerRegistrationValue(value, "Mật khẩu", 200);
        if (normalized.Length < 6)
        {
            throw new InvalidOperationException("Mật khẩu phải có ít nhất 6 ký tự.");
        }

        return normalized;
    }

    private static string BuildPlaceOwnerApprovalSummary(string approvalStatus, string? rejectionReason)
    {
        var normalizedStatus = AdminApprovalCatalog.NormalizeKnownOrDefault(approvalStatus);
        return normalizedStatus == AdminApprovalCatalog.Rejected && !string.IsNullOrWhiteSpace(rejectionReason)
            ? $"approvalStatus={normalizedStatus}; rejectionReason={rejectionReason}"
            : $"approvalStatus={normalizedStatus}";
    }

    private static string BuildPlaceOwnerProfileSummary(AdminUser user)
        => BuildPlaceOwnerProfileSummary(user.Name, user.Email, user.Phone);

    private static string BuildPlaceOwnerProfileSummary(string name, string email, string phone)
        => $"name={name}; email={email}; phone={phone}";

    private sealed class AdminIdentityRow
    {
        public string Id { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public string Role { get; set; } = string.Empty;
        public string? ApprovalStatus { get; set; }
        public DateTimeOffset CreatedAt { get; set; }
    }
}
