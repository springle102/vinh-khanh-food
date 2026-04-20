using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Maui.Storage;
using VinhKhanh.MobileApp.Models;

namespace VinhKhanh.MobileApp.Services;

public interface IBundledOfflinePackageSeedService
{
    Task<OfflinePackageInstallation?> EnsureInstalledAsync(CancellationToken cancellationToken = default);
}

public sealed class BundledOfflinePackageSeedService : IBundledOfflinePackageSeedService
{
    private const string PackageRoot = "seed/offline-package";
    private const string MetadataAssetName = $"{PackageRoot}/metadata.json";
    private const string ManifestAssetName = $"{PackageRoot}/manifest.json";
    private const string BootstrapAssetName = $"{PackageRoot}/bootstrap-envelope.json";

    private readonly SemaphoreSlim _ensureLock = new(1, 1);
    private readonly JsonSerializerOptions _jsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = false
    };
    private readonly IOfflineStorageService _storageService;
    private readonly ILogger<BundledOfflinePackageSeedService> _logger;

    public BundledOfflinePackageSeedService(
        IOfflineStorageService storageService,
        ILogger<BundledOfflinePackageSeedService> logger)
    {
        _storageService = storageService;
        _logger = logger;
    }

    public async Task<OfflinePackageInstallation?> EnsureInstalledAsync(CancellationToken cancellationToken = default)
    {
        await _ensureLock.WaitAsync(cancellationToken);
        try
        {
            var packagedSeed = await LoadPackagedSeedAsync(cancellationToken);
            var currentInstallation = await _storageService.LoadInstallationAsync(cancellationToken);
            if (packagedSeed is null)
            {
                return currentInstallation;
            }

            if (!ShouldInstallPackagedSeed(currentInstallation, packagedSeed.Metadata))
            {
                return currentInstallation;
            }

            var stagingRoot = await _storageService.CreateStagingRootAsync(cancellationToken);
            try
            {
                await InstallPackagedSeedAsync(packagedSeed, stagingRoot, cancellationToken);
                await _storageService.ReplaceInstallationAsync(stagingRoot, cancellationToken);
                stagingRoot = string.Empty;
            }
            finally
            {
                if (!string.IsNullOrWhiteSpace(stagingRoot) && Directory.Exists(stagingRoot))
                {
                    try
                    {
                        Directory.Delete(stagingRoot, recursive: true);
                    }
                    catch
                    {
                        // Best-effort cleanup only.
                    }
                }
            }

            var installed = await _storageService.LoadInstallationAsync(cancellationToken);
            _logger.LogInformation(
                "[BundledSeed] Bundled offline seed installed. version={Version}; files={FileCount}; audioCount={AudioCount}; poiCount={PoiCount}",
                installed?.Metadata.Version ?? packagedSeed.Metadata.Version,
                installed?.Metadata.FileCount ?? packagedSeed.Manifest.Files.Count,
                installed?.Metadata.AudioCount ?? packagedSeed.Metadata.AudioCount,
                installed?.Metadata.PoiCount ?? packagedSeed.Metadata.PoiCount);
            return installed;
        }
        finally
        {
            _ensureLock.Release();
        }
    }

    private bool ShouldInstallPackagedSeed(
        OfflinePackageInstallation? currentInstallation,
        OfflinePackageMetadata packagedMetadata)
    {
        if (currentInstallation is null)
        {
            return true;
        }

        if (!string.Equals(
                currentInstallation.Metadata.InstallationSource,
                OfflinePackageInstallationSources.BundledSeed,
                StringComparison.OrdinalIgnoreCase))
        {
            var currentAssetHealth = InspectInstalledAssets(currentInstallation);
            if (!IsInstalledAssetHealthAcceptable(currentInstallation.Metadata, packagedMetadata, currentAssetHealth))
            {
                _logger.LogWarning(
                    "[BundledSeed] Replacing existing offline package with bundled seed because local asset validation failed. currentSource={CurrentSource}; currentVersion={CurrentVersion}; packagedVersion={PackagedVersion}; metadataAudio={MetadataAudio}; manifestAudio={ManifestAudio}; usableAudio={UsableAudio}; audioLanguages={AudioLanguages}; metadataImages={MetadataImages}; manifestImages={ManifestImages}; usableImages={UsableImages}",
                    currentInstallation.Metadata.InstallationSource,
                    currentInstallation.Metadata.Version,
                    packagedMetadata.Version,
                    currentInstallation.Metadata.AudioCount,
                    currentAssetHealth.AudioFileCount,
                    currentAssetHealth.UsableAudioFileCount,
                    currentAssetHealth.AudioLanguageCount,
                    currentInstallation.Metadata.ImageCount,
                    currentAssetHealth.ImageFileCount,
                    currentAssetHealth.UsableImageFileCount);
                return true;
            }

            if (IsPackagedSeedMoreComplete(currentInstallation.Metadata, packagedMetadata))
            {
                _logger.LogInformation(
                    "[BundledSeed] Replacing existing offline package with bundled seed because bundled data is more complete. currentSource={CurrentSource}; currentVersion={CurrentVersion}; packagedVersion={PackagedVersion}; currentPois={CurrentPoiCount}; packagedPois={PackagedPoiCount}; currentAudio={CurrentAudioCount}; packagedAudio={PackagedAudioCount}; currentImages={CurrentImageCount}; packagedImages={PackagedImageCount}",
                    currentInstallation.Metadata.InstallationSource,
                    currentInstallation.Metadata.Version,
                    packagedMetadata.Version,
                    currentInstallation.Metadata.PoiCount,
                    packagedMetadata.PoiCount,
                    currentInstallation.Metadata.AudioCount,
                    packagedMetadata.AudioCount,
                    currentInstallation.Metadata.ImageCount,
                    packagedMetadata.ImageCount);
                return true;
            }

            return false;
        }

        var bundledAssetHealth = InspectInstalledAssets(currentInstallation);
        if (!IsInstalledAssetHealthAcceptable(currentInstallation.Metadata, packagedMetadata, bundledAssetHealth))
        {
            _logger.LogWarning(
                "[BundledSeed] Reinstalling bundled offline seed because installed bundled assets are missing or invalid. currentVersion={CurrentVersion}; packagedVersion={PackagedVersion}; metadataAudio={MetadataAudio}; manifestAudio={ManifestAudio}; usableAudio={UsableAudio}; audioLanguages={AudioLanguages}; metadataImages={MetadataImages}; manifestImages={ManifestImages}; usableImages={UsableImages}",
                currentInstallation.Metadata.Version,
                packagedMetadata.Version,
                currentInstallation.Metadata.AudioCount,
                bundledAssetHealth.AudioFileCount,
                bundledAssetHealth.UsableAudioFileCount,
                bundledAssetHealth.AudioLanguageCount,
                currentInstallation.Metadata.ImageCount,
                bundledAssetHealth.ImageFileCount,
                bundledAssetHealth.UsableImageFileCount);
            return true;
        }

        return !string.Equals(
            currentInstallation.Metadata.Version,
            packagedMetadata.Version,
            StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsPackagedSeedMoreComplete(
        OfflinePackageMetadata currentMetadata,
        OfflinePackageMetadata packagedMetadata)
    {
        if (string.Equals(currentMetadata.Version, packagedMetadata.Version, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return packagedMetadata.PoiCount >= currentMetadata.PoiCount &&
               packagedMetadata.TourCount >= currentMetadata.TourCount &&
               (packagedMetadata.AudioCount > currentMetadata.AudioCount ||
                packagedMetadata.ImageCount > currentMetadata.ImageCount ||
                packagedMetadata.FileCount > currentMetadata.FileCount);
    }

    private InstalledAssetHealth InspectInstalledAssets(OfflinePackageInstallation installation)
    {
        var audioEntries = installation.Manifest.Files
            .Where(file => string.Equals(file.Kind, "audio", StringComparison.OrdinalIgnoreCase))
            .ToArray();
        var imageEntries = installation.Manifest.Files
            .Where(file => string.Equals(file.Kind, "image", StringComparison.OrdinalIgnoreCase))
            .ToArray();

        var usableAudioFileCount = 0;
        foreach (var entry in audioEntries)
        {
            if (IsUsableAudioFile(ResolveInstalledAssetPath(installation, entry)))
            {
                usableAudioFileCount++;
            }
        }

        var usableImageFileCount = 0;
        foreach (var entry in imageEntries)
        {
            var filePath = ResolveInstalledAssetPath(installation, entry);
            if (File.Exists(filePath) && new FileInfo(filePath).Length > 0)
            {
                usableImageFileCount++;
            }
        }

        var audioLanguageCount = audioEntries
            .Select(entry => entry.LanguageCode)
            .Where(languageCode => !string.IsNullOrWhiteSpace(languageCode))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Count();

        return new InstalledAssetHealth(
            audioEntries.Length,
            usableAudioFileCount,
            audioLanguageCount,
            imageEntries.Length,
            usableImageFileCount);
    }

    private string ResolveInstalledAssetPath(
        OfflinePackageInstallation installation,
        OfflinePackageFileEntry entry)
    {
        if (!string.IsNullOrWhiteSpace(entry.Key) &&
            installation.AssetMap.TryGetValue(entry.Key, out var mappedPath) &&
            !string.IsNullOrWhiteSpace(mappedPath))
        {
            return mappedPath;
        }

        return _storageService.ResolveInstalledAssetPath(entry.RelativePath);
    }

    private static bool IsInstalledAssetHealthAcceptable(
        OfflinePackageMetadata currentMetadata,
        OfflinePackageMetadata packagedMetadata,
        InstalledAssetHealth health)
    {
        var expectedAudioCount = Math.Max(currentMetadata.AudioCount, packagedMetadata.AudioCount);
        var expectedImageCount = Math.Max(currentMetadata.ImageCount, packagedMetadata.ImageCount);
        var expectedLanguageCount = Math.Max(currentMetadata.LanguageCount, packagedMetadata.LanguageCount);

        return health.AudioFileCount >= expectedAudioCount &&
               health.UsableAudioFileCount >= expectedAudioCount &&
               health.AudioLanguageCount >= expectedLanguageCount &&
               health.ImageFileCount >= expectedImageCount &&
               health.UsableImageFileCount >= expectedImageCount;
    }

    private static bool IsUsableAudioFile(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
        {
            return false;
        }

        try
        {
            var fileInfo = new FileInfo(filePath);
            if (fileInfo.Length < 128)
            {
                return false;
            }

            var headerLength = (int)Math.Min(64, fileInfo.Length);
            var header = new byte[headerLength];
            using var stream = File.OpenRead(filePath);
            var bytesRead = stream.Read(header, 0, header.Length);
            if (bytesRead <= 0)
            {
                return false;
            }

            if (bytesRead != header.Length)
            {
                Array.Resize(ref header, bytesRead);
            }

            return LooksLikeKnownAudioPayload(header);
        }
        catch
        {
            return false;
        }
    }

    private static bool LooksLikeKnownAudioPayload(byte[] content)
    {
        if (content.Length >= 3 &&
            content[0] == (byte)'I' &&
            content[1] == (byte)'D' &&
            content[2] == (byte)'3')
        {
            return true;
        }

        if (content.Length >= 2 && content[0] == 0xFF && (content[1] & 0xE0) == 0xE0)
        {
            return true;
        }

        if (content.Length >= 4 &&
            content[0] == (byte)'R' &&
            content[1] == (byte)'I' &&
            content[2] == (byte)'F' &&
            content[3] == (byte)'F')
        {
            return true;
        }

        if (content.Length >= 4 &&
            content[0] == (byte)'O' &&
            content[1] == (byte)'g' &&
            content[2] == (byte)'g' &&
            content[3] == (byte)'S')
        {
            return true;
        }

        if (content.Length >= 4 &&
            content[0] == (byte)'f' &&
            content[1] == (byte)'L' &&
            content[2] == (byte)'a' &&
            content[3] == (byte)'C')
        {
            return true;
        }

        if (content.Length >= 4 &&
            content[0] == (byte)'A' &&
            content[1] == (byte)'D' &&
            content[2] == (byte)'I' &&
            content[3] == (byte)'F')
        {
            return true;
        }

        if (content.Length >= 12 &&
            content[4] == (byte)'f' &&
            content[5] == (byte)'t' &&
            content[6] == (byte)'y' &&
            content[7] == (byte)'p')
        {
            return true;
        }

        return false;
    }

    private async Task InstallPackagedSeedAsync(
        PackagedSeed packagedSeed,
        string stagingRoot,
        CancellationToken cancellationToken)
    {
        foreach (var file in packagedSeed.Manifest.Files)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var assetName = BuildPackageAssetName(file.RelativePath);
            await using var packageStream = await FileSystem.OpenAppPackageFileAsync(assetName);
            var targetPath = Path.Combine(stagingRoot, file.RelativePath.Replace('/', Path.DirectorySeparatorChar));
            var targetDirectory = Path.GetDirectoryName(targetPath);
            if (!string.IsNullOrWhiteSpace(targetDirectory))
            {
                Directory.CreateDirectory(targetDirectory);
            }

            await using var output = File.Create(targetPath);
            await packageStream.CopyToAsync(output, cancellationToken);
            await output.FlushAsync(cancellationToken);
            file.SizeBytes = new FileInfo(targetPath).Length;
        }

        var installedMetadata = CloneMetadata(packagedSeed.Metadata);
        installedMetadata.InstallationSource = OfflinePackageInstallationSources.BundledSeed;
        installedMetadata.LastUpdatedAtUtc = DateTimeOffset.UtcNow;
        installedMetadata.FileCount = packagedSeed.Manifest.Files.Count;

        var bootstrapPath = _storageService.GetStagingBootstrapPath(stagingRoot);
        var manifestPath = _storageService.GetStagingManifestPath(stagingRoot);
        var metadataPath = _storageService.GetStagingMetadataPath(stagingRoot);

        await File.WriteAllTextAsync(bootstrapPath, packagedSeed.BootstrapEnvelopeJson, cancellationToken);
        var manifestJson = JsonSerializer.Serialize(packagedSeed.Manifest, _jsonOptions);
        await File.WriteAllTextAsync(manifestPath, manifestJson, cancellationToken);

        installedMetadata.PackageSizeBytes =
            packagedSeed.Manifest.Files.Sum(item => Math.Max(0, item.SizeBytes)) +
            Encoding.UTF8.GetByteCount(packagedSeed.BootstrapEnvelopeJson) +
            Encoding.UTF8.GetByteCount(manifestJson);

        var metadataJson = JsonSerializer.Serialize(installedMetadata, _jsonOptions);
        installedMetadata.PackageSizeBytes += Encoding.UTF8.GetByteCount(metadataJson);
        metadataJson = JsonSerializer.Serialize(installedMetadata, _jsonOptions);
        await File.WriteAllTextAsync(metadataPath, metadataJson, cancellationToken);
    }

    private async Task<PackagedSeed?> LoadPackagedSeedAsync(CancellationToken cancellationToken)
    {
        try
        {
            var metadataJson = await ReadPackageTextAsync(MetadataAssetName, cancellationToken);
            var manifestJson = await ReadPackageTextAsync(ManifestAssetName, cancellationToken);
            var bootstrapJson = await ReadPackageTextAsync(BootstrapAssetName, cancellationToken);

            var metadata = JsonSerializer.Deserialize<OfflinePackageMetadata>(metadataJson, _jsonOptions);
            var manifest = JsonSerializer.Deserialize<OfflinePackageManifest>(manifestJson, _jsonOptions);
            if (metadata is null || manifest is null)
            {
                _logger.LogWarning("[BundledSeed] Bundled seed metadata or manifest is invalid.");
                return null;
            }

            metadata.InstallationSource = OfflinePackageInstallationSources.BundledSeed;
            metadata.FileCount = manifest.Files.Count;
            return new PackagedSeed(metadata, manifest, bootstrapJson);
        }
        catch (FileNotFoundException)
        {
            _logger.LogInformation("[BundledSeed] No bundled offline seed assets were found in the app package.");
            return null;
        }
        catch (DirectoryNotFoundException)
        {
            _logger.LogInformation("[BundledSeed] No bundled offline seed directory was found in the app package.");
            return null;
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "[BundledSeed] Failed to load bundled offline seed metadata.");
            return null;
        }
    }

    private static OfflinePackageMetadata CloneMetadata(OfflinePackageMetadata metadata)
        => new()
        {
            Version = metadata.Version,
            GeneratedAtUtc = metadata.GeneratedAtUtc,
            LastUpdatedAtUtc = metadata.LastUpdatedAtUtc,
            InstallationSource = metadata.InstallationSource,
            ServerLastChangedAtUtc = metadata.ServerLastChangedAtUtc,
            PackageSizeBytes = metadata.PackageSizeBytes,
            PoiCount = metadata.PoiCount,
            AudioCount = metadata.AudioCount,
            ImageCount = metadata.ImageCount,
            TourCount = metadata.TourCount,
            LanguageCount = metadata.LanguageCount,
            FileCount = metadata.FileCount
        };

    private static string BuildPackageAssetName(string relativePath)
        => $"{PackageRoot}/{relativePath.Replace('\\', '/').TrimStart('/')}";

    private static async Task<string> ReadPackageTextAsync(string assetName, CancellationToken cancellationToken)
    {
        await using var stream = await FileSystem.OpenAppPackageFileAsync(assetName);
        using var reader = new StreamReader(stream);
        return await reader.ReadToEndAsync(cancellationToken);
    }

    private sealed record InstalledAssetHealth(
        int AudioFileCount,
        int UsableAudioFileCount,
        int AudioLanguageCount,
        int ImageFileCount,
        int UsableImageFileCount);

    private sealed record PackagedSeed(
        OfflinePackageMetadata Metadata,
        OfflinePackageManifest Manifest,
        string BootstrapEnvelopeJson);
}
