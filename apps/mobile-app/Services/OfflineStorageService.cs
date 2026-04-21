using System.Text.Json;
using Microsoft.Maui.Storage;
using VinhKhanh.MobileApp.Helpers;
using VinhKhanh.MobileApp.Models;

namespace VinhKhanh.MobileApp.Services;

public interface IOfflineStorageService
{
    Task<OfflinePackageInstallation?> LoadInstallationAsync(CancellationToken cancellationToken = default);
    Task<OfflinePackageInstallation?> LoadInstallationFromRootAsync(string installationRoot, CancellationToken cancellationToken = default);
    Task<string> CreateStagingRootAsync(CancellationToken cancellationToken = default);
    string GetStagingAssetsDirectory(string stagingRoot);
    string GetStagingBootstrapPath(string stagingRoot);
    string GetStagingManifestPath(string stagingRoot);
    string GetStagingMetadataPath(string stagingRoot);
    string ResolveInstalledAssetPath(string relativePath);
    Task ReplaceInstallationAsync(string stagingRoot, CancellationToken cancellationToken = default);
    Task DeleteInstallationAsync(CancellationToken cancellationToken = default);
    long? TryGetAvailableFreeSpaceBytes();
}

public sealed class OfflineStorageService : IOfflineStorageService
{
    private const string PackageFolderName = "offline-package";
    private const string CurrentFolderName = "current";
    private const string BackupFolderName = "backup";
    private const string BootstrapFileName = "bootstrap-envelope.json";
    private const string MetadataFileName = "metadata.json";
    private const string ManifestFileName = "manifest.json";
    private const string AssetsFolderName = "assets";

    private readonly JsonSerializerOptions _jsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = false
    };

    public async Task<OfflinePackageInstallation?> LoadInstallationAsync(CancellationToken cancellationToken = default)
        => await LoadInstallationFromRootAsync(GetCurrentRoot(), cancellationToken);

    public async Task<OfflinePackageInstallation?> LoadInstallationFromRootAsync(
        string installationRoot,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(installationRoot))
        {
            return null;
        }

        var normalizedRoot = installationRoot.Trim();
        var metadataPath = Path.Combine(normalizedRoot, MetadataFileName);
        var manifestPath = Path.Combine(normalizedRoot, ManifestFileName);
        var bootstrapPath = Path.Combine(normalizedRoot, BootstrapFileName);

        if (!File.Exists(metadataPath) || !File.Exists(manifestPath) || !File.Exists(bootstrapPath))
        {
            return null;
        }

        var metadataTask = File.ReadAllTextAsync(metadataPath, cancellationToken);
        var manifestTask = File.ReadAllTextAsync(manifestPath, cancellationToken);
        var bootstrapTask = File.ReadAllTextAsync(bootstrapPath, cancellationToken);
        await Task.WhenAll(metadataTask, manifestTask, bootstrapTask);

        var metadata = JsonSerializer.Deserialize<OfflinePackageMetadata>(metadataTask.Result, _jsonOptions);
        var manifest = JsonSerializer.Deserialize<OfflinePackageManifest>(manifestTask.Result, _jsonOptions);
        if (metadata is null || manifest is null)
        {
            return null;
        }

        var assetMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in manifest.Files.Where(item => !string.IsNullOrWhiteSpace(item.RelativePath)))
        {
            var localPath = Path.Combine(normalizedRoot, item.RelativePath.Replace('/', Path.DirectorySeparatorChar));
            foreach (var key in OfflineAssetUrlHelper.BuildLookupKeys(item.Key)
                         .Concat(OfflineAssetUrlHelper.BuildLookupKeys(item.RelativePath)))
            {
                assetMap.TryAdd(key, localPath);
            }
        }

        return new OfflinePackageInstallation
        {
            Metadata = metadata,
            Manifest = manifest,
            BootstrapEnvelopeJson = bootstrapTask.Result,
            AssetMap = assetMap
        };
    }

    public Task<string> CreateStagingRootAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var stagingRoot = Path.Combine(GetPackageRoot(), $"staging-{Guid.NewGuid():N}");
        Directory.CreateDirectory(stagingRoot);
        Directory.CreateDirectory(GetStagingAssetsDirectory(stagingRoot));
        return Task.FromResult(stagingRoot);
    }

    public string GetStagingAssetsDirectory(string stagingRoot)
        => Path.Combine(stagingRoot, AssetsFolderName);

    public string GetStagingBootstrapPath(string stagingRoot)
        => Path.Combine(stagingRoot, BootstrapFileName);

    public string GetStagingManifestPath(string stagingRoot)
        => Path.Combine(stagingRoot, ManifestFileName);

    public string GetStagingMetadataPath(string stagingRoot)
        => Path.Combine(stagingRoot, MetadataFileName);

    public string ResolveInstalledAssetPath(string relativePath)
        => Path.Combine(GetCurrentRoot(), relativePath.Replace('/', Path.DirectorySeparatorChar));

    public Task ReplaceInstallationAsync(string stagingRoot, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var packageRoot = GetPackageRoot();
        var currentRoot = GetCurrentRoot();
        var backupRoot = Path.Combine(packageRoot, BackupFolderName);
        Directory.CreateDirectory(packageRoot);

        if (Directory.Exists(backupRoot))
        {
            Directory.Delete(backupRoot, recursive: true);
        }

        try
        {
            if (Directory.Exists(currentRoot))
            {
                Directory.Move(currentRoot, backupRoot);
            }

            Directory.Move(stagingRoot, currentRoot);

            if (Directory.Exists(backupRoot))
            {
                Directory.Delete(backupRoot, recursive: true);
            }
        }
        catch
        {
            if (!Directory.Exists(currentRoot) && Directory.Exists(backupRoot))
            {
                Directory.Move(backupRoot, currentRoot);
            }

            throw;
        }

        return Task.CompletedTask;
    }

    public Task DeleteInstallationAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var packageRoot = GetPackageRoot();
        var currentRoot = GetCurrentRoot();
        if (Directory.Exists(currentRoot))
        {
            Directory.Delete(currentRoot, recursive: true);
        }

        if (!Directory.Exists(packageRoot))
        {
            return Task.CompletedTask;
        }

        foreach (var stagingDirectory in Directory.EnumerateDirectories(
                     packageRoot,
                     "staging-*",
                     SearchOption.TopDirectoryOnly))
        {
            cancellationToken.ThrowIfCancellationRequested();
            Directory.Delete(stagingDirectory, recursive: true);
        }

        return Task.CompletedTask;
    }

    public long? TryGetAvailableFreeSpaceBytes()
    {
        try
        {
            var root = Path.GetPathRoot(FileSystem.Current.AppDataDirectory);
            if (string.IsNullOrWhiteSpace(root))
            {
                return null;
            }

            return new DriveInfo(root).AvailableFreeSpace;
        }
        catch
        {
            return null;
        }
    }

    private static string GetPackageRoot()
        => Path.Combine(FileSystem.Current.AppDataDirectory, PackageFolderName);

    private static string GetCurrentRoot()
        => Path.Combine(GetPackageRoot(), CurrentFolderName);
}
