using System.Text.Json;
using VinhKhanh.MobileApp.Models;

namespace VinhKhanh.MobileApp.Helpers;

public static class OfflinePackageIntegrityHelper
{
    public static OfflinePackageVerificationResult VerifyInstallation(OfflinePackageInstallation? installation)
    {
        var result = new OfflinePackageVerificationResult();
        if (installation is null)
        {
            result.Summary = "No offline package is installed.";
            result.Problems = ["installation_missing"];
            return result;
        }

        var problems = new List<string>();
        var manifestFiles = installation.Manifest.Files ?? [];
        result.ManifestFileCount = manifestFiles.Count;

        if (string.IsNullOrWhiteSpace(installation.BootstrapEnvelopeJson))
        {
            problems.Add("bootstrap_missing");
        }

        if (manifestFiles.Count == 0)
        {
            problems.Add("manifest_has_no_files");
        }

        if (installation.Metadata.FileCount > 0 &&
            installation.Metadata.FileCount != manifestFiles.Count)
        {
            problems.Add($"manifest_count_mismatch:{installation.Metadata.FileCount}:{manifestFiles.Count}");
        }

        PopulateBootstrapCounts(installation.BootstrapEnvelopeJson, result, problems);

        var audioEntries = manifestFiles
            .Where(file => string.Equals(file.Kind, "audio", StringComparison.OrdinalIgnoreCase))
            .ToArray();
        var imageEntries = manifestFiles
            .Where(file => string.Equals(file.Kind, "image", StringComparison.OrdinalIgnoreCase))
            .ToArray();

        if (installation.Metadata.AudioCount > 0 &&
            installation.Metadata.AudioCount != audioEntries.Length)
        {
            problems.Add($"audio_manifest_count_mismatch:{installation.Metadata.AudioCount}:{audioEntries.Length}");
        }

        if (installation.Metadata.ImageCount > 0 &&
            installation.Metadata.ImageCount != imageEntries.Length)
        {
            problems.Add($"image_manifest_count_mismatch:{installation.Metadata.ImageCount}:{imageEntries.Length}");
        }

        if (result.BootstrapAudioGuideCount > 0 &&
            audioEntries.Length < result.BootstrapAudioGuideCount)
        {
            problems.Add($"bootstrap_audio_not_fully_packaged:{result.BootstrapAudioGuideCount}:{audioEntries.Length}");
        }

        foreach (var entry in manifestFiles)
        {
            if (!TryResolveFilePath(installation, entry, out var filePath) ||
                !File.Exists(filePath))
            {
                result.MissingFileCount++;
                problems.Add($"file_missing:{entry.Kind}:{entry.RelativePath}");
                continue;
            }

            if (string.Equals(entry.Kind, "audio", StringComparison.OrdinalIgnoreCase) &&
                !IsUsableAudioFile(filePath))
            {
                result.InvalidAudioFileCount++;
                problems.Add($"audio_invalid:{entry.RelativePath}");
                continue;
            }

            if (string.Equals(entry.Kind, "image", StringComparison.OrdinalIgnoreCase))
            {
                var fileInfo = new FileInfo(filePath);
                if (fileInfo.Length <= 0)
                {
                    result.InvalidImageFileCount++;
                    problems.Add($"image_invalid:{entry.RelativePath}");
                }
            }
        }

        result.IsValid =
            problems.Count == 0 &&
            result.MissingFileCount == 0 &&
            result.InvalidAudioFileCount == 0 &&
            result.InvalidImageFileCount == 0;
        result.Summary = result.IsValid
            ? $"verified:{result.ManifestFileCount}:files:{result.BootstrapPoiCount}:pois:{result.BootstrapAudioGuideCount}:audio"
            : $"invalid:{result.MissingFileCount}:missing:{result.InvalidAudioFileCount}:audio:{result.InvalidImageFileCount}:image";
        result.Problems = problems;
        return result;
    }

    private static void PopulateBootstrapCounts(
        string bootstrapEnvelopeJson,
        OfflinePackageVerificationResult result,
        ICollection<string> problems)
    {
        if (string.IsNullOrWhiteSpace(bootstrapEnvelopeJson))
        {
            return;
        }

        try
        {
            using var document = JsonDocument.Parse(bootstrapEnvelopeJson);
            if (!TryReadDataElement(document.RootElement, out var dataElement))
            {
                problems.Add("bootstrap_data_missing");
                return;
            }

            result.BootstrapPoiCount = GetArrayCount(dataElement, "pois");
            result.BootstrapRouteCount = GetArrayCount(dataElement, "routes");
            result.BootstrapAudioGuideCount = CountPlayableAudioGuides(dataElement);

            if (result.BootstrapPoiCount <= 0)
            {
                problems.Add("bootstrap_has_no_pois");
            }
        }
        catch (JsonException)
        {
            problems.Add("bootstrap_invalid_json");
        }
    }

    private static int CountPlayableAudioGuides(JsonElement dataElement)
    {
        if (!dataElement.TryGetProperty("audioGuides", out var audioGuidesElement) ||
            audioGuidesElement.ValueKind != JsonValueKind.Array)
        {
            return 0;
        }

        var count = 0;
        foreach (var audioGuideElement in audioGuidesElement.EnumerateArray())
        {
            if (IsPlayableOfflineAudioGuide(audioGuideElement))
            {
                count++;
            }
        }

        return count;
    }

    private static bool TryResolveFilePath(
        OfflinePackageInstallation installation,
        OfflinePackageFileEntry entry,
        out string filePath)
    {
        filePath = string.Empty;
        foreach (var key in OfflineAssetUrlHelper.BuildLookupKeys(entry.Key)
                     .Concat(OfflineAssetUrlHelper.BuildLookupKeys(entry.RelativePath)))
        {
            if (installation.AssetMap.TryGetValue(key, out var mappedPath) &&
                !string.IsNullOrWhiteSpace(mappedPath))
            {
                filePath = mappedPath;
                return true;
            }
        }

        return false;
    }

    private static bool TryReadDataElement(JsonElement rootElement, out JsonElement dataElement)
    {
        if (rootElement.ValueKind == JsonValueKind.Object &&
            rootElement.TryGetProperty("data", out dataElement) &&
            dataElement.ValueKind == JsonValueKind.Object)
        {
            return true;
        }

        dataElement = default;
        return false;
    }

    private static int GetArrayCount(JsonElement element, string arrayPropertyName)
        => element.TryGetProperty(arrayPropertyName, out var arrayElement) &&
           arrayElement.ValueKind == JsonValueKind.Array
            ? arrayElement.GetArrayLength()
            : 0;

    private static bool IsPlayableOfflineAudioGuide(JsonElement audioGuideElement)
    {
        var audioUrl = GetString(audioGuideElement, "audioUrl");
        if (string.IsNullOrWhiteSpace(audioUrl) ||
            !string.Equals(GetString(audioGuideElement, "status"), "ready", StringComparison.OrdinalIgnoreCase) ||
            GetBoolean(audioGuideElement, "isOutdated"))
        {
            return false;
        }

        var sourceType = GetString(audioGuideElement, "sourceType");
        var generationStatus = NormalizeGenerationStatus(GetString(audioGuideElement, "generationStatus"));
        if (generationStatus is "failed" or "outdated" or "pending")
        {
            return false;
        }

        return !string.Equals(sourceType, "generated", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(generationStatus, "success", StringComparison.OrdinalIgnoreCase);
    }

    private static string GetString(JsonElement element, string propertyName)
        => element.TryGetProperty(propertyName, out var propertyValue) &&
           propertyValue.ValueKind == JsonValueKind.String
            ? propertyValue.GetString()?.Trim() ?? string.Empty
            : string.Empty;

    private static bool GetBoolean(JsonElement element, string propertyName)
        => element.TryGetProperty(propertyName, out var propertyValue) &&
           propertyValue.ValueKind is JsonValueKind.True or JsonValueKind.False &&
           propertyValue.GetBoolean();

    private static string NormalizeGenerationStatus(string? generationStatus)
        => string.IsNullOrWhiteSpace(generationStatus)
            ? "none"
            : generationStatus.Trim().ToLowerInvariant();

    public static bool IsUsableAudioFile(string filePath)
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
}
