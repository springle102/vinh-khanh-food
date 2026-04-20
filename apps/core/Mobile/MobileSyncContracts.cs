namespace VinhKhanh.Core.Mobile;

public static class MobileUsageEventTypes
{
    public const string PoiView = "poi_view";
    public const string AudioPlay = "audio_play";
    public const string QrScan = "qr_scan";

    public static string Normalize(string? eventType)
    {
        return eventType?.Trim().ToLowerInvariant() switch
        {
            PoiView => PoiView,
            AudioPlay => AudioPlay,
            QrScan => QrScan,
            _ => string.Empty
        };
    }
}

public sealed record MobileUsageEventSyncItem(
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

public sealed record MobileUsageEventSyncRequest(
    IReadOnlyList<MobileUsageEventSyncItem> Events);

public sealed record MobileUsageEventSyncResult(
    string IdempotencyKey,
    bool Accepted,
    string? ServerEventId,
    string? ErrorMessage);

public sealed record MobileUsageEventSyncResponse(
    int AcceptedCount,
    int RejectedCount,
    IReadOnlyList<MobileUsageEventSyncResult> Results);
