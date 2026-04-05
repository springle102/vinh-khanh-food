using VinhKhanh.BackendApi.Contracts;
using VinhKhanh.BackendApi.Models;

namespace VinhKhanh.BackendApi.Infrastructure;

public sealed class PoiNarrationService(
    AdminDataRepository repository,
    TranslationProxyService translationProxyService,
    IHttpContextAccessor httpContextAccessor,
    ILogger<PoiNarrationService> logger)
{
    private static readonly HashSet<string> SupportedVoices =
    [
        "standard",
        "north",
        "central",
        "south"
    ];

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
        ["vi"] = "Tiếng Việt",
        ["en"] = "Tiếng Anh",
        ["fr"] = "Tiếng Pháp",
        ["zh-CN"] = "Tiếng Trung",
        ["ko"] = "Tiếng Hàn",
        ["ja"] = "Tiếng Nhật"
    };

    public async Task<PoiNarrationResponse?> ResolveAsync(
        string poiId,
        string requestedLanguageCode,
        string? requestedVoiceType,
        string? scopeUserId,
        string? scopeRole,
        CancellationToken cancellationToken)
    {
        var poi = repository.GetPois(scopeUserId, scopeRole)
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
        var normalizedVoiceType = NormalizeVoiceType(requestedVoiceType);
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

        var exactTranslation = FindExactPoiTranslation(translations, normalizedRequestedLanguage);
        var sourceTranslation = FindBestSourcePoiTranslation(
            translations,
            normalizedRequestedLanguage,
            settings.DefaultLanguage,
            settings.FallbackLanguage);

        var exactTitle = CleanNullable(exactTranslation?.Title);
        var exactBody = GetNarrationBody(exactTranslation);
        var exactText = BuildNarrationText(exactTitle, exactBody);
        var sourceTitle = CleanNullable(sourceTranslation?.Title);
        var sourceBody = GetNarrationBody(sourceTranslation);
        var sourceText = BuildNarrationText(sourceTitle, sourceBody);
        var hasExactNarration = HasNarrationContent(exactBody);
        var hasSourceNarration = HasNarrationContent(sourceBody);
        var shouldAutoTranslateFromSource =
            hasSourceNarration &&
            sourceTranslation is not null &&
            !string.Equals(sourceTranslation.LanguageCode, normalizedRequestedLanguage, StringComparison.OrdinalIgnoreCase) &&
            (!hasExactNarration ||
            IsEquivalentNarration(exactBody, sourceBody) ||
            ShouldRefreshAutoTranslation(exactTranslation, sourceTranslation));

        var displayTitle = hasExactNarration
            ? exactTitle ?? poi.Slug
            : poi.Slug;
        var displayText = hasExactNarration
            ? exactText
            : string.Empty;
        var ttsInputText = displayText;
        string? translatedText = null;
        const string storedStatus = "stored";
        const string autoTranslatedStatus = "auto_translated";
        const string fallbackSourceStatus = "fallback_source";
        var translationStatus = storedStatus;
        string? sourceLanguageCode = sourceTranslation is not null && hasSourceNarration
            ? NormalizeLanguageCode(sourceTranslation.LanguageCode, settings.DefaultLanguage, settings.FallbackLanguage)
            : hasExactNarration
                ? NormalizeLanguageCode(exactTranslation?.LanguageCode, normalizedRequestedLanguage, settings.DefaultLanguage, settings.FallbackLanguage)
                : null;
        var effectiveLanguageCode = normalizedRequestedLanguage;
        string? fallbackMessage = null;

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

                displayTitle = translatedFragments.Title ?? sourceTitle ?? poi.Slug;
                displayText = BuildNarrationText(translatedFragments.Title, translatedFragments.Body);
                ttsInputText = displayText;
                translatedText = displayText;
                translationStatus = autoTranslatedStatus;

                if (!HasNarrationContent(displayText))
                {
                    throw new InvalidOperationException("Không nhận được nội dung đã dịch hợp lệ.");
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

                if (hasExactNarration && !IsEquivalentNarration(exactBody, sourceBody))
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
                    displayTitle = sourceTitle ?? poi.Slug;
                    displayText = sourceText;
                    ttsInputText = sourceText;
                    effectiveLanguageCode = sourceLanguageCode!;
                    translationStatus = fallbackSourceStatus;
                    fallbackMessage = BuildSourceFallbackMessage(
                        exception,
                        normalizedRequestedLanguage,
                        sourceLanguageCode);
                }
            }
        }
        else if (!hasExactNarration && hasSourceNarration && sourceTranslation is not null)
        {
            displayTitle = sourceTitle ?? poi.Slug;
            displayText = sourceText;
            ttsInputText = sourceText;
            effectiveLanguageCode = sourceLanguageCode ?? normalizedRequestedLanguage;
        }

        if (!HasNarrationContent(ttsInputText))
        {
            displayText = string.Empty;
            ttsInputText = string.Empty;
            effectiveLanguageCode = sourceLanguageCode ?? normalizedRequestedLanguage;
        }

        AudioGuide? audioGuide = null;
        if (!string.Equals(translationStatus, autoTranslatedStatus, StringComparison.Ordinal))
        {
            audioGuide = FindPoiAudioGuide(audioGuides, effectiveLanguageCode, normalizedVoiceType);
        }

        audioGuide = NormalizeAudioGuideForResponse(audioGuide);

        var uiPlaybackKey = BuildUiPlaybackKey(poi.Id, normalizedRequestedLanguage, normalizedVoiceType);
        var audioCacheKey = BuildAudioCacheKey(
            uiPlaybackKey,
            translationStatus,
            effectiveLanguageCode,
            sourceLanguageCode,
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
            normalizedVoiceType,
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
            "Resolved POI narration. PoiId={PoiId}; languageSelected={LanguageSelected}; sourceLanguage={SourceLanguage}; sourceText={SourceText}; translatedText={TranslatedText}; ttsInputText={TtsInputText}; selectedVoice={SelectedVoice}; cacheKey={CacheKey}; status={Status}; fallbackMessage={FallbackMessage}",
            poi.Id,
            normalizedRequestedLanguage,
            sourceLanguageCode,
            resolved.SourceText,
            resolved.TranslatedText,
            resolved.TtsInputText,
            normalizedVoiceType,
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

        if (hasTitle)
        {
            segments.Add(sourceTitle!);
        }

        segments.Add(sourceBody);

        var translated = await translationProxyService.TranslateAsync(
            new TextTranslationRequest(targetLanguageCode, sourceLanguageCode, segments),
            cancellationToken);

        var translatedTitle = hasTitle
            ? CleanNullable(translated.Texts.ElementAtOrDefault(0))
            : null;
        var translatedBody = CleanNullable(translated.Texts.ElementAtOrDefault(hasTitle ? 1 : 0));

        return (translatedTitle, translatedBody);
    }

    private static Translation? FindExactPoiTranslation(
        IEnumerable<Translation> translations,
        string languageCode) =>
        translations.FirstOrDefault(item =>
            string.Equals(item.LanguageCode, languageCode, StringComparison.OrdinalIgnoreCase));

    private static Translation? FindBestSourcePoiTranslation(
        IReadOnlyList<Translation> translations,
        string languageCode,
        string defaultLanguageCode,
        string fallbackLanguageCode)
    {
        var preferredLanguages = new List<string>();
        AddPreferredLanguage(preferredLanguages, defaultLanguageCode);
        AddPreferredLanguage(preferredLanguages, fallbackLanguageCode);

        foreach (var currentLanguageCode in preferredLanguages)
        {
            var matched = translations.FirstOrDefault(item =>
                string.Equals(item.LanguageCode, currentLanguageCode, StringComparison.OrdinalIgnoreCase) &&
                HasNarrationContent(GetNarrationBody(item)));
            if (matched is not null)
            {
                return matched;
            }
        }

        var nonRequested = translations.FirstOrDefault(item =>
            !string.Equals(item.LanguageCode, languageCode, StringComparison.OrdinalIgnoreCase) &&
            HasNarrationContent(GetNarrationBody(item)));
        if (nonRequested is not null)
        {
            return nonRequested;
        }

        return translations.FirstOrDefault(item => HasNarrationContent(GetNarrationBody(item)));
    }

    private static AudioGuide? FindPoiAudioGuide(
        IEnumerable<AudioGuide> audioGuides,
        string languageCode,
        string voiceType)
    {
        var matchingGuides = audioGuides
            .Where(item => string.Equals(item.LanguageCode, languageCode, StringComparison.OrdinalIgnoreCase))
            .ToList();

        return
            matchingGuides.FirstOrDefault(item => IsPlayableAudioGuide(item) && string.Equals(item.VoiceType, voiceType, StringComparison.OrdinalIgnoreCase)) ??
            matchingGuides.FirstOrDefault(IsPlayableAudioGuide) ??
            matchingGuides.FirstOrDefault(item => HasValidAudioUrl(item.AudioUrl) && string.Equals(item.VoiceType, voiceType, StringComparison.OrdinalIgnoreCase)) ??
            matchingGuides.FirstOrDefault(item => HasValidAudioUrl(item.AudioUrl)) ??
            matchingGuides.FirstOrDefault(item => string.Equals(item.VoiceType, voiceType, StringComparison.OrdinalIgnoreCase)) ??
            matchingGuides.FirstOrDefault();
    }

    private static bool IsPlayableAudioGuide(AudioGuide audioGuide) =>
        string.Equals(audioGuide.Status, "ready", StringComparison.OrdinalIgnoreCase) &&
        HasValidAudioUrl(audioGuide.AudioUrl) &&
        !IsPlaceholderAudioUrl(audioGuide.AudioUrl);

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

    private static string GetNarrationBody(Translation? translation) =>
        NarrationTextSanitizer.Clean(translation?.FullText) is var fullText && !string.IsNullOrWhiteSpace(fullText)
            ? fullText
            : NarrationTextSanitizer.Clean(translation?.ShortText);

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

    private static string BuildUiPlaybackKey(string poiId, string languageCode, string voiceType) =>
        $"{poiId}:{languageCode}:{voiceType}";

    private static string BuildAudioCacheKey(
        string uiPlaybackKey,
        string translationStatus,
        string effectiveLanguageCode,
        string? sourceLanguageCode,
        string ttsInputText,
        AudioGuide? audioGuide) =>
        string.Join(
            "|",
            uiPlaybackKey,
            $"status={translationStatus}",
            $"effective={effectiveLanguageCode}",
            $"source={sourceLanguageCode ?? "none"}",
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

    private static string NormalizeVoiceType(string? value)
    {
        var normalized = value?.Trim().ToLowerInvariant();
        return normalized is not null && SupportedVoices.Contains(normalized)
            ? normalized
            : "standard";
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
            : "vi-VN";

    private static string GetLanguageLabel(string? languageCode)
    {
        if (!string.IsNullOrWhiteSpace(languageCode) &&
            LanguageLabels.TryGetValue(languageCode, out var label))
        {
            return label;
        }

        return languageCode?.Trim() ?? "ngôn ngữ hiện tại";
    }

    private static string BuildStoredTranslationFallbackMessage(
        Exception exception,
        string requestedLanguageCode) =>
        IsTranslationServiceUnavailable(exception)
            ? $"Không thể kết nối dịch vụ dịch để cập nhật bản {GetLanguageLabel(requestedLanguageCode)}. Đang dùng bản dịch đã lưu trước đó."
            : $"Không thể dịch lại sang {GetLanguageLabel(requestedLanguageCode)}. Đang dùng bản dịch đã lưu trước đó.";

    private static string BuildSourceFallbackMessage(
        Exception exception,
        string requestedLanguageCode,
        string? sourceLanguageCode) =>
        IsTranslationServiceUnavailable(exception)
            ? $"Không thể kết nối dịch vụ dịch cho {GetLanguageLabel(requestedLanguageCode)}. Đang dùng nội dung gốc {GetLanguageLabel(sourceLanguageCode)}."
            : $"Chưa có bản dịch lưu sẵn cho {GetLanguageLabel(requestedLanguageCode)}. Đang dùng nội dung gốc {GetLanguageLabel(sourceLanguageCode)}.";

    private static bool IsTranslationServiceUnavailable(Exception exception) =>
        exception is HttpRequestException ||
        exception.InnerException is HttpRequestException;

    private AudioGuide? NormalizeAudioGuideForResponse(AudioGuide? audioGuide)
    {
        if (audioGuide is null || !HasValidAudioUrl(audioGuide.AudioUrl))
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

    private static string? CleanNullable(string? value)
    {
        var cleaned = NarrationTextSanitizer.Clean(value);
        return string.IsNullOrWhiteSpace(cleaned) ? null : cleaned;
    }
}
