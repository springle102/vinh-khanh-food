using VinhKhanh.BackendApi.Contracts;
using VinhKhanh.BackendApi.Models;

namespace VinhKhanh.BackendApi.Infrastructure;

public sealed class PoiNarrationService(
    AdminDataRepository repository,
    TranslationProxyService translationProxyService,
    IHttpContextAccessor httpContextAccessor,
    ILogger<PoiNarrationService> logger)
{
    private static readonly Dictionary<string, string> LanguageLocales = new(StringComparer.OrdinalIgnoreCase)
    {
        ["vi"] = "vi-VN",
        ["en"] = "en-US",
        ["fr"] = "fr-FR",
        ["zh-CN"] = "zh-CN",
        ["ko"] = "ko-KR",
        ["ja"] = "ja-JP"
    };

    private static readonly Dictionary<string, string> LanguageLabels = new(StringComparer.OrdinalIgnoreCase)
    {
        ["vi"] = "Vietnamese",
        ["en"] = "English",
        ["fr"] = "French",
        ["zh-CN"] = "Chinese",
        ["ko"] = "Korean",
        ["ja"] = "Japanese"
    };

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
        var translations = repository.GetTranslations()
            .Where(item =>
                string.Equals(item.EntityType, "poi", StringComparison.OrdinalIgnoreCase) &&
                string.Equals(item.EntityId, poiId, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(item => item.UpdatedAt)
            .ToList();
        var audioGuides = repository.GetAudioGuides()
            .Where(item =>
                string.Equals(item.EntityType, "poi", StringComparison.OrdinalIgnoreCase) &&
                string.Equals(item.EntityId, poiId, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(item => item.UpdatedAt)
            .ToList();

        var sourceSnapshot = TranslationSourceVersioning.ResolveCurrentSource(
            "poi",
            poiId,
            translations,
            settings.DefaultLanguage,
            settings.FallbackLanguage,
            poi.Slug,
            null,
            null,
            poi.UpdatedAt);
        var exactTranslation = FindExactPoiTranslation(translations, normalizedRequestedLanguage);
        var exactTranslationIsFresh = TranslationSourceVersioning.MatchesCurrentSource(exactTranslation, sourceSnapshot);
        var exactTitle = exactTranslationIsFresh
            ? CleanNullableForLanguage(exactTranslation?.Title, normalizedRequestedLanguage)
            : null;
        var exactBody = exactTranslationIsFresh
            ? GetNarrationBodyForLanguage(exactTranslation, normalizedRequestedLanguage)
            : string.Empty;
        var exactText = BuildNarrationText(exactTitle, exactBody);
        var sourceLanguageCode = sourceSnapshot?.LanguageCode;
        var sourceTitle = sourceSnapshot?.Title;
        var sourceBody = sourceSnapshot?.Body ?? string.Empty;
        var sourceText = BuildNarrationText(sourceTitle, sourceBody);
        var hasExactNarration = HasNarrationContent(exactTitle) || HasNarrationContent(exactBody);
        var hasSourceNarration = HasNarrationContent(sourceTitle) || HasNarrationContent(sourceBody);
        var shouldAutoTranslateFromSource =
            hasSourceNarration &&
            !string.Equals(sourceLanguageCode, normalizedRequestedLanguage, StringComparison.OrdinalIgnoreCase) &&
            (!exactTranslationIsFresh ||
             !hasExactNarration ||
             IsEquivalentNarration(exactBody, sourceBody));

        var displayTitle = exactTitle ??
            (string.Equals(sourceLanguageCode, normalizedRequestedLanguage, StringComparison.OrdinalIgnoreCase)
                ? sourceTitle
                : null) ??
            poi.Slug;
        var displayText = hasExactNarration
            ? exactText
            : string.Empty;
        var ttsInputText = displayText;
        string? translatedText = null;
        const string storedStatus = "stored";
        const string autoTranslatedStatus = "auto_translated";
        const string fallbackSourceStatus = "fallback_source";
        var translationStatus = storedStatus;
        var effectiveLanguageCode = normalizedRequestedLanguage;
        string? fallbackMessage = null;
        var sourceHash = sourceSnapshot?.Hash ?? string.Empty;
        var translationCacheStatus = sourceSnapshot is null
            ? "no_source"
            : exactTranslationIsFresh
                ? "hit"
                : "miss";

        if (shouldAutoTranslateFromSource)
        {
            try
            {
                var translatedFragments = await TranslateNarrationAsync(
                    sourceTitle,
                    sourceBody,
                    sourceLanguageCode!,
                    normalizedRequestedLanguage,
                    cancellationToken);

                displayTitle = translatedFragments.Title ?? displayTitle;
                displayText = BuildNarrationText(translatedFragments.Title, translatedFragments.Body);
                ttsInputText = displayText;
                translatedText = displayText;
                translationStatus = autoTranslatedStatus;
                translationCacheStatus = "miss";

                if (!HasNarrationContent(displayText))
                {
                    throw new InvalidOperationException("The translated narration was empty.");
                }
            }
            catch (Exception exception) when (
                !cancellationToken.IsCancellationRequested &&
                (exception is InvalidOperationException ||
                exception is HttpRequestException ||
                exception is TaskCanceledException))
            {
                logger.LogWarning(
                    exception,
                    "Failed to auto-translate narration for POI {PoiId} from {SourceLanguageCode} to {TargetLanguageCode}.",
                    poiId,
                    sourceLanguageCode,
                    normalizedRequestedLanguage);

                if (hasExactNarration && exactTranslationIsFresh && !IsEquivalentNarration(exactBody, sourceBody))
                {
                    displayTitle = exactTitle ?? poi.Slug;
                    displayText = exactText;
                    ttsInputText = exactText;
                    translationStatus = storedStatus;
                    fallbackMessage = BuildStoredTranslationFallbackMessage(
                        exception,
                        normalizedRequestedLanguage);
                }
                else
                {
                    var canUseSourceFallback =
                        string.Equals(sourceLanguageCode, normalizedRequestedLanguage, StringComparison.OrdinalIgnoreCase) ||
                        (!IsSourceLanguage(sourceLanguageCode) && IsUsableTextForLanguage(sourceText, sourceLanguageCode));
                    displayTitle = canUseSourceFallback
                        ? sourceTitle ?? poi.Slug
                        : exactTitle ?? poi.Slug;
                    displayText = canUseSourceFallback ? sourceText : string.Empty;
                    ttsInputText = displayText;
                    effectiveLanguageCode = canUseSourceFallback
                        ? sourceLanguageCode!
                        : normalizedRequestedLanguage;
                    translationStatus = canUseSourceFallback ? fallbackSourceStatus : storedStatus;
                    fallbackMessage = BuildSourceFallbackMessage(
                        exception,
                        normalizedRequestedLanguage,
                        sourceLanguageCode);
                }
            }
        }
        else if (!hasExactNarration && hasSourceNarration && sourceSnapshot is not null)
        {
            var canUseSourceFallback =
                string.Equals(sourceLanguageCode, normalizedRequestedLanguage, StringComparison.OrdinalIgnoreCase) ||
                (!IsSourceLanguage(sourceLanguageCode) && IsUsableTextForLanguage(sourceText, sourceLanguageCode));
            displayTitle = canUseSourceFallback
                ? sourceTitle ?? poi.Slug
                : exactTitle ?? poi.Slug;
            displayText = canUseSourceFallback ? sourceText : string.Empty;
            ttsInputText = displayText;
            effectiveLanguageCode = canUseSourceFallback
                ? sourceLanguageCode ?? normalizedRequestedLanguage
                : normalizedRequestedLanguage;
        }

        if (!HasNarrationContent(ttsInputText))
        {
            displayText = string.Empty;
            ttsInputText = string.Empty;
            effectiveLanguageCode = normalizedRequestedLanguage;
        }

        AudioGuide? audioGuide = null;
        if (!string.Equals(translationStatus, autoTranslatedStatus, StringComparison.Ordinal))
        {
            audioGuide = FindPoiAudioGuide(audioGuides, effectiveLanguageCode);
        }

        audioGuide = NormalizeAudioGuideForResponse(audioGuide);

        var uiPlaybackKey = BuildUiPlaybackKey(poi.Id, normalizedRequestedLanguage);
        var audioCacheKey = BuildAudioCacheKey(
            uiPlaybackKey,
            translationStatus,
            effectiveLanguageCode,
            sourceLanguageCode,
            sourceHash,
            ttsInputText,
            audioGuide);
        var sourceTextForResponse =
            string.Equals(translationStatus, autoTranslatedStatus, StringComparison.Ordinal) ||
            string.Equals(translationStatus, fallbackSourceStatus, StringComparison.Ordinal)
                ? sourceText
                : HasNarrationContent(exactText)
                    ? exactText
                    : sourceText;
        var resolved = new PoiNarrationResponse(
            poi.Id,
            normalizedRequestedLanguage,
            sourceLanguageCode,
            effectiveLanguageCode,
            displayTitle,
            displayText,
            ttsInputText,
            sourceTextForResponse,
            translatedText,
            translationStatus,
            fallbackMessage,
            audioGuide,
            uiPlaybackKey,
            audioCacheKey,
            GetLocale(effectiveLanguageCode));

        logger.LogDebug(
            "Resolved POI narration. PoiId={PoiId}; languageSelected={LanguageSelected}; dbSourceTitle={DbSourceTitle}; translatorInputTitle={TranslatorInputTitle}; translationCache={TranslationCache}; sourceHash={SourceHash}; sourceLanguage={SourceLanguage}; finalTitle={FinalTitle}; sourceText={SourceText}; translatedText={TranslatedText}; ttsInputText={TtsInputText}; cacheKey={CacheKey}; status={Status}; fallbackMessage={FallbackMessage}",
            poi.Id,
            normalizedRequestedLanguage,
            sourceTitle,
            shouldAutoTranslateFromSource ? sourceTitle : null,
            translationCacheStatus,
            sourceHash,
            sourceLanguageCode,
            resolved.DisplayTitle,
            resolved.SourceText,
            resolved.TranslatedText,
            resolved.TtsInputText,
            resolved.AudioCacheKey,
            resolved.TranslationStatus,
            resolved.FallbackMessage);

        return resolved;
    }

    private async Task<(string? Title, string? Body)> TranslateNarrationAsync(
        string? sourceTitle,
        string sourceBody,
        string sourceLanguageCode,
        string targetLanguageCode,
        CancellationToken cancellationToken)
    {
        var segments = new List<string>();
        var hasTitle = !string.IsNullOrWhiteSpace(sourceTitle);
        var bodySegments = SplitTranslationSegments(sourceBody);

        if (hasTitle)
        {
            segments.Add(sourceTitle!);
        }

        segments.AddRange(bodySegments);

        var translated = await translationProxyService.TranslateAsync(
            new TextTranslationRequest(targetLanguageCode, sourceLanguageCode, segments),
            cancellationToken);

        var translatedTitle = hasTitle
            ? CleanNullableForLanguage(translated.Texts.ElementAtOrDefault(0), targetLanguageCode)
            : null;
        var translatedBodyParts = translated.Texts
            .Skip(hasTitle ? 1 : 0)
            .Take(bodySegments.Count)
            .Select(part => CleanNullableForLanguage(part, targetLanguageCode))
            .Where(part => !string.IsNullOrWhiteSpace(part))
            .ToList();
        var translatedBody = translatedBodyParts.Count == 0
            ? null
            : string.Join("\n\n", translatedBodyParts);

        if (HasNarrationContent(sourceBody) && !HasNarrationContent(translatedBody))
        {
            throw new InvalidOperationException("The translated narration body was empty or unusable.");
        }

        return (translatedTitle, translatedBody);
    }

    private static Translation? FindExactPoiTranslation(
        IEnumerable<Translation> translations,
        string languageCode) =>
        translations.FirstOrDefault(item =>
            string.Equals(NormalizeLanguageCode(item.LanguageCode), NormalizeLanguageCode(languageCode), StringComparison.OrdinalIgnoreCase));

    private static Translation? FindBestSourcePoiTranslation(
        IReadOnlyList<Translation> translations,
        string languageCode,
        string defaultLanguageCode,
        string fallbackLanguageCode)
    {
        var preferredLanguages = new List<string>();
        if (string.Equals(languageCode, NormalizeLanguageCode(defaultLanguageCode), StringComparison.OrdinalIgnoreCase))
        {
            AddPreferredLanguage(preferredLanguages, defaultLanguageCode);
            AddPreferredLanguage(preferredLanguages, fallbackLanguageCode);
        }
        else
        {
            AddPreferredLanguage(preferredLanguages, fallbackLanguageCode);
            AddPreferredLanguage(preferredLanguages, defaultLanguageCode);
        }

        foreach (var currentLanguageCode in preferredLanguages)
        {
            var normalizedCurrentLanguageCode = NormalizeLanguageCode(currentLanguageCode);
            var matched = translations.FirstOrDefault(item =>
                string.Equals(NormalizeLanguageCode(item.LanguageCode), normalizedCurrentLanguageCode, StringComparison.OrdinalIgnoreCase) &&
                HasNarrationContent(GetNarrationBodyForLanguage(item, normalizedCurrentLanguageCode)));
            if (matched is not null)
            {
                return matched;
            }
        }

        return null;
    }

    private static AudioGuide? FindPoiAudioGuide(
        IEnumerable<AudioGuide> audioGuides,
        string languageCode)
    {
        var matchingGuides = audioGuides
            .Where(item => string.Equals(item.LanguageCode, languageCode, StringComparison.OrdinalIgnoreCase))
            .ToList();

        return
            matchingGuides.FirstOrDefault(IsPlayableAudioGuide) ??
            matchingGuides.FirstOrDefault(HasUsablePreparedAudio) ??
            matchingGuides.FirstOrDefault();
    }

    private static bool IsPlayableAudioGuide(AudioGuide audioGuide) =>
        string.Equals(audioGuide.Status, "ready", StringComparison.OrdinalIgnoreCase) &&
        HasUsablePreparedAudio(audioGuide) &&
        !IsPlaceholderAudioUrl(audioGuide.AudioUrl);

    private static bool HasUsablePreparedAudio(AudioGuide audioGuide) =>
        string.Equals(audioGuide.SourceType, "uploaded", StringComparison.OrdinalIgnoreCase) &&
        HasValidAudioUrl(audioGuide.AudioUrl);

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

    private static string GetNarrationBodyForLanguage(Translation? translation, string languageCode)
    {
        var fullText = CleanNullableForLanguage(translation?.FullText, languageCode);
        if (!string.IsNullOrWhiteSpace(fullText))
        {
            return fullText;
        }

        return CleanNullableForLanguage(translation?.ShortText, languageCode) ?? string.Empty;
    }

    private static bool ShouldRefreshAutoTranslation(
        Translation? exactTranslation,
        Translation sourceTranslation)
    {
        if (exactTranslation is null)
        {
            return true;
        }

        return sourceTranslation.UpdatedAt > exactTranslation.UpdatedAt;
    }

    private static bool IsEquivalentNarration(string? left, string? right) =>
        string.Equals(NormalizeNarration(left), NormalizeNarration(right), StringComparison.Ordinal);

    private static string NormalizeNarration(string? value) =>
        string.Join(
            " ",
            (value ?? string.Empty)
                .Split(default(string[]), StringSplitOptions.RemoveEmptyEntries))
            .Trim();

    private static IReadOnlyList<string> SplitTranslationSegments(string value, int maxLength = 700, int minTrailingLength = 180)
    {
        var cleaned = NarrationTextSanitizer.Clean(value);
        if (string.IsNullOrWhiteSpace(cleaned))
        {
            return [];
        }

        var segments = new List<string>();
        var paragraphs = cleaned
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(part => !string.IsNullOrWhiteSpace(part))
            .ToList();

        if (paragraphs.Count == 0)
        {
            paragraphs.Add(cleaned);
        }

        foreach (var paragraph in paragraphs)
        {
            AppendTranslationSegments(segments, paragraph, maxLength, minTrailingLength);
        }

        return segments.Count == 0 ? [cleaned] : segments;
    }

    private static void AppendTranslationSegments(
        ICollection<string> segments,
        string text,
        int maxLength,
        int minTrailingLength)
    {
        var remaining = text.Trim();
        while (remaining.Length > maxLength)
        {
            var splitIndex = FindSegmentSplitIndex(remaining, maxLength, minTrailingLength);
            var segment = remaining[..splitIndex].Trim();
            if (segment.Length == 0)
            {
                break;
            }

            segments.Add(segment);
            remaining = remaining[splitIndex..].Trim();
        }

        if (remaining.Length > 0)
        {
            segments.Add(remaining);
        }
    }

    private static int FindSegmentSplitIndex(string value, int maxLength, int minTrailingLength)
    {
        var lowerBound = Math.Max(maxLength - minTrailingLength, maxLength / 2);

        for (var index = Math.Min(maxLength, value.Length - 1); index >= lowerBound; index -= 1)
        {
            var current = value[index];
            if (current is '.' or '!' or '?' or ';' or ':' or ',' or '\n')
            {
                return index + 1;
            }

            if (char.IsWhiteSpace(current))
            {
                return index;
            }
        }

        return Math.Min(maxLength, value.Length);
    }

    private static void AddPreferredLanguage(ICollection<string> languages, string? languageCode)
    {
        if (!string.IsNullOrWhiteSpace(languageCode) &&
            !languages.Contains(languageCode.Trim(), StringComparer.OrdinalIgnoreCase))
        {
            languages.Add(languageCode.Trim());
        }
    }

    private static string BuildNarrationText(string? title, string? body)
    {
        var normalizedTitle = CleanNullable(title);
        var normalizedBody = CleanNullable(body);

        if (!HasNarrationContent(normalizedTitle) && !HasNarrationContent(normalizedBody))
        {
            return string.Empty;
        }

        // Keep the title separate from the narration body so the UI and TTS
        // reflect the exact description text entered by the admin.
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
            $"guideUrl={CreateHash(audioGuide?.AudioUrl?.Trim() ?? string.Empty)}");

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
            if (!string.IsNullOrWhiteSpace(candidate))
            {
                return candidate.Trim() switch
                {
                    "zh" => "zh-CN",
                    "fr-FR" => "fr",
                    "en-US" => "en",
                    "ja-JP" => "ja",
                    "ko-KR" => "ko",
                    _ => candidate.Trim()
                };
            }
        }

        return "vi";
    }

    private static string GetLocale(string languageCode) =>
        LanguageLocales.TryGetValue(languageCode, out var locale)
            ? locale
            : "en-US";

    private static string GetLanguageLabel(string? languageCode)
    {
        if (!string.IsNullOrWhiteSpace(languageCode) &&
            LanguageLabels.TryGetValue(languageCode, out var label))
        {
            return label;
        }

        return languageCode?.Trim() ?? "the selected language";
    }

    private static string BuildStoredTranslationFallbackMessage(
        Exception exception,
        string requestedLanguageCode) =>
        IsTranslationServiceUnavailable(exception)
            ? $"The translation service is unavailable for {GetLanguageLabel(requestedLanguageCode)}. Using the stored translation."
            : $"Unable to refresh the {GetLanguageLabel(requestedLanguageCode)} translation. Using the stored translation.";

    private static string BuildSourceFallbackMessage(
        Exception exception,
        string requestedLanguageCode,
        string? sourceLanguageCode) =>
        IsTranslationServiceUnavailable(exception)
            ? $"The translation service is unavailable for {GetLanguageLabel(requestedLanguageCode)}. Source content in {GetLanguageLabel(sourceLanguageCode)} is not used for playback."
            : $"No stored translation is available for {GetLanguageLabel(requestedLanguageCode)}. Source content in {GetLanguageLabel(sourceLanguageCode)} is not used for playback.";

    private static bool IsTranslationServiceUnavailable(Exception exception) =>
        exception is HttpRequestException ||
        exception.InnerException is HttpRequestException;

    private AudioGuide? NormalizeAudioGuideForResponse(AudioGuide? audioGuide)
    {
        if (audioGuide is null)
        {
            return audioGuide;
        }

        if (!string.Equals(audioGuide.SourceType, "uploaded", StringComparison.OrdinalIgnoreCase))
        {
            return new AudioGuide
            {
                Id = audioGuide.Id,
                EntityType = audioGuide.EntityType,
                EntityId = audioGuide.EntityId,
                LanguageCode = audioGuide.LanguageCode,
                AudioUrl = string.Empty,
                VoiceType = audioGuide.VoiceType,
                SourceType = audioGuide.SourceType,
                Status = audioGuide.Status,
                UpdatedBy = audioGuide.UpdatedBy,
                UpdatedAt = audioGuide.UpdatedAt
            };
        }

        if (!HasValidAudioUrl(audioGuide.AudioUrl))
        {
            return audioGuide;
        }

        var absoluteUrl = BuildAbsoluteUrl(audioGuide.AudioUrl);
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
            AudioUrl = absoluteUrl,
            VoiceType = audioGuide.VoiceType,
            SourceType = audioGuide.SourceType,
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

    private static bool IsSourceLanguage(string? languageCode)
        => LocalizationContentPolicy.IsSourceLanguage(languageCode);

    private static bool IsUsableTextForLanguage(string? value, string? languageCode)
        => LocalizationContentPolicy.IsUsableTextForLanguage(value, languageCode);

    private static string? CleanNullableForLanguage(string? value, string? languageCode)
        => LocalizationContentPolicy.CleanForLanguage(value, languageCode);

    private static string? CleanNullable(string? value)
    {
        var cleaned = NarrationTextSanitizer.Clean(value);
        return string.IsNullOrWhiteSpace(cleaned) ? null : cleaned;
    }
}
