namespace VinhKhanh.MobileApp.Models;

public enum OfflinePackageLifecycleStatus
{
    NotInstalled,
    Ready,
    Preparing,
    Downloading,
    Validating,
    Installing,
    Completed,
    Error,
    Deleting,
    Canceled
}

public sealed class OfflinePackageMetadata
{
    public string Version { get; set; } = string.Empty;
    public DateTimeOffset GeneratedAtUtc { get; set; }
    public DateTimeOffset LastUpdatedAtUtc { get; set; }
    public string InstallationSource { get; set; } = string.Empty;
    public DateTimeOffset? ServerLastChangedAtUtc { get; set; }
    public long PackageSizeBytes { get; set; }
    public int PoiCount { get; set; }
    public int AudioCount { get; set; }
    public int ImageCount { get; set; }
    public int TourCount { get; set; }
    public int LanguageCount { get; set; }
    public int FileCount { get; set; }
}

public static class OfflinePackageInstallationSources
{
    public const string BundledSeed = "bundled_seed";
    public const string Downloaded = "downloaded";
}

public sealed class OfflinePackageFileEntry
{
    public string Key { get; set; } = string.Empty;
    public string Kind { get; set; } = string.Empty;
    public string RelativePath { get; set; } = string.Empty;
    public string EntityType { get; set; } = string.Empty;
    public string EntityId { get; set; } = string.Empty;
    public string LanguageCode { get; set; } = string.Empty;
    public long SizeBytes { get; set; }
}

public sealed class OfflinePackageManifest
{
    public string Version { get; set; } = string.Empty;
    public DateTimeOffset GeneratedAtUtc { get; set; }
    public List<OfflinePackageFileEntry> Files { get; set; } = [];
}

public sealed class OfflinePackageInstallation
{
    public OfflinePackageMetadata Metadata { get; set; } = new();
    public OfflinePackageManifest Manifest { get; set; } = new();
    public string BootstrapEnvelopeJson { get; set; } = string.Empty;
    public IReadOnlyDictionary<string, string> AssetMap { get; set; } =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
}

public sealed class OfflinePackageState
{
    public static OfflinePackageState Empty { get; } = new();

    public OfflinePackageLifecycleStatus Status { get; set; } = OfflinePackageLifecycleStatus.NotInstalled;
    public bool IsInstalled { get; set; }
    public bool IsUpdateAvailable { get; set; }
    public bool CanReachServer { get; set; }
    public string InstalledVersion { get; set; } = string.Empty;
    public string RemoteVersion { get; set; } = string.Empty;
    public long DownloadedBytes { get; set; }
    public long TotalBytes { get; set; }
    public int DownloadedFileCount { get; set; }
    public int TotalFileCount { get; set; }
    public string ErrorMessage { get; set; } = string.Empty;
    public OfflinePackageMetadata? Metadata { get; set; }

    public bool IsBusy =>
        Status is OfflinePackageLifecycleStatus.Preparing or
        OfflinePackageLifecycleStatus.Downloading or
        OfflinePackageLifecycleStatus.Validating or
        OfflinePackageLifecycleStatus.Installing or
        OfflinePackageLifecycleStatus.Deleting;

    public double ProgressFraction
    {
        get
        {
            if (TotalBytes > 0)
            {
                return Math.Clamp((double)DownloadedBytes / TotalBytes, 0d, 1d);
            }

            if (TotalFileCount > 0)
            {
                return Math.Clamp((double)DownloadedFileCount / TotalFileCount, 0d, 1d);
            }

            return Status switch
            {
                OfflinePackageLifecycleStatus.Completed => 1d,
                OfflinePackageLifecycleStatus.Ready => 1d,
                _ => 0d
            };
        }
    }

    public int ProgressPercent => (int)Math.Round(ProgressFraction * 100d, MidpointRounding.AwayFromZero);
}

public sealed class OfflinePackageVerificationResult
{
    public bool IsValid { get; set; }
    public int ManifestFileCount { get; set; }
    public int MissingFileCount { get; set; }
    public int InvalidAudioFileCount { get; set; }
    public int InvalidImageFileCount { get; set; }
    public int BootstrapPoiCount { get; set; }
    public int BootstrapRouteCount { get; set; }
    public int BootstrapAudioGuideCount { get; set; }
    public string Summary { get; set; } = string.Empty;
    public IReadOnlyList<string> Problems { get; set; } = Array.Empty<string>();
}
