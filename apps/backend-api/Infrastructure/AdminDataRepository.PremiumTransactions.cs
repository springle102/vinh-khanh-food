using Microsoft.Data.SqlClient;
using VinhKhanh.BackendApi.Models;

namespace VinhKhanh.BackendApi.Infrastructure;

public sealed partial class AdminDataRepository
{
    public PremiumPurchaseTransaction? GetPremiumPurchaseTransactionByIdempotencyKey(
        string customerUserId,
        string idempotencyKey)
    {
        using var connection = OpenConnection();
        return GetPremiumPurchaseTransactionByIdempotencyKey(connection, null, customerUserId, idempotencyKey);
    }

    public PremiumPurchaseTransaction? GetLatestPendingPremiumPurchase(string customerUserId)
    {
        using var connection = OpenConnection();
        return GetLatestPendingPremiumPurchase(connection, null, customerUserId);
    }

    public PremiumPurchaseTransaction CreatePendingPremiumPurchase(PremiumPurchaseTransaction transaction)
    {
        using var connection = OpenConnection();
        using var dbTransaction = connection.BeginTransaction();

        ExecuteNonQuery(
            connection,
            dbTransaction,
            """
            INSERT INTO dbo.PremiumPurchaseTransactions (
                Id, CustomerUserId, AmountUsd, CurrencyCode, PaymentProvider, PaymentMethod,
                PaymentReference, MaskedAccount, IdempotencyKey, [Status], FailureMessage, CreatedAt, ProcessedAt
            )
            VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?);
            """,
            transaction.Id,
            transaction.CustomerUserId,
            transaction.AmountUsd,
            transaction.CurrencyCode,
            transaction.PaymentProvider,
            transaction.PaymentMethod,
            transaction.PaymentReference,
            transaction.MaskedAccount,
            transaction.IdempotencyKey,
            transaction.Status,
            transaction.FailureMessage,
            transaction.CreatedAt,
            transaction.ProcessedAt);

        var saved = GetPremiumPurchaseTransactionById(connection, dbTransaction, transaction.Id)
            ?? throw new InvalidOperationException("Khong the tao giao dich Premium dang cho.");

        dbTransaction.Commit();
        return saved;
    }

    public PremiumPurchaseTransaction MarkPremiumPurchaseFailed(
        string transactionId,
        string failureMessage,
        DateTimeOffset processedAt)
    {
        using var connection = OpenConnection();
        using var dbTransaction = connection.BeginTransaction();

        var existing = GetPremiumPurchaseTransactionById(connection, dbTransaction, transactionId)
            ?? throw new InvalidOperationException("Khong tim thay giao dich Premium de cap nhat.");

        if (string.Equals(existing.Status, "succeeded", StringComparison.OrdinalIgnoreCase))
        {
            dbTransaction.Commit();
            return existing;
        }

        ExecuteNonQuery(
            connection,
            dbTransaction,
            """
            UPDATE dbo.PremiumPurchaseTransactions
            SET [Status] = ?,
                FailureMessage = ?,
                ProcessedAt = ?
            WHERE Id = ?;
            """,
            "failed",
            failureMessage,
            processedAt,
            transactionId);

        var saved = GetPremiumPurchaseTransactionById(connection, dbTransaction, transactionId)
            ?? throw new InvalidOperationException("Khong the cap nhat giao dich Premium that bai.");

        dbTransaction.Commit();
        return saved;
    }

    public (PremiumPurchaseTransaction Transaction, CustomerUser Customer) CompletePremiumPurchase(
        string transactionId,
        string customerUserId,
        string actorName,
        string actorRole,
        string paymentReference,
        DateTimeOffset processedAt)
    {
        using var connection = OpenConnection();
        using var dbTransaction = connection.BeginTransaction();

        var transactionRecord = GetPremiumPurchaseTransactionById(connection, dbTransaction, transactionId)
            ?? throw new InvalidOperationException("Khong tim thay giao dich Premium de hoan tat.");
        var customer = GetCustomerUserById(connection, dbTransaction, customerUserId)
            ?? throw new InvalidOperationException("Khong tim thay khach hang de kich hoat Premium.");

        if (!string.Equals(transactionRecord.Status, "succeeded", StringComparison.OrdinalIgnoreCase))
        {
            ExecuteNonQuery(
                connection,
                dbTransaction,
                """
                UPDATE dbo.PremiumPurchaseTransactions
                SET [Status] = ?,
                    PaymentReference = ?,
                    FailureMessage = NULL,
                    ProcessedAt = ?
                WHERE Id = ?;
                """,
                "succeeded",
                paymentReference,
                processedAt,
                transactionId);
        }

        if (!customer.IsPremium)
        {
            ExecuteNonQuery(
                connection,
                dbTransaction,
                """
                UPDATE dbo.CustomerUsers
                SET IsPremium = ?
                WHERE Id = ?;
                """,
                true,
                customerUserId);

            AppendAuditLog(
                connection,
                dbTransaction,
                actorName,
                actorRole,
                "Kich hoat goi Premium",
                customerUserId);
        }

        var savedTransaction = GetPremiumPurchaseTransactionById(connection, dbTransaction, transactionId)
            ?? throw new InvalidOperationException("Khong the doc lai giao dich Premium sau khi hoan tat.");
        var savedCustomer = GetCustomerUserById(connection, dbTransaction, customerUserId)
            ?? throw new InvalidOperationException("Khong the doc lai khach hang sau khi kich hoat Premium.");

        dbTransaction.Commit();
        return (savedTransaction, savedCustomer);
    }

    private PremiumPurchaseTransaction? GetLatestPendingPremiumPurchase(
        SqlConnection connection,
        SqlTransaction? transaction,
        string customerUserId)
    {
        const string sql = """
            SELECT TOP 1 Id, CustomerUserId, AmountUsd, CurrencyCode, PaymentProvider, PaymentMethod,
                   PaymentReference, MaskedAccount, IdempotencyKey, [Status], FailureMessage, CreatedAt, ProcessedAt
            FROM dbo.PremiumPurchaseTransactions
            WHERE CustomerUserId = ? AND [Status] = ?
            ORDER BY CreatedAt DESC, Id DESC;
            """;

        using var command = CreateCommand(connection, transaction, sql, customerUserId, "pending");
        using var reader = command.ExecuteReader();
        return reader.Read() ? MapPremiumPurchaseTransaction(reader) : null;
    }

    private PremiumPurchaseTransaction? GetPremiumPurchaseTransactionByIdempotencyKey(
        SqlConnection connection,
        SqlTransaction? transaction,
        string customerUserId,
        string idempotencyKey)
    {
        const string sql = """
            SELECT TOP 1 Id, CustomerUserId, AmountUsd, CurrencyCode, PaymentProvider, PaymentMethod,
                   PaymentReference, MaskedAccount, IdempotencyKey, [Status], FailureMessage, CreatedAt, ProcessedAt
            FROM dbo.PremiumPurchaseTransactions
            WHERE CustomerUserId = ? AND IdempotencyKey = ?
            ORDER BY CreatedAt DESC, Id DESC;
            """;

        using var command = CreateCommand(connection, transaction, sql, customerUserId, idempotencyKey);
        using var reader = command.ExecuteReader();
        return reader.Read() ? MapPremiumPurchaseTransaction(reader) : null;
    }

    private PremiumPurchaseTransaction? GetPremiumPurchaseTransactionById(
        SqlConnection connection,
        SqlTransaction? transaction,
        string id)
    {
        const string sql = """
            SELECT TOP 1 Id, CustomerUserId, AmountUsd, CurrencyCode, PaymentProvider, PaymentMethod,
                   PaymentReference, MaskedAccount, IdempotencyKey, [Status], FailureMessage, CreatedAt, ProcessedAt
            FROM dbo.PremiumPurchaseTransactions
            WHERE Id = ?;
            """;

        using var command = CreateCommand(connection, transaction, sql, id);
        using var reader = command.ExecuteReader();
        return reader.Read() ? MapPremiumPurchaseTransaction(reader) : null;
    }

    private static PremiumPurchaseTransaction MapPremiumPurchaseTransaction(SqlDataReader reader)
    {
        return new PremiumPurchaseTransaction
        {
            Id = ReadString(reader, "Id"),
            CustomerUserId = ReadString(reader, "CustomerUserId"),
            AmountUsd = ReadInt(reader, "AmountUsd"),
            CurrencyCode = ReadString(reader, "CurrencyCode"),
            PaymentProvider = ReadString(reader, "PaymentProvider"),
            PaymentMethod = ReadString(reader, "PaymentMethod"),
            PaymentReference = ReadNullableString(reader, "PaymentReference"),
            MaskedAccount = ReadNullableString(reader, "MaskedAccount"),
            IdempotencyKey = ReadString(reader, "IdempotencyKey"),
            Status = ReadString(reader, "Status"),
            FailureMessage = ReadNullableString(reader, "FailureMessage"),
            CreatedAt = ReadDateTimeOffset(reader, "CreatedAt"),
            ProcessedAt = ReadNullableDateTimeOffset(reader, "ProcessedAt")
        };
    }
}
