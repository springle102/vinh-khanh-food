using System.Text.RegularExpressions;
using VinhKhanh.BackendApi.Contracts;

namespace VinhKhanh.BackendApi.Infrastructure;

public sealed class StorageService(IWebHostEnvironment environment)
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
        var relativeFolder = Path.Combine("storage", normalizedFolder);
        var targetFolder = Path.Combine(environment.WebRootPath ?? Path.Combine(environment.ContentRootPath, "wwwroot"), relativeFolder);

        Directory.CreateDirectory(targetFolder);

        var extension = Path.GetExtension(file.FileName);
        var fileName = $"{DateTimeOffset.UtcNow:yyyyMMddHHmmssfff}-{Guid.NewGuid():N}{extension}";
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
            file.Length);
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
