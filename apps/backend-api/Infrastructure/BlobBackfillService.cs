using Microsoft.Extensions.Options;
using VinhKhanh.BackendApi.Models;

namespace VinhKhanh.BackendApi.Infrastructure;

public sealed class BlobBackfillService(
    IWebHostEnvironment environment,
    AdminDataRepository repository,
    IBlobStorageService blobStorageService,
    IOptions<BlobStorageOptions> optionsAccessor,
    ILogger<BlobBackfillService> logger)
{
    private readonly BlobStorageOptions _options = optionsAccessor.Value;

    public async Task<BlobBackfillResult> RunAsync(
        AdminRequestContext actor,
        bool dryRun,
        CancellationToken cancellationToken)
    {
        if (!actor.IsSuperAdmin)
        {
            throw new ApiForbiddenException("Chi Super Admin moi duoc chay Blob backfill.");
        }

        if (!blobStorageService.IsConfigured)
        {
            throw new InvalidOperationException("BlobStorage:ConnectionString is not configured.");
        }

        var result = new BlobBackfillAccumulator(dryRun);

        await BackfillDownloadsAsync(result, cancellationToken);
        await BackfillAudioGuidesAsync(actor, result, cancellationToken);
        await BackfillMediaAssetsAsync(result, cancellationToken);
        await BackfillFoodItemImagesAsync(result, cancellationToken);
        await BackfillRouteImagesAsync(result, cancellationToken);
        await BackfillUnlinkedStorageFilesAsync(result, cancellationToken);

        var finalResult = result.ToResult();
        logger.LogInformation(
            "[BlobBackfill] completed. dryRun={DryRun}; scanned={Scanned}; uploaded={Uploaded}; skipped={Skipped}; updated={Updated}; failed={Failed}",
            finalResult.DryRun,
            finalResult.Scanned,
            finalResult.Uploaded,
            finalResult.Skipped,
            finalResult.DatabaseUpdated,
            finalResult.Failed);
        return finalResult;
    }

    private async Task BackfillDownloadsAsync(BlobBackfillAccumulator result, CancellationToken cancellationToken)
    {
        var downloadsRoot = Path.Combine(GetWebRootPath(), "downloads");
        if (!Directory.Exists(downloadsRoot))
        {
            return;
        }

        foreach (var file in Directory.EnumerateFiles(downloadsRoot, "*", SearchOption.AllDirectories))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (ShouldSkipLocalFile(file))
            {
                continue;
            }

            var fileName = Path.GetFileName(file);
            await UploadFileIfNeededAsync(
                result,
                file,
                blobStorageService.NormalizeBlobPath(_options.ApkFolder, fileName),
                cancellationToken);
        }
    }

    private async Task BackfillAudioGuidesAsync(
        AdminRequestContext actor,
        BlobBackfillAccumulator result,
        CancellationToken cancellationToken)
    {
        foreach (var audioGuide in repository.GetAudioGuides(actor))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (TryResolveBlobPath(audioGuide.AudioUrl, out _) ||
                TryResolveBlobPath(audioGuide.AudioFilePath, out _))
            {
                result.MarkSkipped("audio-guide-already-blob", audioGuide.Id, audioGuide.AudioUrl);
                continue;
            }

            var localPath = ResolveLocalAssetPath(audioGuide.AudioFilePath) ??
                            ResolveLocalAssetPath(audioGuide.AudioUrl);
            if (string.IsNullOrWhiteSpace(localPath) || !File.Exists(localPath))
            {
                result.MarkFailed("audio-guide-missing-local-file", audioGuide.Id, audioGuide.AudioUrl);
                continue;
            }

            var blobPath = blobStorageService.NormalizeBlobPath(
                _options.AudioFolder,
                LanguageRegistry.GetStorageCode(audioGuide.LanguageCode),
                audioGuide.EntityId,
                Path.GetFileName(localPath));
            var upload = await UploadFileIfNeededAsync(result, localPath, blobPath, cancellationToken);
            if (!result.DryRun && upload is not null)
            {
                repository.UpdateAudioGuideBlobLocation(
                    audioGuide.Id,
                    upload.PublicUrl,
                    upload.BlobPath,
                    actor.Name);
                result.MarkDatabaseUpdated("audio-guide", audioGuide.Id, upload.BlobPath);
            }
        }
    }

    private async Task BackfillMediaAssetsAsync(BlobBackfillAccumulator result, CancellationToken cancellationToken)
    {
        foreach (var mediaAsset in repository.GetMediaAssets())
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (TryResolveBlobPath(mediaAsset.Url, out _))
            {
                result.MarkSkipped("media-asset-already-blob", mediaAsset.Id, mediaAsset.Url);
                continue;
            }

            var localPath = ResolveLocalAssetPath(mediaAsset.Url);
            if (string.IsNullOrWhiteSpace(localPath) || !File.Exists(localPath))
            {
                result.MarkFailed("media-asset-missing-local-file", mediaAsset.Id, mediaAsset.Url);
                continue;
            }

            var blobPath = blobStorageService.NormalizeBlobPath(
                _options.MediaFolder,
                NormalizeEntitySegment(mediaAsset.EntityType),
                mediaAsset.EntityId,
                Path.GetFileName(localPath));
            var upload = await UploadFileIfNeededAsync(result, localPath, blobPath, cancellationToken);
            if (!result.DryRun && upload is not null)
            {
                repository.UpdateMediaAssetBlobUrl(mediaAsset.Id, upload.PublicUrl);
                result.MarkDatabaseUpdated("media-asset", mediaAsset.Id, upload.BlobPath);
            }
        }
    }

    private async Task BackfillFoodItemImagesAsync(BlobBackfillAccumulator result, CancellationToken cancellationToken)
    {
        foreach (var foodItem in repository.GetFoodItems())
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (TryResolveBlobPath(foodItem.ImageUrl, out _))
            {
                result.MarkSkipped("food-image-already-blob", foodItem.Id, foodItem.ImageUrl);
                continue;
            }

            var localPath = ResolveLocalAssetPath(foodItem.ImageUrl);
            if (string.IsNullOrWhiteSpace(localPath) || !File.Exists(localPath))
            {
                result.MarkFailed("food-image-missing-local-file", foodItem.Id, foodItem.ImageUrl);
                continue;
            }

            var blobPath = blobStorageService.NormalizeBlobPath(
                _options.MediaFolder,
                "food_item",
                foodItem.Id,
                Path.GetFileName(localPath));
            var upload = await UploadFileIfNeededAsync(result, localPath, blobPath, cancellationToken);
            if (!result.DryRun && upload is not null)
            {
                repository.UpdateFoodItemImageBlobUrl(foodItem.Id, upload.PublicUrl);
                result.MarkDatabaseUpdated("food-item", foodItem.Id, upload.BlobPath);
            }
        }
    }

    private async Task BackfillRouteImagesAsync(BlobBackfillAccumulator result, CancellationToken cancellationToken)
    {
        foreach (var route in repository.GetRoutes())
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (string.IsNullOrWhiteSpace(route.CoverImageUrl) ||
                TryResolveBlobPath(route.CoverImageUrl, out _))
            {
                continue;
            }

            var localPath = ResolveLocalAssetPath(route.CoverImageUrl);
            if (string.IsNullOrWhiteSpace(localPath) || !File.Exists(localPath))
            {
                result.MarkFailed("route-image-missing-local-file", route.Id, route.CoverImageUrl);
                continue;
            }

            var blobPath = blobStorageService.NormalizeBlobPath(
                _options.MediaFolder,
                "route",
                route.Id,
                Path.GetFileName(localPath));
            var upload = await UploadFileIfNeededAsync(result, localPath, blobPath, cancellationToken);
            if (!result.DryRun &&
                upload is not null &&
                repository.UpdateRouteCoverImageBlobUrl(route.Id, upload.PublicUrl))
            {
                result.MarkDatabaseUpdated("route", route.Id, upload.BlobPath);
            }
        }
    }

    private async Task BackfillUnlinkedStorageFilesAsync(BlobBackfillAccumulator result, CancellationToken cancellationToken)
    {
        var storageRoot = Path.Combine(GetWebRootPath(), "storage");
        if (!Directory.Exists(storageRoot))
        {
            return;
        }

        foreach (var file in Directory.EnumerateFiles(storageRoot, "*", SearchOption.AllDirectories))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (ShouldSkipLocalFile(file))
            {
                continue;
            }

            var relativePath = Path.GetRelativePath(storageRoot, file).Replace('\\', '/');
            var blobPath = TryMapLegacyAudioPath(relativePath, file) ??
                           blobStorageService.NormalizeBlobPath(_options.MediaFolder, relativePath);
            await UploadFileIfNeededAsync(result, file, blobPath, cancellationToken);
        }
    }

    private async Task<BlobUploadResult?> UploadFileIfNeededAsync(
        BlobBackfillAccumulator result,
        string localPath,
        string blobPath,
        CancellationToken cancellationToken)
    {
        result.MarkScanned(localPath, blobPath);
        if (result.DryRun)
        {
            return new BlobUploadResult(
                blobPath,
                blobStorageService.GetPublicUrl(blobPath),
                string.Empty,
                new FileInfo(localPath).Length,
                Uploaded: false,
                Skipped: true);
        }

        try
        {
            var uploaded = await blobStorageService.UploadLocalFileAsync(localPath, blobPath, null, cancellationToken);
            if (uploaded.Uploaded)
            {
                result.MarkUploaded(localPath, uploaded.BlobPath);
            }
            else
            {
                result.MarkSkipped("blob-exists", localPath, uploaded.BlobPath);
            }

            return uploaded;
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "[BlobBackfill] upload failed. localPath={LocalPath}; blobPath={BlobPath}", localPath, blobPath);
            result.MarkFailed("upload-failed", localPath, blobPath);
            return null;
        }
    }

    private string? TryMapLegacyAudioPath(string relativePath, string localPath)
    {
        var parts = relativePath.Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length >= 5 &&
            string.Equals(parts[0], "audio", StringComparison.OrdinalIgnoreCase) &&
            string.Equals(parts[1], "pois", StringComparison.OrdinalIgnoreCase))
        {
            return blobStorageService.NormalizeBlobPath(_options.AudioFolder, parts[3], parts[2], Path.GetFileName(localPath));
        }

        return null;
    }

    private bool TryResolveBlobPath(string? value, out string blobPath)
    {
        blobPath = blobStorageService.TryGetBlobPathFromPublicUrl(value) ?? string.Empty;
        return !string.IsNullOrWhiteSpace(blobPath);
    }

    private string? ResolveLocalAssetPath(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var normalized = value.Trim();
        if (blobStorageService.IsBlobUrlOrPath(normalized))
        {
            return null;
        }

        if (Path.IsPathFullyQualified(normalized))
        {
            return IsInsideWebRoot(normalized) ? normalized : null;
        }

        if (Uri.TryCreate(normalized, UriKind.Absolute, out var absoluteUri))
        {
            normalized = Uri.UnescapeDataString(absoluteUri.AbsolutePath);
        }

        normalized = Uri.UnescapeDataString(normalized).TrimStart('/').Replace('\\', '/');
        if (!normalized.StartsWith("storage/", StringComparison.OrdinalIgnoreCase) &&
            !normalized.StartsWith("downloads/", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var candidatePath = Path.GetFullPath(Path.Combine(GetWebRootPath(), normalized.Replace('/', Path.DirectorySeparatorChar)));
        return IsInsideWebRoot(candidatePath) ? candidatePath : null;
    }

    private bool IsInsideWebRoot(string path)
    {
        var normalizedWebRoot = Path.GetFullPath(GetWebRootPath()).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) +
                                Path.DirectorySeparatorChar;
        var normalizedPath = Path.GetFullPath(path);
        return normalizedPath.StartsWith(normalizedWebRoot, StringComparison.OrdinalIgnoreCase);
    }

    private string GetWebRootPath()
        => environment.WebRootPath ?? Path.Combine(environment.ContentRootPath, "wwwroot");

    private static bool ShouldSkipLocalFile(string file)
        => string.Equals(Path.GetFileName(file), ".gitkeep", StringComparison.OrdinalIgnoreCase) ||
           string.Equals(Path.GetExtension(file), ".tmp", StringComparison.OrdinalIgnoreCase);

    private static string NormalizeEntitySegment(string entityType)
        => string.Equals(entityType.Trim(), "food-item", StringComparison.OrdinalIgnoreCase)
            ? "food_item"
            : entityType.Trim().ToLowerInvariant();

    private sealed class BlobBackfillAccumulator(bool dryRun)
    {
        private readonly List<BlobBackfillItem> _items = [];

        public bool DryRun { get; } = dryRun;
        public int Scanned { get; private set; }
        public int Uploaded { get; private set; }
        public int Skipped { get; private set; }
        public int DatabaseUpdated { get; private set; }
        public int Failed { get; private set; }

        public void MarkScanned(string source, string blobPath)
        {
            Scanned++;
            Add("scanned", source, blobPath);
        }

        public void MarkUploaded(string source, string blobPath)
        {
            Uploaded++;
            Add("uploaded", source, blobPath);
        }

        public void MarkSkipped(string reason, string source, string target)
        {
            Skipped++;
            Add($"skipped:{reason}", source, target);
        }

        public void MarkDatabaseUpdated(string entityType, string entityId, string blobPath)
        {
            DatabaseUpdated++;
            Add($"db-updated:{entityType}", entityId, blobPath);
        }

        public void MarkFailed(string reason, string source, string target)
        {
            Failed++;
            Add($"failed:{reason}", source, target);
        }

        public BlobBackfillResult ToResult()
            => new(DryRun, Scanned, Uploaded, Skipped, DatabaseUpdated, Failed, _items.TakeLast(100).ToList());

        private void Add(string status, string source, string target)
            => _items.Add(new BlobBackfillItem(status, source, target));
    }
}

public sealed record BlobBackfillResult(
    bool DryRun,
    int Scanned,
    int Uploaded,
    int Skipped,
    int DatabaseUpdated,
    int Failed,
    IReadOnlyList<BlobBackfillItem> RecentItems);

public sealed record BlobBackfillItem(string Status, string Source, string Target);
