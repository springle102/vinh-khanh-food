using VinhKhanh.BackendApi.Models;

namespace VinhKhanh.BackendApi.Infrastructure;

public static class AudioGuideCatalog
{
    public const string SourceTypeUploaded = "uploaded";
    public const string SourceTypeGenerated = "generated";
    public const string SourceTypeLegacyTts = "tts";

    public const string ProviderElevenLabs = "elevenlabs";

    public const string PublicStatusReady = "ready";
    public const string PublicStatusProcessing = "processing";
    public const string PublicStatusMissing = "missing";

    public const string GenerationStatusNone = "none";
    public const string GenerationStatusPending = "pending";
    public const string GenerationStatusSuccess = "success";
    public const string GenerationStatusFailed = "failed";
    public const string GenerationStatusOutdated = "outdated";

    public static string NormalizeSourceType(string? value)
    {
        var normalized = value?.Trim().ToLowerInvariant();
        return normalized switch
        {
            SourceTypeUploaded => SourceTypeUploaded,
            SourceTypeGenerated => SourceTypeGenerated,
            "pregenerated" => SourceTypeGenerated,
            "pre_generated" => SourceTypeGenerated,
            SourceTypeLegacyTts => SourceTypeGenerated,
            _ => SourceTypeGenerated
        };
    }

    public static string NormalizeGenerationStatus(string? value)
    {
        var normalized = value?.Trim().ToLowerInvariant();
        return normalized switch
        {
            GenerationStatusPending => GenerationStatusPending,
            GenerationStatusSuccess => GenerationStatusSuccess,
            GenerationStatusFailed => GenerationStatusFailed,
            GenerationStatusOutdated => GenerationStatusOutdated,
            _ => GenerationStatusNone
        };
    }

    public static string NormalizePublicStatus(string? value)
    {
        var normalized = value?.Trim().ToLowerInvariant();
        return normalized switch
        {
            PublicStatusReady => PublicStatusReady,
            PublicStatusProcessing => PublicStatusProcessing,
            _ => PublicStatusMissing
        };
    }

    public static string NormalizeProvider(string? value)
        => string.IsNullOrWhiteSpace(value)
            ? ProviderElevenLabs
            : value.Trim().ToLowerInvariant();

    public static bool IsReadyForPlayback(AudioGuide? audioGuide)
        => audioGuide is not null &&
           !audioGuide.IsOutdated &&
           string.Equals(NormalizeGenerationStatus(audioGuide.GenerationStatus), GenerationStatusSuccess, StringComparison.OrdinalIgnoreCase) &&
           string.Equals(NormalizePublicStatus(audioGuide.Status), PublicStatusReady, StringComparison.OrdinalIgnoreCase) &&
           (!string.IsNullOrWhiteSpace(audioGuide.AudioUrl) || !string.IsNullOrWhiteSpace(audioGuide.AudioFilePath));

    public static string ResolvePublicStatus(string generationStatus, bool hasAudioUrl, bool isOutdated)
    {
        if (isOutdated || string.Equals(generationStatus, GenerationStatusOutdated, StringComparison.OrdinalIgnoreCase))
        {
            return PublicStatusMissing;
        }

        if (string.Equals(generationStatus, GenerationStatusPending, StringComparison.OrdinalIgnoreCase))
        {
            return PublicStatusProcessing;
        }

        return string.Equals(generationStatus, GenerationStatusSuccess, StringComparison.OrdinalIgnoreCase) && hasAudioUrl
            ? PublicStatusReady
            : PublicStatusMissing;
    }
}
