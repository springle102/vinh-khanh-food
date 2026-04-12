using Microsoft.Data.SqlClient;
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
                    OccurredAt DATETIMEOFFSET(7) NOT NULL
                );
            END;

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
            """);
    }

    public AppUsageEvent TrackAppUsageEvent(AppUsageEventCreateRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        using var connection = OpenConnection();
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
            OccurredAt = request.OccurredAt ?? DateTimeOffset.UtcNow
        };

        ExecuteNonQuery(
            connection,
            null,
            """
            INSERT INTO dbo.AppUsageEvents (
                Id, EventType, PoiId, LanguageCode, Platform, SessionId, Source, Metadata, DurationInSeconds, OccurredAt
            )
            VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?);
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
            usageEvent.OccurredAt);

        return usageEvent;
    }

    private IReadOnlyList<AppUsageEvent> GetAppUsageEvents(SqlConnection connection, SqlTransaction? transaction)
    {
        if (!TableExists(connection, transaction, "AppUsageEvents"))
        {
            return BuildLegacyUsageEvents(connection, transaction);
        }

        const string sql = """
            SELECT Id, EventType, PoiId, LanguageCode, Platform, SessionId, Source, Metadata, DurationInSeconds, OccurredAt
            FROM dbo.AppUsageEvents
            ORDER BY OccurredAt DESC, Id DESC;
            """;

        using var command = CreateCommand(connection, transaction, sql);
        using var reader = command.ExecuteReader();

        var items = new List<AppUsageEvent>();
        while (reader.Read())
        {
            items.Add(new AppUsageEvent
            {
                Id = ReadString(reader, "Id"),
                EventType = ReadString(reader, "EventType"),
                PoiId = ReadNullableString(reader, "PoiId"),
                LanguageCode = PremiumAccessCatalog.NormalizeLanguageCode(ReadString(reader, "LanguageCode")),
                Platform = ReadString(reader, "Platform"),
                SessionId = ReadString(reader, "SessionId"),
                Source = ReadString(reader, "Source"),
                Metadata = ReadString(reader, "Metadata"),
                DurationInSeconds = ReadNullableInt(reader, "DurationInSeconds"),
                OccurredAt = ReadDateTimeOffset(reader, "OccurredAt")
            });
        }

        if (items.Count > 0)
        {
            return items;
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
        return eventType?.Trim().ToLowerInvariant() switch
        {
            "poi_view" => "poi_view",
            "audio_play" => "audio_play",
            "qr_scan" => "qr_scan",
            _ => throw new ArgumentException("Unsupported app usage event type.", nameof(eventType))
        };
    }

    private static string NormalizeUsagePlatform(string? platform)
    {
        return platform?.Trim().ToLowerInvariant() switch
        {
            "web" => "web",
            "ios" => "ios",
            _ => "android"
        };
    }
}
