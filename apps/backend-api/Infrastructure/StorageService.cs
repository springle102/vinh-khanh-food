using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;
using VinhKhanh.BackendApi.Contracts;

namespace VinhKhanh.BackendApi.Infrastructure;

public sealed class StorageService(
    IWebHostEnvironment environment,
    IBlobStorageService blobStorageService,
    IOptions<BlobStorageOptions> blobStorageOptions,
    ILogger<StorageService> logger)
{
    private static readonly Regex InvalidPathChars = new("[^a-zA-Z0-9/_-]+", RegexOptions.Compiled);

    public async Task<StoredFileResponse> SaveAsync(
        IFormFile file,
        string? folder,
        CancellationToken cancellationToken = default)
    {
        if (file.Length <= 0)
        {
            throw new InvalidOperationException("File upload is empty.");
        }

        var normalizedFolder = NormalizeFolder(folder);
        var extension = Path.GetExtension(file.FileName);
        var fileName = $"{DateTimeOffset.UtcNow:yyyyMMddHHmmssfff}-{Guid.NewGuid():N}{extension}";

        if (blobStorageService.IsConfigured)
        {
            var blobPath = BuildUploadBlobPath(normalizedFolder, fileName);
            await using var fileStream = file.OpenReadStream();
            var uploaded = await blobStorageService.UploadAsync(
                fileStream,
                blobPath,
                file.ContentType,
                cancellationToken);

            return new StoredFileResponse(
                uploaded.PublicUrl,
                fileName,
                uploaded.ContentType,
                file.Length,
                uploaded.BlobPath,
                "azure-blob");
        }

        logger.LogWarning(
            "[StorageUpload] Blob storage is not configured; falling back to local wwwroot storage. folder={Folder}; fileName={FileName}",
            normalizedFolder,
            fileName);

        var relativeFolder = Path.Combine("storage", normalizedFolder);
        var targetFolder = Path.Combine(environment.WebRootPath ?? Path.Combine(environment.ContentRootPath, "wwwroot"), relativeFolder);
        Directory.CreateDirectory(targetFolder);

        var targetPath = Path.Combine(targetFolder, fileName);

        await using (var stream = File.Create(targetPath))
        {
            await file.CopyToAsync(stream, cancellationToken);
        }

        var relativeUrl = $"/{relativeFolder.Replace("\\", "/")}/{fileName}";

        return new StoredFileResponse(
            relativeUrl,
            fileName,
            file.ContentType,
            file.Length,
            null,
            "local");
    }

    private string BuildUploadBlobPath(string normalizedFolder, string fileName)
    {
        var options = blobStorageOptions.Value;
        var normalized = normalizedFolder.Replace('\\', '/').Trim('/');
        if (normalized.StartsWith($"{options.AudioFolder}/", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(normalized, options.AudioFolder, StringComparison.OrdinalIgnoreCase))
        {
            var suffix = normalized[options.AudioFolder.Length..].Trim('/');
            return blobStorageService.NormalizeBlobPath(options.AudioFolder, suffix, fileName);
        }

        if (normalized.StartsWith($"{options.ApkFolder}/", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(normalized, options.ApkFolder, StringComparison.OrdinalIgnoreCase))
        {
            var suffix = normalized[options.ApkFolder.Length..].Trim('/');
            return blobStorageService.NormalizeBlobPath(options.ApkFolder, suffix, fileName);
        }

        return blobStorageService.NormalizeBlobPath(options.MediaFolder, normalized, fileName);
    }

    private static string NormalizeFolder(string? folder)
    {
        var cleaned = string.IsNullOrWhiteSpace(folder) ? "misc" : InvalidPathChars.Replace(folder.Trim(), "-");
        var normalized = cleaned
            .Replace('\\', '/')
            .Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(segment => segment.Trim('-'))
            .Where(segment => !string.IsNullOrWhiteSpace(segment))
            .ToArray();

        return normalized.Length == 0 ? "misc" : Path.Combine(normalized);
    }
}
