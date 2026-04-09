using System.Data;
using System.Net.Mail;
using Microsoft.Data.SqlClient;
using VinhKhanh.BackendApi.Contracts;
using VinhKhanh.BackendApi.Models;

namespace VinhKhanh.BackendApi.Infrastructure;

public sealed partial class AdminDataRepository
{
    private const string CustomerIdPrefix = "customer-";

    public CustomerUser CreateCustomerUser(CustomerRegistrationRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        using var connection = OpenConnection();
        using var transaction = connection.BeginTransaction(IsolationLevel.Serializable);

        var normalizedName = NormalizeCustomerProfileValue(request.Name, "Ten", 120);
        var normalizedUsername = NormalizeCustomerUsername(request.Username);
        var normalizedEmail = NormalizeCustomerRegistrationEmail(request.Email);
        var normalizedPhone = NormalizeCustomerRegistrationPhone(request.Phone);
        var normalizedPassword = NormalizeCustomerRegistrationPassword(request.Password);
        var normalizedPreferredLanguage = NormalizeCustomerPreferredLanguage(request.PreferredLanguage);
        var normalizedCountry = NormalizeCustomerCountry(request.Country);
        var now = DateTimeOffset.UtcNow;

        var existingRows = GetCustomerIdentityRowsForRegistration(connection, transaction);
        EnsureCustomerIdentityIsUnique(existingRows, normalizedEmail, normalizedPhone, normalizedUsername);

        var customerId = GenerateNextCustomerUserId(existingRows);

        ExecuteNonQuery(
            connection,
            transaction,
            """
            INSERT INTO dbo.CustomerUsers (
                Id,
                Name,
                Email,
                Phone,
                [Password],
                PreferredLanguage,
                IsPremium,
                CreatedAt,
                LastActiveAt,
                Username,
                Country
            )
            VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?);
            """,
            customerId,
            normalizedName,
            normalizedEmail,
            normalizedPhone,
            normalizedPassword,
            normalizedPreferredLanguage,
            false,
            now,
            now,
            normalizedUsername,
            normalizedCountry);

        AppendAuditLog(
            connection,
            transaction,
            normalizedName,
            "CUSTOMER",
            "Dang ky tai khoan khach hang",
            customerId);

        var saved = GetCustomerUserById(connection, transaction, customerId)
            ?? throw new InvalidOperationException("Khong the doc lai khach hang sau khi dang ky.");

        transaction.Commit();
        return saved;
    }

    private IReadOnlyList<CustomerIdentityRow> GetCustomerIdentityRowsForRegistration(
        SqlConnection connection,
        SqlTransaction transaction)
    {
        const string sql = """
            SELECT Id, Email, Phone, Username, CreatedAt
            FROM dbo.CustomerUsers WITH (UPDLOCK, HOLDLOCK)
            ORDER BY CreatedAt DESC, Id DESC;
            """;

        using var command = CreateCommand(connection, transaction, sql);
        using var reader = command.ExecuteReader();

        var items = new List<CustomerIdentityRow>();
        while (reader.Read())
        {
            items.Add(new CustomerIdentityRow
            {
                Id = ReadString(reader, "Id"),
                Email = ReadString(reader, "Email"),
                Phone = ReadString(reader, "Phone"),
                Username = ReadNullableString(reader, "Username"),
                CreatedAt = ReadDateTimeOffset(reader, "CreatedAt")
            });
        }

        return items;
    }

    private static void EnsureCustomerIdentityIsUnique(
        IReadOnlyList<CustomerIdentityRow> existingRows,
        string normalizedEmail,
        string normalizedPhone,
        string normalizedUsername,
        string? excludeCustomerId = null)
    {
        if (existingRows.Any(row =>
                !string.Equals(row.Id, excludeCustomerId, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(row.Email, normalizedEmail, StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidOperationException("Email nay da duoc su dung.");
        }

        if (existingRows.Any(row =>
                !string.Equals(row.Id, excludeCustomerId, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(NormalizePhoneForComparison(row.Phone), normalizedPhone, StringComparison.Ordinal)))
        {
            throw new InvalidOperationException("So dien thoai nay da duoc su dung.");
        }

        if (existingRows.Any(row =>
                !string.Equals(row.Id, excludeCustomerId, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(row.Username, normalizedUsername, StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidOperationException("Username nay da duoc su dung.");
        }
    }

    private static string GenerateNextCustomerUserId(IReadOnlyList<CustomerIdentityRow> existingRows)
    {
        var maxSequence = existingRows
            .Select(row => TryParseCustomerSequence(row.Id, out var sequence) ? sequence : 0)
            .DefaultIfEmpty(0)
            .Max();

        var preferredNext = maxSequence + 1;
        var latestCreatedId = existingRows.FirstOrDefault()?.Id;
        if (TryParseCustomerSequence(latestCreatedId, out var latestSequence))
        {
            preferredNext = latestSequence + 1;
        }

        while (existingRows.Any(row =>
                   string.Equals(row.Id, $"{CustomerIdPrefix}{preferredNext}", StringComparison.OrdinalIgnoreCase)))
        {
            preferredNext++;
        }

        return $"{CustomerIdPrefix}{preferredNext}";
    }

    private static string NormalizeCustomerRegistrationEmail(string? value)
    {
        var normalized = NormalizeCustomerProfileValue(value, "Email", 200).ToLowerInvariant();

        try
        {
            var mailAddress = new MailAddress(normalized);
            if (!string.Equals(mailAddress.Address, normalized, StringComparison.OrdinalIgnoreCase))
            {
                throw new ArgumentException("Email khong hop le.");
            }
        }
        catch (FormatException)
        {
            throw new ArgumentException("Email khong hop le.");
        }

        return normalized;
    }

    private static string NormalizeCustomerRegistrationPhone(string? value)
    {
        var normalized = NormalizePhoneForComparison(NormalizeCustomerProfileValue(value, "So dien thoai", 30));
        if (normalized.Length < 8 || normalized.Length > 15)
        {
            throw new ArgumentException("So dien thoai khong hop le.");
        }

        return normalized;
    }

    private static string NormalizeCustomerRegistrationPassword(string? value)
    {
        var normalized = NormalizeCustomerProfileValue(value, "Mat khau", 200);
        if (normalized.Length < 6)
        {
            throw new ArgumentException("Mat khau phai co it nhat 6 ky tu.");
        }

        return normalized;
    }

    private static string NormalizeCustomerUsername(string? value)
    {
        var normalized = NormalizeCustomerProfileValue(value, "Username", 120).ToLowerInvariant();
        if (normalized.Length < 3)
        {
            throw new ArgumentException("Username phai co it nhat 3 ky tu.");
        }

        if (normalized.Any(character => !char.IsLetterOrDigit(character) && character is not '.' and not '_' and not '-'))
        {
            throw new ArgumentException("Username chi duoc chua chu cai, so, dau cham, gach ngang hoac gach duoi.");
        }

        return normalized;
    }

    private static string NormalizeCustomerPreferredLanguage(string? value)
    {
        var normalized = PremiumAccessCatalog.NormalizeLanguageCode(value);
        return string.IsNullOrWhiteSpace(normalized) ? "vi" : normalized;
    }

    private static string NormalizeCustomerCountry(string? value)
    {
        var normalized = string.IsNullOrWhiteSpace(value)
            ? "VN"
            : value.Trim().ToUpperInvariant();

        if (normalized.Length > 20)
        {
            throw new ArgumentException("Ma quoc gia khong duoc vuot qua 20 ky tu.");
        }

        return normalized;
    }

    private static string NormalizePhoneForComparison(string? value)
        => string.Concat((value ?? string.Empty).Where(char.IsDigit));

    private static bool TryParseCustomerSequence(string? customerId, out int sequence)
    {
        sequence = 0;
        if (string.IsNullOrWhiteSpace(customerId) ||
            !customerId.StartsWith(CustomerIdPrefix, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return int.TryParse(customerId[CustomerIdPrefix.Length..], out sequence) && sequence > 0;
    }

    private sealed class CustomerIdentityRow
    {
        public string Id { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public string Phone { get; set; } = string.Empty;
        public string? Username { get; set; }
        public DateTimeOffset CreatedAt { get; set; }
    }
}
