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

    public static AudioGuide? SelectCanonical(IEnumerable<AudioGuide> audioGuides)
        => OrderByCanonicalPreference(audioGuides).FirstOrDefault();

    public static IReadOnlyList<AudioGuide> OrderByCanonicalPreference(IEnumerable<AudioGuide> audioGuides)
    {
        ArgumentNullException.ThrowIfNull(audioGuides);

        return audioGuides
            .Where(audioGuide => audioGuide is not null)
            .OrderByDescending(GetCanonicalPreferenceScore)
            .ThenByDescending(audioGuide => audioGuide.GeneratedAt ?? DateTimeOffset.MinValue)
            .ThenByDescending(audioGuide => audioGuide.UpdatedAt)
            .ThenByDescending(audioGuide => audioGuide.FileSizeBytes ?? 0L)
            .ThenByDescending(audioGuide => audioGuide.Id, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public static string ResolvePublicStatus(string generationStatus, bool hasPlaybackAsset, bool isOutdated)
    {
        if (isOutdated || string.Equals(generationStatus, GenerationStatusOutdated, StringComparison.OrdinalIgnoreCase))
        {
            return PublicStatusMissing;
        }

        if (string.Equals(generationStatus, GenerationStatusPending, StringComparison.OrdinalIgnoreCase))
        {
            return PublicStatusProcessing;
        }

        return string.Equals(generationStatus, GenerationStatusSuccess, StringComparison.OrdinalIgnoreCase) && hasPlaybackAsset
            ? PublicStatusReady
            : PublicStatusMissing;
    }

    private static int GetCanonicalPreferenceScore(AudioGuide audioGuide)
    {
        var score = 0;
        var generationStatus = NormalizeGenerationStatus(audioGuide.GenerationStatus);
        var publicStatus = NormalizePublicStatus(audioGuide.Status);

        if (IsReadyForPlayback(audioGuide))
        {
            score += 1_000;
        }

        if (string.Equals(generationStatus, GenerationStatusSuccess, StringComparison.OrdinalIgnoreCase))
        {
            score += 250;
        }

        if (string.Equals(publicStatus, PublicStatusReady, StringComparison.OrdinalIgnoreCase))
        {
            score += 150;
        }

        if (!audioGuide.IsOutdated)
        {
            score += 120;
        }

        if (!string.IsNullOrWhiteSpace(audioGuide.AudioFilePath))
        {
            score += 80;
        }

        if (!string.IsNullOrWhiteSpace(audioGuide.AudioUrl))
        {
            score += 40;
        }

        if (!string.IsNullOrWhiteSpace(audioGuide.ContentVersion))
        {
            score += 20;
        }

        if (!string.IsNullOrWhiteSpace(audioGuide.TextHash))
        {
            score += 10;
        }

        if (audioGuide.GeneratedAt.HasValue)
        {
            score += 5;
        }

        return score;
    }
}
