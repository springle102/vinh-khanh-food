using System.Globalization;
using System.Text.Json;
using Android.Content;
using Android.Database.Sqlite;
using Microsoft.Extensions.Logging;
using Microsoft.Maui.Storage;
using VinhKhanh.Core.Mobile;
using VinhKhanh.MobileApp.Helpers;
using VinhKhanh.MobileApp.Models;

namespace VinhKhanh.MobileApp.Services;

public interface IMobileOfflineDatabaseService
{
    string DatabasePath { get; }
    Task EnsureInitializedAsync(CancellationToken cancellationToken = default);
}

public interface IMobileDatasetRepository
{
    Task<MobileBootstrapCache?> LoadBootstrapEnvelopeAsync(CancellationToken cancellationToken = default);
    Task SaveBootstrapEnvelopeAsync(
        string bootstrapEnvelopeJson,
        string installationSource,
        CancellationToken cancellationToken = default);
}

public interface IMobileSyncQueueRepository
{
    Task EnqueueUsageEventAsync(QueuedMobileUsageEvent usageEvent, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<QueuedMobileUsageEvent>> GetPendingUsageEventsAsync(
        int limit = MobileDatasetConstants.DefaultSyncBatchSize,
        CancellationToken cancellationToken = default);
    Task MarkUsageEventsSyncedAsync(
        IReadOnlyCollection<string> idempotencyKeys,
        CancellationToken cancellationToken = default);
    Task MarkUsageEventsFailedAsync(
        IReadOnlyDictionary<string, string> failures,
        CancellationToken cancellationToken = default);
}

public sealed record MobileBootstrapCache(
    string EnvelopeJson,
    string DatasetVersion,
    string InstallationSource,
    DateTimeOffset SavedAtUtc);

public sealed record QueuedMobileUsageEvent(
    string IdempotencyKey,
    string EventType,
    string? PoiId,
    string LanguageCode,
    string Platform,
    string SessionId,
    string Source,
    string? Metadata,
    int? DurationInSeconds,
    DateTimeOffset OccurredAt);

public sealed class MobileOfflineDatabaseService :
    IMobileOfflineDatabaseService,
    IMobileDatasetRepository,
    IMobileSyncQueueRepository
{
    private const string PackageSeedDatabasePath = "seed/offline-package/seed.db";
    private const string PackageSeedMetadataPath = "seed/offline-package/metadata.json";
    private const string DatabaseFolderName = "mobile-offline";
    private const string DatabaseFileName = "mobile.db";

    private readonly SemaphoreSlim _lock = new(1, 1);
    private readonly ILogger<MobileOfflineDatabaseService> _logger;
    private readonly JsonSerializerOptions _jsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

    private bool _initialized;

    public MobileOfflineDatabaseService(ILogger<MobileOfflineDatabaseService> logger)
    {
        _logger = logger;
        DatabasePath = Path.Combine(
            FileSystem.Current.AppDataDirectory,
            DatabaseFolderName,
            DatabaseFileName);
    }

    public string DatabasePath { get; }

    public async Task EnsureInitializedAsync(CancellationToken cancellationToken = default)
    {
        if (_initialized)
        {
            return;
        }

        await _lock.WaitAsync(cancellationToken);
        try
        {
            if (_initialized)
            {
                return;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(DatabasePath)!);
            var packagedVersion = await ReadPackagedSeedVersionAsync(cancellationToken);
            var shouldCopySeed = await ShouldInstallPackagedSeedAsync(packagedVersion, cancellationToken);
            var pendingQueue = shouldCopySeed
                ? await TryReadPendingQueueWithoutInitializationAsync(cancellationToken)
                : [];

            if (shouldCopySeed)
            {
                var copied = await TryCopyPackagedSeedDatabaseAsync(cancellationToken);
                if (!copied)
                {
                    _logger.LogWarning(
                        "[OfflineDb] No packaged seed.db found. Creating an empty local SQLite database at {DatabasePath}.",
                        DatabasePath);
                }
            }

            using var db = OpenDatabase();
            EnsureSchema(db);

            if (!string.IsNullOrWhiteSpace(packagedVersion))
            {
                PutMetadata(db, "dataset_version", packagedVersion);
            }

            PutMetadata(db, "database_path", DatabasePath);
            PutMetadata(db, "initialized_at_utc", DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture));
            if (shouldCopySeed)
            {
                PutMetadata(db, "installation_source", MobileDatasetConstants.SeedInstallationSource);
                RestorePendingQueue(db, pendingQueue);
            }

            _initialized = true;
            _logger.LogInformation(
                "[OfflineDb] Local mobile SQLite initialized. path={DatabasePath}; packagedVersion={PackagedVersion}; copiedSeed={CopiedSeed}; restoredQueue={QueueCount}",
                DatabasePath,
                packagedVersion ?? "none",
                shouldCopySeed,
                pendingQueue.Count);
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task<MobileBootstrapCache?> LoadBootstrapEnvelopeAsync(CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken);

        using var db = OpenDatabase();
        using var cursor = db.RawQuery(
            """
            SELECT envelope_json, dataset_version, installation_source, saved_at_utc
            FROM bootstrap_cache
            WHERE id = ?
            LIMIT 1;
            """,
            [MobileDatasetConstants.CurrentBootstrapCacheId]);

        if (!cursor.MoveToFirst())
        {
            return null;
        }

        var envelopeJson = GetCursorString(cursor, "envelope_json");
        if (string.IsNullOrWhiteSpace(envelopeJson))
        {
            return null;
        }

        return new MobileBootstrapCache(
            envelopeJson,
            GetCursorString(cursor, "dataset_version"),
            GetCursorString(cursor, "installation_source"),
            ParseDateTimeOffset(GetCursorString(cursor, "saved_at_utc")) ?? DateTimeOffset.UtcNow);
    }

    public async Task SaveBootstrapEnvelopeAsync(
        string bootstrapEnvelopeJson,
        string installationSource,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(bootstrapEnvelopeJson))
        {
            return;
        }

        await EnsureInitializedAsync(cancellationToken);
        var datasetVersion = TryReadDatasetVersion(bootstrapEnvelopeJson);
        var savedAt = DateTimeOffset.UtcNow;
        var normalizedInstallationSource = string.IsNullOrWhiteSpace(installationSource)
            ? MobileDatasetConstants.DownloadedInstallationSource
            : installationSource.Trim();

        using var db = OpenDatabase();
        EnsureSchema(db);

        var values = new ContentValues();
        values.Put("id", MobileDatasetConstants.CurrentBootstrapCacheId);
        values.Put("language_code", AppLanguage.DefaultLanguage);
        values.Put("envelope_json", bootstrapEnvelopeJson);
        values.Put("dataset_version", datasetVersion);
        values.Put("installation_source", normalizedInstallationSource);
        values.Put("saved_at_utc", savedAt.ToString("O", CultureInfo.InvariantCulture));

        db.InsertWithOnConflict("bootstrap_cache", null, values, Conflict.Replace);
        PutMetadata(db, "dataset_version", datasetVersion);
        PutMetadata(db, "installation_source", normalizedInstallationSource);

        _logger.LogInformation(
            "[OfflineDb] Bootstrap envelope saved to local SQLite. version={Version}; source={Source}; bytes={Bytes}",
            datasetVersion,
            normalizedInstallationSource,
            bootstrapEnvelopeJson.Length);
    }

    public async Task EnqueueUsageEventAsync(QueuedMobileUsageEvent usageEvent, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(usageEvent.IdempotencyKey) ||
            string.IsNullOrWhiteSpace(MobileUsageEventTypes.Normalize(usageEvent.EventType)))
        {
            return;
        }

        await EnsureInitializedAsync(cancellationToken);

        using var db = OpenDatabase();
        EnsureSchema(db);

        var values = new ContentValues();
        values.Put("idempotency_key", usageEvent.IdempotencyKey.Trim());
        values.Put("event_type", MobileUsageEventTypes.Normalize(usageEvent.EventType));
        PutNullable(values, "poi_id", usageEvent.PoiId);
        values.Put("language_code", AppLanguage.NormalizeCode(usageEvent.LanguageCode));
        values.Put("platform", string.IsNullOrWhiteSpace(usageEvent.Platform) ? "android" : usageEvent.Platform.Trim());
        values.Put("session_id", string.IsNullOrWhiteSpace(usageEvent.SessionId) ? Guid.NewGuid().ToString("N") : usageEvent.SessionId.Trim());
        values.Put("source", string.IsNullOrWhiteSpace(usageEvent.Source) ? "mobile_app" : usageEvent.Source.Trim());
        PutNullable(values, "metadata", usageEvent.Metadata);
        if (usageEvent.DurationInSeconds > 0)
        {
            values.Put("duration_in_seconds", usageEvent.DurationInSeconds.Value);
        }
        else
        {
            values.PutNull("duration_in_seconds");
        }

        values.Put("occurred_at_utc", usageEvent.OccurredAt.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture));
        values.Put("status", "pending");
        values.Put("retry_count", 0);
        values.PutNull("last_error");
        values.PutNull("last_attempt_at_utc");

        db.InsertWithOnConflict("sync_logs_queue", null, values, Conflict.Ignore);
        _logger.LogDebug(
            "[SyncQueue] Usage event queued. type={EventType}; poiId={PoiId}; key={IdempotencyKey}",
            usageEvent.EventType,
            usageEvent.PoiId ?? "none",
            usageEvent.IdempotencyKey);
    }

    public async Task<IReadOnlyList<QueuedMobileUsageEvent>> GetPendingUsageEventsAsync(
        int limit = MobileDatasetConstants.DefaultSyncBatchSize,
        CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken);

        using var db = OpenDatabase();
        EnsureSchema(db);

        var safeLimit = Math.Clamp(limit, 1, 200);
        using var cursor = db.RawQuery(
            $"""
            SELECT idempotency_key, event_type, poi_id, language_code, platform, session_id, source,
                   metadata, duration_in_seconds, occurred_at_utc
            FROM sync_logs_queue
            WHERE status = 'pending' OR status = 'failed'
            ORDER BY occurred_at_utc ASC
            LIMIT {safeLimit};
            """,
            []);

        var items = new List<QueuedMobileUsageEvent>();
        while (cursor.MoveToNext())
        {
            items.Add(new QueuedMobileUsageEvent(
                GetCursorString(cursor, "idempotency_key"),
                GetCursorString(cursor, "event_type"),
                GetCursorNullableString(cursor, "poi_id"),
                GetCursorString(cursor, "language_code"),
                GetCursorString(cursor, "platform"),
                GetCursorString(cursor, "session_id"),
                GetCursorString(cursor, "source"),
                GetCursorNullableString(cursor, "metadata"),
                GetCursorNullableInt(cursor, "duration_in_seconds"),
                ParseDateTimeOffset(GetCursorString(cursor, "occurred_at_utc")) ?? DateTimeOffset.UtcNow));
        }

        return items;
    }

    public async Task MarkUsageEventsSyncedAsync(
        IReadOnlyCollection<string> idempotencyKeys,
        CancellationToken cancellationToken = default)
    {
        if (idempotencyKeys.Count == 0)
        {
            return;
        }

        await EnsureInitializedAsync(cancellationToken);

        using var db = OpenDatabase();
        foreach (var key in idempotencyKeys.Where(value => !string.IsNullOrWhiteSpace(value)).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var values = new ContentValues();
            values.Put("status", "synced");
            values.PutNull("last_error");
            values.Put("last_attempt_at_utc", DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture));
            db.Update("sync_logs_queue", values, "idempotency_key = ?", [key.Trim()]);
        }
    }

    public async Task MarkUsageEventsFailedAsync(
        IReadOnlyDictionary<string, string> failures,
        CancellationToken cancellationToken = default)
    {
        if (failures.Count == 0)
        {
            return;
        }

        await EnsureInitializedAsync(cancellationToken);

        using var db = OpenDatabase();
        foreach (var failure in failures)
        {
            if (string.IsNullOrWhiteSpace(failure.Key))
            {
                continue;
            }

            var values = new ContentValues();
            values.Put("status", "failed");
            values.Put("last_error", string.IsNullOrWhiteSpace(failure.Value) ? "sync_failed" : failure.Value.Trim());
            values.Put("last_attempt_at_utc", DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture));
            db.Update("sync_logs_queue", values, "idempotency_key = ?", [failure.Key.Trim()]);
            db.ExecSQL(
                "UPDATE sync_logs_queue SET retry_count = retry_count + 1 WHERE idempotency_key = ?;",
                [new Java.Lang.String(failure.Key.Trim())]);
        }
    }

    private async Task<bool> ShouldInstallPackagedSeedAsync(string? packagedVersion, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!File.Exists(DatabasePath))
        {
            return true;
        }

        if (string.IsNullOrWhiteSpace(packagedVersion))
        {
            return false;
        }

        var existingVersion = TryReadMetadataValue("dataset_version");
        var existingSource = TryReadMetadataValue("installation_source");
        if (string.Equals(existingVersion, packagedVersion, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return string.IsNullOrWhiteSpace(existingSource) ||
               string.Equals(existingSource, MobileDatasetConstants.SeedInstallationSource, StringComparison.OrdinalIgnoreCase);
    }

    private async Task<string?> ReadPackagedSeedVersionAsync(CancellationToken cancellationToken)
    {
        try
        {
            await using var stream = await FileSystem.OpenAppPackageFileAsync(PackageSeedMetadataPath);
            var metadata = await JsonSerializer.DeserializeAsync<OfflinePackageMetadata>(
                stream,
                _jsonOptions,
                cancellationToken);
            return string.IsNullOrWhiteSpace(metadata?.Version) ? null : metadata.Version.Trim();
        }
        catch
        {
            return null;
        }
    }

    private async Task<bool> TryCopyPackagedSeedDatabaseAsync(CancellationToken cancellationToken)
    {
        try
        {
            var tempPath = Path.Combine(Path.GetDirectoryName(DatabasePath)!, $"seed-{Guid.NewGuid():N}.db");
            await using (var input = await FileSystem.OpenAppPackageFileAsync(PackageSeedDatabasePath))
            await using (var output = File.Create(tempPath))
            {
                await input.CopyToAsync(output, cancellationToken);
            }

            if (File.Exists(DatabasePath))
            {
                File.Delete(DatabasePath);
            }

            File.Move(tempPath, DatabasePath);
            return true;
        }
        catch (FileNotFoundException)
        {
            return false;
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "[OfflineDb] Failed to copy packaged seed database.");
            return false;
        }
    }

    private async Task<IReadOnlyList<QueuedMobileUsageEvent>> TryReadPendingQueueWithoutInitializationAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(DatabasePath))
        {
            return [];
        }

        try
        {
            using var db = OpenDatabase();
            EnsureSchema(db);
            var items = new List<QueuedMobileUsageEvent>();
            using var cursor = db.RawQuery(
                """
                SELECT idempotency_key, event_type, poi_id, language_code, platform, session_id, source,
                       metadata, duration_in_seconds, occurred_at_utc
                FROM sync_logs_queue
                WHERE status = 'pending' OR status = 'failed'
                ORDER BY occurred_at_utc ASC;
                """,
                []);

            while (cursor.MoveToNext())
            {
                cancellationToken.ThrowIfCancellationRequested();
                items.Add(new QueuedMobileUsageEvent(
                    GetCursorString(cursor, "idempotency_key"),
                    GetCursorString(cursor, "event_type"),
                    GetCursorNullableString(cursor, "poi_id"),
                    GetCursorString(cursor, "language_code"),
                    GetCursorString(cursor, "platform"),
                    GetCursorString(cursor, "session_id"),
                    GetCursorString(cursor, "source"),
                    GetCursorNullableString(cursor, "metadata"),
                    GetCursorNullableInt(cursor, "duration_in_seconds"),
                    ParseDateTimeOffset(GetCursorString(cursor, "occurred_at_utc")) ?? DateTimeOffset.UtcNow));
            }

            return items;
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "[OfflineDb] Unable to preserve existing sync queue before seed replacement.");
            return [];
        }
    }

    private void RestorePendingQueue(SQLiteDatabase db, IReadOnlyList<QueuedMobileUsageEvent> pendingQueue)
    {
        foreach (var usageEvent in pendingQueue)
        {
            var values = new ContentValues();
            values.Put("idempotency_key", usageEvent.IdempotencyKey);
            values.Put("event_type", usageEvent.EventType);
            PutNullable(values, "poi_id", usageEvent.PoiId);
            values.Put("language_code", usageEvent.LanguageCode);
            values.Put("platform", usageEvent.Platform);
            values.Put("session_id", usageEvent.SessionId);
            values.Put("source", usageEvent.Source);
            PutNullable(values, "metadata", usageEvent.Metadata);
            if (usageEvent.DurationInSeconds > 0)
            {
                values.Put("duration_in_seconds", usageEvent.DurationInSeconds.Value);
            }
            else
            {
                values.PutNull("duration_in_seconds");
            }

            values.Put("occurred_at_utc", usageEvent.OccurredAt.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture));
            values.Put("status", "pending");
            values.Put("retry_count", 0);
            values.PutNull("last_error");
            values.PutNull("last_attempt_at_utc");
            db.InsertWithOnConflict("sync_logs_queue", null, values, Conflict.Ignore);
        }
    }

    private SQLiteDatabase OpenDatabase()
        => SQLiteDatabase.OpenDatabase(
            DatabasePath,
            null,
            DatabaseOpenFlags.OpenReadwrite | DatabaseOpenFlags.CreateIfNecessary)!;

    private static void EnsureSchema(SQLiteDatabase db)
    {
        db.ExecSQL(
            """
            CREATE TABLE IF NOT EXISTS mobile_metadata (
                key TEXT PRIMARY KEY NOT NULL,
                value TEXT NOT NULL
            );
            """);
        db.ExecSQL(
            """
            CREATE TABLE IF NOT EXISTS bootstrap_cache (
                id TEXT PRIMARY KEY NOT NULL,
                language_code TEXT NOT NULL,
                envelope_json TEXT NOT NULL,
                dataset_version TEXT NOT NULL,
                installation_source TEXT NOT NULL,
                saved_at_utc TEXT NOT NULL
            );
            """);
        db.ExecSQL(
            """
            CREATE TABLE IF NOT EXISTS categories (
                id TEXT PRIMARY KEY NOT NULL,
                name TEXT NOT NULL
            );
            """);
        db.ExecSQL(
            """
            CREATE TABLE IF NOT EXISTS settings (
                key TEXT PRIMARY KEY NOT NULL,
                value TEXT NOT NULL
            );
            """);
        db.ExecSQL(
            """
            CREATE TABLE IF NOT EXISTS pois (
                id TEXT PRIMARY KEY NOT NULL,
                slug TEXT NOT NULL,
                address TEXT NOT NULL,
                lat REAL NOT NULL,
                lng REAL NOT NULL,
                category_id TEXT NOT NULL,
                status TEXT NOT NULL,
                featured INTEGER NOT NULL,
                price_range TEXT NOT NULL,
                trigger_radius REAL NOT NULL,
                priority INTEGER NOT NULL,
                place_tier INTEGER NOT NULL DEFAULT 0,
                average_visit_duration INTEGER NOT NULL,
                popularity_score INTEGER NOT NULL,
                tags_json TEXT NOT NULL
            );
            """);
        db.ExecSQL(
            """
            CREATE TABLE IF NOT EXISTS poi_translations (
                poi_id TEXT NOT NULL,
                language_code TEXT NOT NULL,
                title TEXT NOT NULL,
                short_text TEXT NOT NULL,
                full_text TEXT NOT NULL,
                PRIMARY KEY (poi_id, language_code)
            );
            """);
        db.ExecSQL(
            """
            CREATE TABLE IF NOT EXISTS tours (
                id TEXT PRIMARY KEY NOT NULL,
                name TEXT NOT NULL,
                theme TEXT NOT NULL,
                description TEXT NOT NULL,
                duration_minutes INTEGER NOT NULL,
                cover_image_url TEXT NOT NULL,
                is_active INTEGER NOT NULL,
                updated_at_utc TEXT NOT NULL
            );
            """);
        db.ExecSQL(
            """
            CREATE TABLE IF NOT EXISTS tour_pois (
                tour_id TEXT NOT NULL,
                poi_id TEXT NOT NULL,
                stop_order INTEGER NOT NULL,
                PRIMARY KEY (tour_id, poi_id)
            );
            """);
        db.ExecSQL(
            """
            CREATE TABLE IF NOT EXISTS audio_assets (
                id TEXT PRIMARY KEY NOT NULL,
                entity_type TEXT NOT NULL,
                entity_id TEXT NOT NULL,
                language_code TEXT NOT NULL,
                audio_url TEXT NOT NULL,
                local_path TEXT NOT NULL,
                content_version TEXT NOT NULL,
                updated_at_utc TEXT NOT NULL
            );
            """);
        db.ExecSQL(
            """
            CREATE TABLE IF NOT EXISTS media_assets (
                id TEXT PRIMARY KEY NOT NULL,
                entity_type TEXT NOT NULL,
                entity_id TEXT NOT NULL,
                media_type TEXT NOT NULL,
                remote_url TEXT NOT NULL,
                local_path TEXT NOT NULL,
                alt_text TEXT NOT NULL,
                created_at_utc TEXT NOT NULL
            );
            """);
        db.ExecSQL(
            """
            CREATE TABLE IF NOT EXISTS sync_logs_queue (
                idempotency_key TEXT PRIMARY KEY NOT NULL,
                event_type TEXT NOT NULL,
                poi_id TEXT NULL,
                language_code TEXT NOT NULL,
                platform TEXT NOT NULL,
                session_id TEXT NOT NULL,
                source TEXT NOT NULL,
                metadata TEXT NULL,
                duration_in_seconds INTEGER NULL,
                occurred_at_utc TEXT NOT NULL,
                status TEXT NOT NULL DEFAULT 'pending',
                retry_count INTEGER NOT NULL DEFAULT 0,
                last_error TEXT NULL,
                last_attempt_at_utc TEXT NULL
            );
            """);
        db.ExecSQL(
            """
            CREATE INDEX IF NOT EXISTS ix_sync_logs_queue_status
            ON sync_logs_queue (status, occurred_at_utc);
            """);

        EnsureColumnExists(db, "pois", "place_tier", "INTEGER NOT NULL DEFAULT 0");
    }

    private static void EnsureColumnExists(SQLiteDatabase db, string tableName, string columnName, string columnDefinition)
    {
        if (HasColumn(db, tableName, columnName))
        {
            return;
        }

        db.ExecSQL($"ALTER TABLE {tableName} ADD COLUMN {columnName} {columnDefinition};");
    }

    private static bool HasColumn(SQLiteDatabase db, string tableName, string columnName)
    {
        using var cursor = db.RawQuery($"PRAGMA table_info({tableName});", null);
        while (cursor.MoveToNext())
        {
            if (string.Equals(GetCursorString(cursor, "name"), columnName, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private string? TryReadMetadataValue(string key)
    {
        try
        {
            using var db = OpenDatabase();
            EnsureSchema(db);
            using var cursor = db.RawQuery(
                "SELECT value FROM mobile_metadata WHERE key = ? LIMIT 1;",
                [key]);
            return cursor.MoveToFirst() ? cursor.GetString(0) : null;
        }
        catch
        {
            return null;
        }
    }

    private static void PutMetadata(SQLiteDatabase db, string key, string? value)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            return;
        }

        var values = new ContentValues();
        values.Put("key", key.Trim());
        values.Put("value", value?.Trim() ?? string.Empty);
        db.InsertWithOnConflict("mobile_metadata", null, values, Conflict.Replace);
    }

    private static void PutNullable(ContentValues values, string key, string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            values.PutNull(key);
            return;
        }

        values.Put(key, value.Trim());
    }

    private static string GetCursorString(Android.Database.ICursor cursor, string columnName)
    {
        var index = cursor.GetColumnIndex(columnName);
        return index < 0 || cursor.IsNull(index) ? string.Empty : cursor.GetString(index) ?? string.Empty;
    }

    private static string? GetCursorNullableString(Android.Database.ICursor cursor, string columnName)
    {
        var index = cursor.GetColumnIndex(columnName);
        return index < 0 || cursor.IsNull(index) ? null : cursor.GetString(index);
    }

    private static int? GetCursorNullableInt(Android.Database.ICursor cursor, string columnName)
    {
        var index = cursor.GetColumnIndex(columnName);
        return index < 0 || cursor.IsNull(index) ? null : cursor.GetInt(index);
    }

    private static DateTimeOffset? ParseDateTimeOffset(string? value)
        => DateTimeOffset.TryParse(
            value,
            CultureInfo.InvariantCulture,
            DateTimeStyles.RoundtripKind,
            out var parsed)
            ? parsed
            : null;

    private static string TryReadDatasetVersion(string bootstrapEnvelopeJson)
    {
        try
        {
            using var document = JsonDocument.Parse(bootstrapEnvelopeJson);
            if (document.RootElement.TryGetProperty("data", out var data) &&
                data.TryGetProperty("syncState", out var syncState) &&
                syncState.TryGetProperty("version", out var versionElement))
            {
                var version = versionElement.GetString();
                if (!string.IsNullOrWhiteSpace(version))
                {
                    return version.Trim();
                }
            }
        }
        catch
        {
            // A remote payload without syncState is still useful as a local cache.
        }

        return $"mobile-cache-{DateTimeOffset.UtcNow:yyyyMMddHHmmss}";
    }
}
