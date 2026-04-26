using VinhKhanh.BackendApi.Contracts;
using VinhKhanh.BackendApi.Models;

namespace VinhKhanh.BackendApi.Infrastructure;

public sealed class PoiNarrationService(
    AdminDataRepository repository,
    RuntimeTranslationService runtimeTranslationService,
    IHttpContextAccessor httpContextAccessor,
    ILogger<PoiNarrationService> logger)
{
    private const string StoredStatus = "stored";
    private const string AutoTranslatedStatus = "auto_translated";
    private const string FallbackSourceStatus = "fallback_source";

    public async Task<PoiNarrationResponse?> ResolveAsync(
        string poiId,
        string requestedLanguageCode,
        AdminRequestContext? actor,
        CancellationToken cancellationToken)
    {
        var poi = repository.GetPois(actor)
            .FirstOrDefault(item => string.Equals(item.Id, poiId, StringComparison.OrdinalIgnoreCase));
        if (poi is null)
        {
            return null;
        }

        var settings = repository.GetSettings();
        var normalizedRequestedLanguage = NormalizeLanguageCode(
            requestedLanguageCode,
            settings.DefaultLanguage,
            settings.FallbackLanguage);
        var sourceLanguageCode = NormalizeLanguageCode(
            poi.SourceLanguageCode,
            settings.DefaultLanguage,
            "vi");
        var sourceTitle = CleanNullable(poi.Title) ?? poi.Slug;
        var sourceBody = ResolveSourceNarrationBody(poi);
        var sourceText = BuildNarrationText(sourceTitle, sourceBody);
        var sourceHash = TranslationSourceVersioning.CreateSourceHashForRuntime(
            sourceTitle,
            poi.ShortDescription,
            sourceBody,
            sourceLanguageCode);

        var titleResult = await runtimeTranslationService.TranslateTextAsync(
            "poi",
            poi.Id,
            "title",
            sourceTitle,
            sourceLanguageCode,
            normalizedRequestedLanguage,
            cancellationToken);
        var bodyResult = await runtimeTranslationService.TranslateTextAsync(
            "poi",
            poi.Id,
            "audioScript",
            sourceBody,
            sourceLanguageCode,
            normalizedRequestedLanguage,
            cancellationToken);

        var translationFailedForTarget =
            !string.Equals(sourceLanguageCode, normalizedRequestedLanguage, StringComparison.OrdinalIgnoreCase) &&
            (titleResult.UsedFallback || bodyResult.UsedFallback);
        var translationStatus = string.Equals(sourceLanguageCode, normalizedRequestedLanguage, StringComparison.OrdinalIgnoreCase)
            ? StoredStatus
            : translationFailedForTarget
                ? FallbackSourceStatus
                : AutoTranslatedStatus;
        var effectiveLanguageCode = translationFailedForTarget
            ? sourceLanguageCode
            : normalizedRequestedLanguage;
        var displayTitle = translationFailedForTarget
            ? sourceTitle
            : CleanNullable(titleResult.Text) ?? sourceTitle;
        var displayBody = translationFailedForTarget
            ? sourceBody
            : CleanNullable(bodyResult.Text) ?? string.Empty;
        var displayText = BuildNarrationText(displayTitle, displayBody);
        var translatedText = string.Equals(translationStatus, AutoTranslatedStatus, StringComparison.Ordinal)
            ? displayText
            : null;
        var fallbackMessage = translationFailedForTarget
            ? $"Unable to translate narration to {normalizedRequestedLanguage}. The response falls back to source content in {sourceLanguageCode}."
            : null;

        if (!HasNarrationContent(displayText))
        {
            displayText = string.Empty;
            effectiveLanguageCode = normalizedRequestedLanguage;
        }

        var poiAudioGuides = repository.GetAudioGuides(actor)
            .Where(item =>
                string.Equals(item.EntityType, "poi", StringComparison.OrdinalIgnoreCase) &&
                string.Equals(item.EntityId, poiId, StringComparison.OrdinalIgnoreCase))
            .ToList();
        var selectedAudioGuide = SelectPoiAudioGuide(
            poiAudioGuides,
            normalizedRequestedLanguage,
            effectiveLanguageCode,
            sourceLanguageCode);
        var selectedAudioLanguageCode = NormalizeLanguageCode(
            selectedAudioGuide?.LanguageCode,
            normalizedRequestedLanguage,
            effectiveLanguageCode,
            sourceLanguageCode);
        var foundPreparedAudio = selectedAudioGuide is not null && HasUsablePreparedAudio(selectedAudioGuide);
        var audioGuide = NormalizeAudioGuideForResponse(
            selectedAudioGuide);
        var uiPlaybackKey = BuildUiPlaybackKey(poi.Id, normalizedRequestedLanguage);
        var audioCacheKey = BuildAudioCacheKey(
            uiPlaybackKey,
            translationStatus,
            effectiveLanguageCode,
            sourceLanguageCode,
            sourceHash,
            displayText,
            audioGuide);

        var resolved = new PoiNarrationResponse(
            poi.Id,
            normalizedRequestedLanguage,
            sourceLanguageCode,
            effectiveLanguageCode,
            displayTitle,
            displayText,
            displayText,
            sourceText,
            translatedText,
            translationStatus,
            fallbackMessage,
            audioGuide,
            uiPlaybackKey,
            audioCacheKey,
            GetLocale(effectiveLanguageCode));

        logger.LogInformation(
            "[AudioRequest] poiId={PoiId}; requestedLang={RequestedLanguage}; normalizedLang={NormalizedLanguage}; effectiveLanguage={EffectiveLanguage}; selectedAudioLanguage={SelectedAudioLanguage}; foundAudio={FoundAudio}; audioGuideId={AudioGuideId}; audioGuideLanguage={AudioGuideLanguage}; generationStatus={GenerationStatus}; publicStatus={PublicStatus}; audioUrl={AudioUrl}; audioFilePath={AudioFilePath}; candidateCount={CandidateCount}",
            poi.Id,
            requestedLanguageCode,
            normalizedRequestedLanguage,
            effectiveLanguageCode,
            selectedAudioLanguageCode,
            foundPreparedAudio,
            selectedAudioGuide?.Id,
            selectedAudioGuide?.LanguageCode,
            selectedAudioGuide?.GenerationStatus,
            selectedAudioGuide?.Status,
            selectedAudioGuide?.AudioUrl,
            selectedAudioGuide?.AudioFilePath,
            poiAudioGuides.Count);

        return resolved;
    }

    private static AudioGuide? SelectPoiAudioGuide(
        IReadOnlyList<AudioGuide> audioGuides,
        string requestedLanguageCode,
        string effectiveLanguageCode,
        string sourceLanguageCode)
    {
        var candidateLanguages = new List<string>();
        AddLanguageCandidate(candidateLanguages, requestedLanguageCode);
        AddLanguageCandidate(candidateLanguages, effectiveLanguageCode);
        AddLanguageCandidate(candidateLanguages, sourceLanguageCode);

        foreach (var candidateLanguage in candidateLanguages)
        {
            var candidateGuide = FindPoiAudioGuide(audioGuides, candidateLanguage);
            if (candidateGuide is not null && IsPlayableAudioGuide(candidateGuide))
            {
                return candidateGuide;
            }
        }

        foreach (var candidateLanguage in candidateLanguages)
        {
            var candidateGuide = FindPoiAudioGuide(audioGuides, candidateLanguage);
            if (candidateGuide is not null)
            {
                return candidateGuide;
            }
        }

        return AudioGuideCatalog.SelectCanonical(audioGuides);
    }

    private static AudioGuide? FindPoiAudioGuide(
        IEnumerable<AudioGuide> audioGuides,
        string languageCode)
    {
        var matchingGuides = audioGuides
            .Where(item => PremiumAccessCatalog.LanguageCodesMatch(item.LanguageCode, languageCode))
            .ToList();

        return AudioGuideCatalog.OrderByCanonicalPreference(matchingGuides).FirstOrDefault();
    }

    private static bool IsPlayableAudioGuide(AudioGuide audioGuide) =>
        HasUsablePreparedAudio(audioGuide) &&
        !IsPlaceholderAudioUrl(audioGuide.AudioUrl);

    private static bool HasUsablePreparedAudio(AudioGuide audioGuide) =>
        !audioGuide.IsOutdated &&
        string.Equals(AudioGuideCatalog.NormalizeGenerationStatus(audioGuide.GenerationStatus), AudioGuideCatalog.GenerationStatusSuccess, StringComparison.OrdinalIgnoreCase) &&
        string.Equals(AudioGuideCatalog.NormalizePublicStatus(audioGuide.Status), AudioGuideCatalog.PublicStatusReady, StringComparison.OrdinalIgnoreCase) &&
        (HasValidAudioUrl(audioGuide.AudioUrl) || !string.IsNullOrWhiteSpace(audioGuide.AudioFilePath));

    private static bool HasValidAudioUrl(string? value) =>
        !string.IsNullOrWhiteSpace(value);

    private static bool IsPlaceholderAudioUrl(string? value)
    {
        if (!HasValidAudioUrl(value))
        {
            return false;
        }

        return Uri.TryCreate(value, UriKind.Absolute, out var parsed) &&
            parsed.Host.Contains("example.com", StringComparison.OrdinalIgnoreCase);
    }

    private static string ResolveSourceNarrationBody(Poi poi)
    {
        var audioScript = CleanNullable(poi.AudioScript);
        if (!string.IsNullOrWhiteSpace(audioScript))
        {
            return audioScript;
        }

        var description = CleanNullable(poi.Description);
        if (!string.IsNullOrWhiteSpace(description))
        {
            return description;
        }

        return CleanNullable(poi.ShortDescription) ?? string.Empty;
    }

    private static string BuildNarrationText(string? title, string? body)
    {
        var normalizedTitle = CleanNullable(title);
        var normalizedBody = CleanNullable(body);

        if (!HasNarrationContent(normalizedTitle) && !HasNarrationContent(normalizedBody))
        {
            return string.Empty;
        }

        return HasNarrationContent(normalizedBody)
            ? normalizedBody!
            : normalizedTitle ?? string.Empty;
    }

    private static string BuildUiPlaybackKey(string poiId, string languageCode) =>
        $"{poiId}:{languageCode}";

    private static string BuildAudioCacheKey(
        string uiPlaybackKey,
        string translationStatus,
        string effectiveLanguageCode,
        string? sourceLanguageCode,
        string sourceVersion,
        string ttsInputText,
        AudioGuide? audioGuide) =>
        string.Join(
            "|",
            uiPlaybackKey,
            $"status={translationStatus}",
            $"effective={effectiveLanguageCode}",
            $"source={sourceLanguageCode ?? "none"}",
            $"sourceVersion={sourceVersion}",
            $"text={CreateHash(ttsInputText)}",
            $"guide={audioGuide?.Id ?? "none"}",
            $"guideUpdated={audioGuide?.UpdatedAt.ToString("O") ?? "none"}",
            $"guideUrl={CreateHash((audioGuide?.AudioUrl ?? audioGuide?.AudioFilePath ?? string.Empty).Trim())}");

    private static string CreateHash(string value)
    {
        uint hash = 0;

        foreach (var character in value)
        {
            hash = (hash * 31) + character;
        }

        return hash.ToString("x8");
    }

    private static string NormalizeLanguageCode(string? primary, params string?[] fallbacks)
    {
        foreach (var candidate in new[] { primary }.Concat(fallbacks))
        {
            var normalized = PremiumAccessCatalog.NormalizeLanguageCode(candidate);
            if (!string.IsNullOrWhiteSpace(normalized))
            {
                return normalized;
            }
        }

        return "vi";
    }

    private static void AddLanguageCandidate(ICollection<string> candidates, string? languageCode)
    {
        var normalizedLanguageCode = PremiumAccessCatalog.NormalizeLanguageCode(languageCode);
        if (string.IsNullOrWhiteSpace(normalizedLanguageCode) ||
            candidates.Any(candidate => string.Equals(candidate, normalizedLanguageCode, StringComparison.OrdinalIgnoreCase)))
        {
            return;
        }

        candidates.Add(normalizedLanguageCode);
    }

    private static string GetLocale(string languageCode)
        => LanguageRegistry.GetLocale(languageCode);

    private AudioGuide? NormalizeAudioGuideForResponse(AudioGuide? audioGuide)
    {
        if (audioGuide is null)
        {
            return audioGuide;
        }

        if (!HasUsablePreparedAudio(audioGuide))
        {
            return new AudioGuide
            {
                Id = audioGuide.Id,
                EntityType = audioGuide.EntityType,
                EntityId = audioGuide.EntityId,
                LanguageCode = audioGuide.LanguageCode,
                TranscriptText = audioGuide.TranscriptText,
                AudioUrl = string.Empty,
                AudioFilePath = audioGuide.AudioFilePath,
                AudioFileName = audioGuide.AudioFileName,
                VoiceType = audioGuide.VoiceType,
                SourceType = audioGuide.SourceType,
                Provider = audioGuide.Provider,
                VoiceId = audioGuide.VoiceId,
                ModelId = audioGuide.ModelId,
                OutputFormat = audioGuide.OutputFormat,
                DurationInSeconds = audioGuide.DurationInSeconds,
                FileSizeBytes = audioGuide.FileSizeBytes,
                TextHash = audioGuide.TextHash,
                ContentVersion = audioGuide.ContentVersion,
                GeneratedAt = audioGuide.GeneratedAt,
                GenerationStatus = audioGuide.GenerationStatus,
                ErrorMessage = audioGuide.ErrorMessage,
                IsOutdated = audioGuide.IsOutdated,
                Status = audioGuide.Status,
                UpdatedBy = audioGuide.UpdatedBy,
                UpdatedAt = audioGuide.UpdatedAt
            };
        }

        var audioUrl = HasValidAudioUrl(audioGuide.AudioUrl)
            ? audioGuide.AudioUrl
            : audioGuide.AudioFilePath;
        var absoluteUrl = BuildAbsoluteUrl(audioUrl);
        if (string.Equals(absoluteUrl, audioGuide.AudioUrl, StringComparison.Ordinal))
        {
            return audioGuide;
        }

        return new AudioGuide
        {
            Id = audioGuide.Id,
            EntityType = audioGuide.EntityType,
            EntityId = audioGuide.EntityId,
            LanguageCode = audioGuide.LanguageCode,
            TranscriptText = audioGuide.TranscriptText,
            AudioUrl = absoluteUrl,
            AudioFilePath = audioGuide.AudioFilePath,
            AudioFileName = audioGuide.AudioFileName,
            VoiceType = audioGuide.VoiceType,
            SourceType = audioGuide.SourceType,
            Provider = audioGuide.Provider,
            VoiceId = audioGuide.VoiceId,
            ModelId = audioGuide.ModelId,
            OutputFormat = audioGuide.OutputFormat,
            DurationInSeconds = audioGuide.DurationInSeconds,
            FileSizeBytes = audioGuide.FileSizeBytes,
            TextHash = audioGuide.TextHash,
            ContentVersion = audioGuide.ContentVersion,
            GeneratedAt = audioGuide.GeneratedAt,
            GenerationStatus = audioGuide.GenerationStatus,
            ErrorMessage = audioGuide.ErrorMessage,
            IsOutdated = audioGuide.IsOutdated,
            Status = audioGuide.Status,
            UpdatedBy = audioGuide.UpdatedBy,
            UpdatedAt = audioGuide.UpdatedAt
        };
    }

    private string BuildAbsoluteUrl(string value)
    {
        if (!HasValidAudioUrl(value) || Uri.TryCreate(value, UriKind.Absolute, out _))
        {
            return value;
        }

        var request = httpContextAccessor.HttpContext?.Request;
        if (request is null || string.IsNullOrWhiteSpace(request.Host.Value))
        {
            return value;
        }

        var normalizedPath = value.StartsWith("/", StringComparison.Ordinal) ? value : $"/{value}";
        return $"{request.Scheme}://{request.Host}{request.PathBase}{normalizedPath}";
    }

    private static bool HasNarrationContent(string? value) =>
        !string.IsNullOrWhiteSpace(value);

    private static string? CleanNullable(string? value)
    {
        var cleaned = NarrationTextSanitizer.Clean(value);
        return string.IsNullOrWhiteSpace(cleaned) ? null : cleaned;
    }
}
