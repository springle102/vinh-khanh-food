using Microsoft.Data.SqlClient;
using VinhKhanh.BackendApi.Contracts;
using VinhKhanh.BackendApi.Models;

namespace VinhKhanh.BackendApi.Infrastructure;

public sealed partial class AdminDataRepository
{
    private const int AppPresenceOnlineTimeoutSeconds = 90;
    private static readonly TimeSpan AppPresenceRetention = TimeSpan.FromDays(7);

    private void EnsureAppPresenceSchema(SqlConnection connection)
    {
        ExecuteNonQuery(
            connection,
            null,
            """
            IF OBJECT_ID(N'dbo.AppPresence', N'U') IS NULL
            BEGIN
                CREATE TABLE dbo.AppPresence (
                    Id NVARCHAR(60) NOT NULL PRIMARY KEY,
                    ClientId NVARCHAR(80) NOT NULL,
                    LastSeenAtUtc DATETIMEOFFSET(7) NOT NULL,
                    CreatedAtUtc DATETIMEOFFSET(7) NOT NULL,
                    Platform NVARCHAR(20) NOT NULL,
                    AppVersion NVARCHAR(40) NOT NULL
                );
            END;

            IF COL_LENGTH(N'dbo.AppPresence', N'AppVersion') IS NULL
                ALTER TABLE dbo.AppPresence ADD AppVersion NVARCHAR(40) NOT NULL CONSTRAINT DF_AppPresence_AppVersion DEFAULT N'';

            IF NOT EXISTS (
                SELECT 1
                FROM sys.indexes
                WHERE object_id = OBJECT_ID(N'dbo.AppPresence')
                  AND name = N'UX_AppPresence_ClientId'
            )
            BEGIN
                CREATE UNIQUE INDEX UX_AppPresence_ClientId
                ON dbo.AppPresence (ClientId);
            END;

            IF NOT EXISTS (
                SELECT 1
                FROM sys.indexes
                WHERE object_id = OBJECT_ID(N'dbo.AppPresence')
                  AND name = N'IX_AppPresence_LastSeenAtUtc'
            )
            BEGIN
                CREATE INDEX IX_AppPresence_LastSeenAtUtc
                ON dbo.AppPresence (LastSeenAtUtc DESC);
            END;
            """);
    }

    public AppPresence UpsertAppPresence(AppPresenceHeartbeatRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var clientId = NormalizePresenceClientId(request.ClientId);
        var platform = NormalizePresencePlatform(request.Platform);
        var appVersion = NormalizePresenceAppVersion(request.AppVersion);
        var now = DateTimeOffset.UtcNow;

        using var connection = OpenConnection();
        PruneExpiredAppPresence(connection, null, now);

        ExecuteNonQuery(
            connection,
            null,
            """
            MERGE dbo.AppPresence WITH (HOLDLOCK) AS target
            USING (SELECT ? AS ClientId) AS source
            ON target.ClientId = source.ClientId
            WHEN MATCHED THEN
                UPDATE SET
                    LastSeenAtUtc = ?,
                    Platform = ?,
                    AppVersion = ?
            WHEN NOT MATCHED THEN
                INSERT (Id, ClientId, LastSeenAtUtc, CreatedAtUtc, Platform, AppVersion)
                VALUES (?, ?, ?, ?, ?, ?);
            """,
            clientId,
            now,
            platform,
            appVersion,
            CreateId("presence"),
            clientId,
            now,
            now,
            platform,
            appVersion);

        _logger.LogDebug(
            "[Presence] Heartbeat saved. clientId={ClientId}; platform={Platform}; appVersion={AppVersion}",
            clientId,
            platform,
            appVersion);

        return GetAppPresenceByClientId(connection, null, clientId)
               ?? throw new InvalidOperationException("Presence heartbeat was not persisted.");
    }

    public AppPresence MarkAppPresenceOffline(AppPresenceHeartbeatRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var clientId = NormalizePresenceClientId(request.ClientId);
        var platform = NormalizePresencePlatform(request.Platform);
        var appVersion = NormalizePresenceAppVersion(request.AppVersion);
        var now = DateTimeOffset.UtcNow;
        var offlineAt = now.AddSeconds(-(AppPresenceOnlineTimeoutSeconds + 1));

        using var connection = OpenConnection();

        ExecuteNonQuery(
            connection,
            null,
            """
            MERGE dbo.AppPresence WITH (HOLDLOCK) AS target
            USING (SELECT ? AS ClientId) AS source
            ON target.ClientId = source.ClientId
            WHEN MATCHED THEN
                UPDATE SET
                    LastSeenAtUtc = ?,
                    Platform = ?,
                    AppVersion = ?
            WHEN NOT MATCHED THEN
                INSERT (Id, ClientId, LastSeenAtUtc, CreatedAtUtc, Platform, AppVersion)
                VALUES (?, ?, ?, ?, ?, ?);
            """,
            clientId,
            offlineAt,
            platform,
            appVersion,
            CreateId("presence"),
            clientId,
            offlineAt,
            now,
            platform,
            appVersion);

        _logger.LogInformation(
            "[Presence] Client marked offline. clientId={ClientId}; platform={Platform}; appVersion={AppVersion}",
            clientId,
            platform,
            appVersion);

        return GetAppPresenceByClientId(connection, null, clientId)
               ?? throw new InvalidOperationException("Presence offline state was not persisted.");
    }

    public OnlineUsersResponse GetOnlineUsers()
    {
        using var connection = OpenConnection();
        var checkedAtUtc = DateTimeOffset.UtcNow;
        return new OnlineUsersResponse(
            GetOnlineUsersCount(connection, null, checkedAtUtc),
            checkedAtUtc,
            AppPresenceOnlineTimeoutSeconds);
    }

    private int GetOnlineUsersCount(
        SqlConnection connection,
        SqlTransaction? transaction,
        DateTimeOffset checkedAtUtc)
    {
        if (!TableExists(connection, transaction, "AppPresence"))
        {
            return 0;
        }

        const string sql = """
            SELECT COUNT(*)
            FROM dbo.AppPresence
            WHERE LastSeenAtUtc >= ?;
            """;

        var cutoff = checkedAtUtc.AddSeconds(-AppPresenceOnlineTimeoutSeconds);
        return ExecuteScalarInt(connection, transaction, sql, cutoff);
    }

    private void PruneExpiredAppPresence(
        SqlConnection connection,
        SqlTransaction? transaction,
        DateTimeOffset now)
    {
        ExecuteNonQuery(
            connection,
            transaction,
            """
            DELETE FROM dbo.AppPresence
            WHERE LastSeenAtUtc < ?;
            """,
            now - AppPresenceRetention);
    }

    private static AppPresence? GetAppPresenceByClientId(
        SqlConnection connection,
        SqlTransaction? transaction,
        string clientId)
    {
        const string sql = """
            SELECT TOP 1 Id, ClientId, LastSeenAtUtc, CreatedAtUtc, Platform, AppVersion
            FROM dbo.AppPresence
            WHERE ClientId = ?;
            """;

        using var command = CreateCommand(connection, transaction, sql, clientId);
        using var reader = command.ExecuteReader();
        return reader.Read() ? MapAppPresence(reader) : null;
    }

    private static AppPresence MapAppPresence(SqlDataReader reader)
        => new()
        {
            Id = ReadString(reader, "Id"),
            ClientId = ReadString(reader, "ClientId"),
            LastSeenAtUtc = ReadDateTimeOffset(reader, "LastSeenAtUtc"),
            CreatedAtUtc = ReadDateTimeOffset(reader, "CreatedAtUtc"),
            Platform = ReadString(reader, "Platform"),
            AppVersion = ReadString(reader, "AppVersion")
        };

    private static string NormalizePresenceClientId(string? clientId)
    {
        var normalized = clientId?.Trim();
        if (string.IsNullOrWhiteSpace(normalized))
        {
            throw new ArgumentException("ClientId is required.", nameof(clientId));
        }

        return normalized.Length <= 80 ? normalized : normalized[..80];
    }

    private static string NormalizePresencePlatform(string? platform)
    {
        var normalized = platform?.Trim().ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return "android";
        }

        return normalized.Length <= 20 ? normalized : normalized[..20];
    }

    private static string NormalizePresenceAppVersion(string? appVersion)
    {
        var normalized = appVersion?.Trim() ?? string.Empty;
        return normalized.Length <= 40 ? normalized : normalized[..40];
    }
}
