namespace VinhKhanh.Core.Mobile;

public static class MobileDatasetConstants
{
    public const string CurrentBootstrapCacheId = "current";
    public const string SeedInstallationSource = "bundled_seed";
    public const string DownloadedInstallationSource = "downloaded";
    public const int DefaultSyncBatchSize = 50;
}

public sealed record MobileDatasetVersionResponse(
    string Version,
    DateTimeOffset GeneratedAt,
    DateTimeOffset LastChangedAt,
    int PoiCount,
    int TourCount,
    int AudioCount,
    int ImageCount);
