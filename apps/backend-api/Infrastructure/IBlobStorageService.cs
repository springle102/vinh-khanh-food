namespace VinhKhanh.BackendApi.Infrastructure;

public interface IBlobStorageService
{
    bool IsConfigured { get; }

    Task<BlobUploadResult> UploadAsync(
        Stream file,
        string blobPath,
        string? contentType,
        CancellationToken cancellationToken = default);

    Task<BlobUploadResult> UploadLocalFileAsync(
        string localPath,
        string blobPath,
        string? contentType,
        CancellationToken cancellationToken = default);

    Task<bool> DeleteAsync(string blobPath, CancellationToken cancellationToken = default);

    Task<bool> ExistsAsync(string blobPath, CancellationToken cancellationToken = default);

    string GetPublicUrl(string blobPath);

    string NormalizeBlobPath(params string?[] segments);

    string? TryGetBlobPathFromPublicUrl(string? value);

    bool IsBlobUrlOrPath(string? value);
}

public sealed record BlobUploadResult(
    string BlobPath,
    string PublicUrl,
    string ContentType,
    long SizeBytes,
    bool Uploaded,
    bool Skipped);
