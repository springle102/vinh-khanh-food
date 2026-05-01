using Azure;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Microsoft.Extensions.Options;

namespace VinhKhanh.BackendApi.Infrastructure;

public sealed class BlobStorageService : IBlobStorageService
{
    private readonly BlobStorageOptions _options;
    private readonly ILogger<BlobStorageService> _logger;
    private readonly SemaphoreSlim _containerLock = new(1, 1);
    private readonly BlobContainerClient? _containerClient;
    private bool _containerReady;

    public BlobStorageService(IOptions<BlobStorageOptions> optionsAccessor, ILogger<BlobStorageService> logger)
    {
        _options = optionsAccessor.Value;
        _logger = logger;

        if (_options.HasConnectionString)
        {
            _containerClient = new BlobContainerClient(_options.ConnectionString, _options.ContainerName);
        }
    }

    public bool IsConfigured => _containerClient is not null;

    public async Task<BlobUploadResult> UploadAsync(
        Stream file,
        string blobPath,
        string? contentType,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(file);
        EnsureConfigured();

        var normalizedBlobPath = NormalizeBlobPath(blobPath);
        var resolvedContentType = ResolveContentType(normalizedBlobPath, contentType);

        try
        {
            await EnsureContainerAsync(cancellationToken);
            var blobClient = _containerClient!.GetBlobClient(normalizedBlobPath);
            var initialPosition = file.CanSeek ? file.Position : 0;

            await blobClient.UploadAsync(
                file,
                new BlobUploadOptions
                {
                    HttpHeaders = new BlobHttpHeaders
                    {
                        ContentType = resolvedContentType
                    }
                },
                cancellationToken);

            var sizeBytes = file.CanSeek ? Math.Max(0, file.Position - initialPosition) : 0;
            _logger.LogInformation(
                "[BlobStorage] Upload succeeded. blobPath={BlobPath}; contentType={ContentType}; sizeBytes={SizeBytes}",
                normalizedBlobPath,
                resolvedContentType,
                sizeBytes);

            return new BlobUploadResult(
                normalizedBlobPath,
                GetPublicUrl(normalizedBlobPath),
                resolvedContentType,
                sizeBytes,
                Uploaded: true,
                Skipped: false);
        }
        catch (Exception exception)
        {
            _logger.LogError(
                exception,
                "[BlobStorage] Upload failed. blobPath={BlobPath}; contentType={ContentType}",
                normalizedBlobPath,
                resolvedContentType);
            throw;
        }
    }

    public async Task<BlobUploadResult> UploadLocalFileAsync(
        string localPath,
        string blobPath,
        string? contentType,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(localPath) || !File.Exists(localPath))
        {
            throw new FileNotFoundException("Local file was not found for Blob upload.", localPath);
        }

        var normalizedBlobPath = NormalizeBlobPath(blobPath);
        var resolvedContentType = ResolveContentType(normalizedBlobPath, contentType);

        if (await ExistsAsync(normalizedBlobPath, cancellationToken))
        {
            var fileInfo = new FileInfo(localPath);
            _logger.LogInformation(
                "[BlobStorage] Upload skipped because blob already exists. localPath={LocalPath}; blobPath={BlobPath}; sizeBytes={SizeBytes}",
                localPath,
                normalizedBlobPath,
                fileInfo.Length);
            return new BlobUploadResult(
                normalizedBlobPath,
                GetPublicUrl(normalizedBlobPath),
                resolvedContentType,
                fileInfo.Length,
                Uploaded: false,
                Skipped: true);
        }

        await using var stream = File.OpenRead(localPath);
        return await UploadAsync(stream, normalizedBlobPath, resolvedContentType, cancellationToken);
    }

    public async Task<bool> DeleteAsync(string blobPath, CancellationToken cancellationToken = default)
    {
        if (!IsConfigured)
        {
            return false;
        }

        var normalizedBlobPath = NormalizeBlobPath(blobPath);
        try
        {
            await EnsureContainerAsync(cancellationToken);
            var response = await _containerClient!.GetBlobClient(normalizedBlobPath)
                .DeleteIfExistsAsync(DeleteSnapshotsOption.IncludeSnapshots, cancellationToken: cancellationToken);
            _logger.LogInformation(
                "[BlobStorage] Delete completed. blobPath={BlobPath}; deleted={Deleted}",
                normalizedBlobPath,
                response.Value);
            return response.Value;
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "[BlobStorage] Delete failed. blobPath={BlobPath}", normalizedBlobPath);
            return false;
        }
    }

    public async Task<bool> ExistsAsync(string blobPath, CancellationToken cancellationToken = default)
    {
        if (!IsConfigured)
        {
            return false;
        }

        var normalizedBlobPath = TryGetBlobPathFromPublicUrl(blobPath) ?? NormalizeBlobPath(blobPath);
        try
        {
            await EnsureContainerAsync(cancellationToken);
            return await _containerClient!.GetBlobClient(normalizedBlobPath).ExistsAsync(cancellationToken);
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "[BlobStorage] Exists check failed. blobPath={BlobPath}", normalizedBlobPath);
            return false;
        }
    }

    public string GetPublicUrl(string blobPath)
    {
        var normalizedBlobPath = NormalizeBlobPath(blobPath);
        if (!string.IsNullOrWhiteSpace(_options.PublicBaseUrl))
        {
            return $"{_options.PublicBaseUrl.TrimEnd('/')}/{Uri.EscapeDataString(normalizedBlobPath).Replace("%2F", "/", StringComparison.OrdinalIgnoreCase)}";
        }

        return _containerClient is null
            ? normalizedBlobPath
            : _containerClient.GetBlobClient(normalizedBlobPath).Uri.ToString();
    }

    public string NormalizeBlobPath(params string?[] segments)
    {
        var normalized = BlobStorageOptions.CombineBlobPath(segments);
        if (string.IsNullOrWhiteSpace(normalized))
        {
            throw new ArgumentException("Blob path cannot be empty.", nameof(segments));
        }

        return normalized;
    }

    public string? TryGetBlobPathFromPublicUrl(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var normalized = value.Trim();
        if (!Uri.TryCreate(normalized, UriKind.Absolute, out var absoluteUri))
        {
            var pathValue = normalized.TrimStart('/').Replace('\\', '/');
            return StartsWithKnownBlobFolder(pathValue) ? NormalizeBlobPath(pathValue) : null;
        }

        var publicBaseUrl = _options.PublicBaseUrl.TrimEnd('/');
        if (!string.IsNullOrWhiteSpace(publicBaseUrl) &&
            Uri.TryCreate(publicBaseUrl, UriKind.Absolute, out var publicBaseUri) &&
            string.Equals(absoluteUri.Host, publicBaseUri.Host, StringComparison.OrdinalIgnoreCase))
        {
            var basePath = publicBaseUri.AbsolutePath.TrimEnd('/');
            var candidatePath = Uri.UnescapeDataString(absoluteUri.AbsolutePath);
            if (!string.IsNullOrWhiteSpace(basePath) &&
                !string.Equals(basePath, "/", StringComparison.Ordinal) &&
                candidatePath.StartsWith(basePath, StringComparison.OrdinalIgnoreCase))
            {
                candidatePath = candidatePath[basePath.Length..];
            }

            var blobPath = candidatePath.TrimStart('/').Replace('\\', '/');
            return string.IsNullOrWhiteSpace(blobPath) ? null : NormalizeBlobPath(blobPath);
        }

        if (_containerClient is not null &&
            string.Equals(absoluteUri.Host, _containerClient.Uri.Host, StringComparison.OrdinalIgnoreCase))
        {
            var containerPath = _containerClient.Uri.AbsolutePath.Trim('/');
            var absolutePath = Uri.UnescapeDataString(absoluteUri.AbsolutePath).Trim('/');
            if (!string.IsNullOrWhiteSpace(containerPath) &&
                absolutePath.StartsWith(containerPath, StringComparison.OrdinalIgnoreCase))
            {
                var blobPath = absolutePath[containerPath.Length..].TrimStart('/');
                return string.IsNullOrWhiteSpace(blobPath) ? null : NormalizeBlobPath(blobPath);
            }
        }

        return null;
    }

    public bool IsBlobUrlOrPath(string? value)
        => TryGetBlobPathFromPublicUrl(value) is not null;

    private async Task EnsureContainerAsync(CancellationToken cancellationToken)
    {
        if (_containerReady)
        {
            return;
        }

        await _containerLock.WaitAsync(cancellationToken);
        try
        {
            if (_containerReady)
            {
                return;
            }

            await _containerClient!.CreateIfNotExistsAsync(
                PublicAccessType.Blob,
                cancellationToken: cancellationToken);
            _containerReady = true;
        }
        catch (RequestFailedException exception)
        {
            _logger.LogError(
                exception,
                "[BlobStorage] Container initialization failed. container={ContainerName}",
                _options.ContainerName);
            throw;
        }
        finally
        {
            _containerLock.Release();
        }
    }

    private void EnsureConfigured()
    {
        if (!IsConfigured)
        {
            throw new InvalidOperationException(
                "BlobStorage:ConnectionString is not configured. Set BlobStorage__ConnectionString in Azure App Service Configuration.");
        }
    }

    private bool StartsWithKnownBlobFolder(string blobPath)
        => blobPath.StartsWith($"{_options.ApkFolder}/", StringComparison.OrdinalIgnoreCase) ||
           blobPath.StartsWith($"{_options.AudioFolder}/", StringComparison.OrdinalIgnoreCase) ||
           blobPath.StartsWith($"{_options.MediaFolder}/", StringComparison.OrdinalIgnoreCase);

    private static string ResolveContentType(string blobPath, string? contentType)
    {
        if (!string.IsNullOrWhiteSpace(contentType) &&
            !string.Equals(contentType.Trim(), "application/octet-stream", StringComparison.OrdinalIgnoreCase))
        {
            return contentType.Trim();
        }

        return Path.GetExtension(blobPath).ToLowerInvariant() switch
        {
            ".apk" => "application/vnd.android.package-archive",
            ".mp3" => "audio/mpeg",
            ".wav" => "audio/wav",
            ".m4a" => "audio/mp4",
            ".jpg" or ".jpeg" => "image/jpeg",
            ".png" => "image/png",
            ".webp" => "image/webp",
            ".gif" => "image/gif",
            ".svg" => "image/svg+xml",
            ".json" => "application/json",
            _ => "application/octet-stream"
        };
    }
}
