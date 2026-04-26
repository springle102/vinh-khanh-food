using Microsoft.Data.SqlClient;
using VinhKhanh.Core.Mobile;
using VinhKhanh.BackendApi.Contracts;
using VinhKhanh.BackendApi.Models;

namespace VinhKhanh.BackendApi.Infrastructure;

public sealed partial class AdminDataRepository
{
    private void EnsureAppUsageEventSchema(SqlConnection connection)
    {
        ExecuteNonQuery(
            connection,
            null,
            """
            IF OBJECT_ID(N'dbo.AppUsageEvents', N'U') IS NULL
            BEGIN
                CREATE TABLE dbo.AppUsageEvents (
                    Id NVARCHAR(60) NOT NULL PRIMARY KEY,
                    EventType NVARCHAR(40) NOT NULL,
                    PoiId NVARCHAR(50) NULL,
                    LanguageCode NVARCHAR(20) NOT NULL,
                    Platform NVARCHAR(20) NOT NULL,
                    SessionId NVARCHAR(80) NOT NULL,
                    Source NVARCHAR(60) NOT NULL,
                    Metadata NVARCHAR(MAX) NOT NULL,
                    DurationInSeconds INT NULL,
                    OccurredAt DATETIMEOFFSET(7) NOT NULL,
                    IdempotencyKey NVARCHAR(100) NULL
                );
            END;

            IF COL_LENGTH(N'dbo.AppUsageEvents', N'IdempotencyKey') IS NULL
                ALTER TABLE dbo.AppUsageEvents ADD IdempotencyKey NVARCHAR(100) NULL;

            IF NOT EXISTS (
                SELECT 1
                FROM sys.indexes
                WHERE object_id = OBJECT_ID(N'dbo.AppUsageEvents')
                  AND name = N'IX_AppUsageEvents_OccurredAt'
            )
            BEGIN
                CREATE INDEX IX_AppUsageEvents_OccurredAt
                ON dbo.AppUsageEvents (OccurredAt DESC, EventType, PoiId, LanguageCode);
            END;

            IF NOT EXISTS (
                SELECT 1
                FROM sys.indexes
                WHERE object_id = OBJECT_ID(N'dbo.AppUsageEvents')
                  AND name = N'UX_AppUsageEvents_IdempotencyKey'
            )
            BEGIN
                EXEC(N'CREATE UNIQUE INDEX UX_AppUsageEvents_IdempotencyKey
                      ON dbo.AppUsageEvents (IdempotencyKey)
                      WHERE IdempotencyKey IS NOT NULL AND IdempotencyKey <> N'''';');
            END;
            """);
    }

    public AppUsageEvent TrackAppUsageEvent(AppUsageEventCreateRequest request)
        => TrackAppUsageEventWithResult(request).Event;

    public AppUsageEventTrackResult TrackAppUsageEventWithResult(AppUsageEventCreateRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        using var connection = OpenConnection();
        var idempotencyKey = NormalizeIdempotencyKey(request.IdempotencyKey);
        if (!string.IsNullOrWhiteSpace(idempotencyKey))
        {
            var existing = GetAppUsageEventByIdempotencyKey(connection, null, idempotencyKey);
            if (existing is not null)
            {
                _logger.LogInformation(
                    "[Analytics] Reused existing app usage event by idempotency key. eventId={EventId}; type={EventType}; poiId={PoiId}; language={LanguageCode}; source={Source}; key={IdempotencyKey}",
                    existing.Id,
                    existing.EventType,
                    existing.PoiId ?? "none",
                    existing.LanguageCode,
                    existing.Source,
                    existing.IdempotencyKey ?? "none");
                return new AppUsageEventTrackResult(existing, WasCreated: false);
            }
        }

        var usageEvent = new AppUsageEvent
        {
            Id = CreateId("evt"),
            EventType = NormalizeUsageEventType(request.EventType),
            PoiId = string.IsNullOrWhiteSpace(request.PoiId) ? null : request.PoiId.Trim(),
            LanguageCode = PremiumAccessCatalog.NormalizeLanguageCode(request.LanguageCode),
            Platform = NormalizeUsagePlatform(request.Platform),
            SessionId = string.IsNullOrWhiteSpace(request.SessionId) ? Guid.NewGuid().ToString("N") : request.SessionId.Trim(),
            Source = string.IsNullOrWhiteSpace(request.Source) ? "mobile-app" : request.Source.Trim(),
            Metadata = string.IsNullOrWhiteSpace(request.Metadata) ? string.Empty : request.Metadata.Trim(),
            DurationInSeconds = request.DurationInSeconds > 0 ? request.DurationInSeconds : null,
            OccurredAt = request.OccurredAt ?? DateTimeOffset.UtcNow,
            IdempotencyKey = idempotencyKey
        };

        try
        {
            ExecuteNonQuery(
                connection,
                null,
                """
                INSERT INTO dbo.AppUsageEvents (
                    Id, EventType, PoiId, LanguageCode, Platform, SessionId, Source, Metadata, DurationInSeconds, OccurredAt, IdempotencyKey
                )
                VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?);
                """,
                usageEvent.Id,
                usageEvent.EventType,
                usageEvent.PoiId,
                usageEvent.LanguageCode,
                usageEvent.Platform,
                usageEvent.SessionId,
                usageEvent.Source,
                usageEvent.Metadata,
                usageEvent.DurationInSeconds,
                usageEvent.OccurredAt,
                usageEvent.IdempotencyKey);
        }
        catch (SqlException exception) when (
            IsUniqueConstraintViolation(exception) &&
            !string.IsNullOrWhiteSpace(idempotencyKey))
        {
            var existing = GetAppUsageEventByIdempotencyKey(connection, null, idempotencyKey);
            if (existing is not null)
            {
                _logger.LogDebug(
                    exception,
                    "[Analytics] Reused existing app usage event after unique key race. eventId={EventId}; type={EventType}; key={IdempotencyKey}",
                    existing.Id,
                    existing.EventType,
                    existing.IdempotencyKey ?? "none");
                return new AppUsageEventTrackResult(existing, WasCreated: false);
            }

            throw;
        }

        _logger.LogInformation(
            "[Analytics] Saved app usage event. eventId={EventId}; type={EventType}; poiId={PoiId}; language={LanguageCode}; platform={Platform}; source={Source}; key={IdempotencyKey}",
            usageEvent.Id,
            usageEvent.EventType,
            usageEvent.PoiId ?? "none",
            usageEvent.LanguageCode,
            usageEvent.Platform,
            usageEvent.Source,
            usageEvent.IdempotencyKey ?? "none");

        return new AppUsageEventTrackResult(usageEvent, WasCreated: true);
    }

    public AppUsageEvent TrackQrScan(string source, string? metadata = null, string? idempotencyKey = null)
        => TrackQrScanWithResult(source, metadata, idempotencyKey).Event;

    public AppUsageEventTrackResult TrackQrScanWithResult(string source, string? metadata = null, string? idempotencyKey = null)
        => TrackAppUsageEventWithResult(new AppUsageEventCreateRequest(
            MobileUsageEventTypes.QrScan,
            PoiId: null,
            LanguageCode: null,
            Platform: "web",
            SessionId: Guid.NewGuid().ToString("N"),
            Source: string.IsNullOrWhiteSpace(source) ? "public_download_apk" : source.Trim(),
            Metadata: metadata,
            DurationInSeconds: null,
            OccurredAt: DateTimeOffset.UtcNow,
            IdempotencyKey: idempotencyKey));

    public AppUsageEvent TrackApkDownloadAccess(string? metadata = null, string? idempotencyKey = null)
        => TrackApkDownloadAccessWithResult(metadata, idempotencyKey).Event;

    public AppUsageEventTrackResult TrackApkDownloadAccessWithResult(string? metadata = null, string? idempotencyKey = null)
        => TrackAppUsageEventWithResult(new AppUsageEventCreateRequest(
            MobileUsageEventTypes.ApkDownloadAccess,
            PoiId: null,
            LanguageCode: null,
            Platform: "web",
            SessionId: Guid.NewGuid().ToString("N"),
            Source: "public_download_apk",
            Metadata: metadata,
            DurationInSeconds: null,
            OccurredAt: DateTimeOffset.UtcNow,
            IdempotencyKey: idempotencyKey));

    public QrScanDiagnosticsResponse GetQrScanDiagnostics()
    {
        using var connection = OpenConnection();
        var databaseServer = connection.DataSource;
        var databaseName = connection.Database;
        if (!TableExists(connection, null, "AppUsageEvents"))
        {
            return new QrScanDiagnosticsResponse(
                QrScanCount: 0,
                PublicDownloadQrScanCount: 0,
                ApkDownloadAccessCount: 0,
                DashboardQrTotal: 0,
                LatestTrackedQrScanAt: null,
                DatabaseServer: databaseServer,
                DatabaseName: databaseName);
        }

        const string sql = """
            SELECT
                COALESCE(SUM(CASE WHEN LOWER(EventType) = N'qr_scan' THEN ? ELSE 0 END), 0) AS QrScanCount,
                COALESCE(SUM(CASE WHEN LOWER(EventType) = N'qr_scan'
                           AND LOWER(Source) = N'public_download_apk' THEN ? ELSE 0 END), 0) AS PublicDownloadQrScanCount,
                COALESCE(SUM(CASE WHEN LOWER(EventType) = N'apk_download_access' THEN ? ELSE 0 END), 0) AS ApkDownloadAccessCount,
                COALESCE(SUM(CASE WHEN LOWER(EventType) = N'apk_download_access'
                           OR LOWER(EventType) = N'qr_scan' THEN ? ELSE 0 END), 0) AS DashboardQrTotal,
                MAX(CASE WHEN LOWER(EventType) = N'apk_download_access'
                           OR LOWER(EventType) = N'qr_scan' THEN OccurredAt ELSE NULL END) AS LatestTrackedQrScanAt
            FROM dbo.AppUsageEvents;
            """;

        using var command = CreateCommand(
            connection,
            null,
            sql,
            AnalyticsMetricWeights.QrScan,
            AnalyticsMetricWeights.QrScan,
            AnalyticsMetricWeights.QrScan,
            AnalyticsMetricWeights.QrScan);
        using var reader = command.ExecuteReader();
        if (!reader.Read())
        {
            return new QrScanDiagnosticsResponse(0, 0, 0, 0, null, databaseServer, databaseName);
        }

        return new QrScanDiagnosticsResponse(
            QrScanCount: ReadInt(reader, "QrScanCount"),
            PublicDownloadQrScanCount: ReadInt(reader, "PublicDownloadQrScanCount"),
            ApkDownloadAccessCount: ReadInt(reader, "ApkDownloadAccessCount"),
            DashboardQrTotal: ReadInt(reader, "DashboardQrTotal"),
            LatestTrackedQrScanAt: ReadNullableDateTimeOffset(reader, "LatestTrackedQrScanAt"),
            DatabaseServer: databaseServer,
            DatabaseName: databaseName);
    }

    private IReadOnlyList<AppUsageEvent> GetAppUsageEvents(SqlConnection connection, SqlTransaction? transaction)
    {
        if (!TableExists(connection, transaction, "AppUsageEvents"))
        {
            return BuildLegacyUsageEvents(connection, transaction);
        }

        const string sql = """
            SELECT Id, EventType, PoiId, LanguageCode, Platform, SessionId, Source, Metadata, DurationInSeconds, OccurredAt, IdempotencyKey
            FROM dbo.AppUsageEvents
            ORDER BY OccurredAt DESC, Id DESC;
            """;

        var items = new List<AppUsageEvent>();
        using (var command = CreateCommand(connection, transaction, sql))
        using (var reader = command.ExecuteReader())
        {
            while (reader.Read())
            {
                items.Add(MapAppUsageEvent(reader));
            }
        }

        var hasModernPoiOrAudioUsage = items.Any(item =>
            string.Equals(item.EventType, MobileUsageEventTypes.PoiView, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(item.EventType, MobileUsageEventTypes.AudioPlay, StringComparison.OrdinalIgnoreCase));
        if (!hasModernPoiOrAudioUsage)
        {
            items.AddRange(BuildLegacyUsageEvents(connection, transaction));
        }

        if (items.Count > 0)
        {
            return items
                .OrderByDescending(item => item.OccurredAt)
                .ThenByDescending(item => item.Id, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        return BuildLegacyUsageEvents(connection, transaction);
    }

    private IReadOnlyList<AppUsageEvent> BuildLegacyUsageEvents(SqlConnection connection, SqlTransaction? transaction)
    {
        var items = new List<AppUsageEvent>();

        if (TableExists(connection, transaction, "ViewLogs"))
        {
            items.AddRange(GetViewLogs(connection, transaction).Select(log => new AppUsageEvent
            {
                Id = $"legacy-view-{log.Id}",
                EventType = "poi_view",
                PoiId = log.PoiId,
                LanguageCode = PremiumAccessCatalog.NormalizeLanguageCode(log.LanguageCode),
                Platform = NormalizeUsagePlatform(log.DeviceType),
                SessionId = string.Empty,
                Source = "legacy-view-log",
                Metadata = string.Empty,
                OccurredAt = log.ViewedAt
            }));
        }

        if (TableExists(connection, transaction, "AudioListenLogs"))
        {
            items.AddRange(GetAudioListenLogs(connection, transaction).Select(log => new AppUsageEvent
            {
                Id = $"legacy-audio-{log.Id}",
                EventType = "audio_play",
                PoiId = log.PoiId,
                LanguageCode = PremiumAccessCatalog.NormalizeLanguageCode(log.LanguageCode),
                Platform = "android",
                SessionId = string.Empty,
                Source = "legacy-audio-log",
                Metadata = string.Empty,
                DurationInSeconds = log.DurationInSeconds,
                OccurredAt = log.ListenedAt
            }));
        }

        return items
            .OrderByDescending(item => item.OccurredAt)
            .ThenByDescending(item => item.Id, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static string NormalizeUsageEventType(string? eventType)
    {
        var normalized = MobileUsageEventTypes.Normalize(eventType);
        if (string.IsNullOrWhiteSpace(normalized))
        {
            throw new ArgumentException("Unsupported app usage event type.", nameof(eventType));
        }

        return string.Equals(normalized, MobileUsageEventTypes.ApkDownloadAccess, StringComparison.OrdinalIgnoreCase)
            ? MobileUsageEventTypes.QrScan
            : normalized;
    }

    private static string NormalizeUsagePlatform(string? platform)
    {
        return platform?.Trim().ToLowerInvariant() switch
        {
            "web" => "web",
            _ => "android"
        };
    }

    private static string? NormalizeIdempotencyKey(string? idempotencyKey)
    {
        var value = idempotencyKey?.Trim();
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return value.Length <= 100 ? value : value[..100];
    }

    private static bool IsUniqueConstraintViolation(SqlException exception)
        => exception.Errors
            .Cast<SqlError>()
            .Any(error => error.Number is 2601 or 2627);

    private static AppUsageEvent MapAppUsageEvent(SqlDataReader reader)
        => new()
        {
            Id = ReadString(reader, "Id"),
            EventType = ReadString(reader, "EventType"),
            PoiId = ReadNullableString(reader, "PoiId"),
            LanguageCode = PremiumAccessCatalog.NormalizeLanguageCode(ReadString(reader, "LanguageCode")),
            Platform = NormalizeUsagePlatform(ReadString(reader, "Platform")),
            SessionId = ReadString(reader, "SessionId"),
            Source = ReadString(reader, "Source"),
            Metadata = ReadString(reader, "Metadata"),
            DurationInSeconds = ReadNullableInt(reader, "DurationInSeconds"),
            OccurredAt = ReadDateTimeOffset(reader, "OccurredAt"),
            IdempotencyKey = ReadNullableString(reader, "IdempotencyKey")
        };

    private AppUsageEvent? GetAppUsageEventByIdempotencyKey(
        SqlConnection connection,
        SqlTransaction? transaction,
        string idempotencyKey)
    {
        const string sql = """
            SELECT TOP 1 Id, EventType, PoiId, LanguageCode, Platform, SessionId, Source, Metadata,
                         DurationInSeconds, OccurredAt, IdempotencyKey
            FROM dbo.AppUsageEvents
            WHERE IdempotencyKey = ?;
            """;

        using var command = CreateCommand(connection, transaction, sql, idempotencyKey);
        using var reader = command.ExecuteReader();
        return reader.Read() ? MapAppUsageEvent(reader) : null;
    }
}

public sealed record AppUsageEventTrackResult(AppUsageEvent Event, bool WasCreated);
