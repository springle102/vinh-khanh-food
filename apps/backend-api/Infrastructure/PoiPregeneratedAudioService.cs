using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using VinhKhanh.BackendApi.Contracts;
using VinhKhanh.BackendApi.Models;

namespace VinhKhanh.BackendApi.Infrastructure;

public sealed class PoiPregeneratedAudioService(
    AdminDataRepository repository,
    PoiNarrationService poiNarrationService,
    ITextToSpeechService textToSpeechService,
    GeneratedAudioStorageService generatedAudioStorageService,
    IOptions<TextToSpeechOptions> optionsAccessor,
    ILogger<PoiPregeneratedAudioService> logger)
{
    public IReadOnlyList<AudioGuide> GetPoiAudioGuides(string poiId, AdminRequestContext actor)
        => repository.GetAudioGuides(actor)
            .Where(item =>
                string.Equals(item.EntityType, "poi", StringComparison.OrdinalIgnoreCase) &&
                string.Equals(item.EntityId, poiId, StringComparison.OrdinalIgnoreCase))
            .OrderBy(item => item.LanguageCode, StringComparer.OrdinalIgnoreCase)
            .ThenByDescending(item => item.UpdatedAt)
            .ToList();

    public async Task<PoiAudioGenerationResult> GeneratePoiLanguageAsync(
        string poiId,
        PoiAudioGenerationRequest request,
        AdminRequestContext actor,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var normalizedLanguageCode = PremiumAccessCatalog.NormalizeLanguageCode(request.LanguageCode);
        var poi = repository.GetPois(actor).FirstOrDefault(item => string.Equals(item.Id, poiId, StringComparison.OrdinalIgnoreCase));
        if (poi is null)
        {
            throw new ApiNotFoundException("Khong tim thay POI de generate audio.");
        }

        EnsureLanguageIsSupported(normalizedLanguageCode);

        var narration = await poiNarrationService.ResolveAsync(
            poiId,
            normalizedLanguageCode,
            actor,
            cancellationToken);
        if (narration is null)
        {
            throw new ApiNotFoundException("Khong tim thay noi dung thuyet minh de generate audio.");
        }

        var transcriptText = narration.TtsInputText?.Trim() ?? string.Empty;
        var existing = GetPoiAudioGuides(poiId, actor)
            .FirstOrDefault(item => PremiumAccessCatalog.LanguageCodesMatch(item.LanguageCode, normalizedLanguageCode));
        var options = optionsAccessor.Value;
        var effectiveLanguageCode = PremiumAccessCatalog.NormalizeLanguageCode(narration.EffectiveLanguageCode);
        var ttsProviderLanguageCode = LanguageRegistry.GetTtsProviderCode(effectiveLanguageCode);
        var storageLanguageCode = LanguageRegistry.GetStorageCode(normalizedLanguageCode);
        var voiceCandidates = options.ResolveVoiceCandidates(effectiveLanguageCode, request.VoiceId);
        var modelCandidates = options.ResolveModelCandidates(effectiveLanguageCode, request.ModelId);
        var voiceId = voiceCandidates.First();
        var modelId = modelCandidates.First();
        var outputFormat = string.IsNullOrWhiteSpace(request.OutputFormat)
            ? options.OutputFormat
            : request.OutputFormat.Trim();
        var textHash = CreateTextHash(effectiveLanguageCode, transcriptText);
        var translationFellBackToSource =
            string.Equals(narration.TranslationStatus, "fallback_source", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(effectiveLanguageCode, normalizedLanguageCode, StringComparison.OrdinalIgnoreCase);

        if (translationFellBackToSource)
        {
            var failedGuide = SaveGuide(
                existing?.Id,
                actor,
                new AudioGuideUpsertRequest(
                    "poi",
                    poiId,
                    normalizedLanguageCode,
                    string.Empty,
                    AudioGuideCatalog.SourceTypeGenerated,
                    AudioGuideCatalog.PublicStatusMissing,
                    actor.Name,
                    TranscriptText: transcriptText,
                    AudioFilePath: string.Empty,
                    AudioFileName: string.Empty,
                    Provider: AudioGuideCatalog.ProviderElevenLabs,
                    VoiceId: voiceId,
                    ModelId: modelId,
                    OutputFormat: outputFormat,
                    DurationInSeconds: null,
                    FileSizeBytes: null,
                    TextHash: textHash,
                    ContentVersion: textHash,
                    GeneratedAt: null,
                    GenerationStatus: AudioGuideCatalog.GenerationStatusFailed,
                    ErrorMessage: "Runtime translation failed; audio was not generated to avoid saving source-language audio under the requested language.",
                    IsOutdated: true,
                    VoiceType: existing?.VoiceType ?? "standard"));

            logger.LogWarning(
                "Skipped POI audio generation because runtime translation fell back to source. poiId={PoiId}; requestedLanguage={RequestedLanguage}; effectiveLanguage={EffectiveLanguage}",
                poiId,
                normalizedLanguageCode,
                effectiveLanguageCode);

            return new PoiAudioGenerationResult(
                poiId,
                normalizedLanguageCode,
                effectiveLanguageCode,
                false,
                false,
                existing is not null,
                "Khong tao audio vi dich runtime that bai; tranh luu audio sai ngon ngu.",
                transcriptText,
                textHash,
                failedGuide);
        }

        if (string.IsNullOrWhiteSpace(transcriptText))
        {
            var failedGuide = SaveGuide(
                existing?.Id,
                actor,
                new AudioGuideUpsertRequest(
                    "poi",
                    poiId,
                    normalizedLanguageCode,
                    existing?.AudioUrl ?? string.Empty,
                    existing?.SourceType ?? AudioGuideCatalog.SourceTypeGenerated,
                    AudioGuideCatalog.PublicStatusMissing,
                    actor.Name,
                    TranscriptText: string.Empty,
                    AudioFilePath: existing?.AudioFilePath,
                    AudioFileName: existing?.AudioFileName,
                    Provider: AudioGuideCatalog.ProviderElevenLabs,
                    VoiceId: voiceId,
                    ModelId: modelId,
                    OutputFormat: outputFormat,
                    DurationInSeconds: existing?.DurationInSeconds,
                    FileSizeBytes: existing?.FileSizeBytes,
                    TextHash: textHash,
                    ContentVersion: textHash,
                    GeneratedAt: existing?.GeneratedAt,
                    GenerationStatus: AudioGuideCatalog.GenerationStatusFailed,
                    ErrorMessage: "Narration text is empty for this POI/language.",
                    IsOutdated: true,
                    VoiceType: existing?.VoiceType ?? "standard"));

            return new PoiAudioGenerationResult(
                poiId,
                normalizedLanguageCode,
                effectiveLanguageCode,
                false,
                false,
                false,
                "Noi dung thuyet minh rong, khong the tao audio.",
                transcriptText,
                textHash,
                failedGuide);
        }

        if (!request.ForceRegenerate &&
            existing is not null &&
            string.Equals(existing.TextHash, textHash, StringComparison.OrdinalIgnoreCase) &&
            voiceCandidates.Any(candidate => string.Equals(existing.VoiceId, candidate, StringComparison.OrdinalIgnoreCase)) &&
            modelCandidates.Any(candidate => string.Equals(existing.ModelId, candidate, StringComparison.OrdinalIgnoreCase)) &&
            string.Equals(existing.OutputFormat, outputFormat, StringComparison.OrdinalIgnoreCase) &&
            !existing.IsOutdated &&
            AudioGuideCatalog.IsReadyForPlayback(existing) &&
            generatedAudioStorageService.Exists(existing.AudioFilePath))
        {
            logger.LogInformation(
                "Skipped POI audio generation because an up-to-date file already exists. poiId={PoiId}; languageCode={LanguageCode}; audioGuideId={AudioGuideId}",
                poiId,
                normalizedLanguageCode,
                existing.Id);

            return new PoiAudioGenerationResult(
                poiId,
                normalizedLanguageCode,
                effectiveLanguageCode,
                true,
                true,
                false,
                "Audio da ton tai va van con hieu luc, bo qua generate lai.",
                transcriptText,
                textHash,
                existing);
        }

        SaveGuide(
            existing?.Id,
            actor,
            new AudioGuideUpsertRequest(
                "poi",
                poiId,
                normalizedLanguageCode,
                string.Empty,
                AudioGuideCatalog.SourceTypeGenerated,
                AudioGuideCatalog.PublicStatusProcessing,
                actor.Name,
                TranscriptText: transcriptText,
                AudioFilePath: string.Empty,
                AudioFileName: string.Empty,
                Provider: AudioGuideCatalog.ProviderElevenLabs,
                VoiceId: voiceId,
                ModelId: modelId,
                OutputFormat: outputFormat,
                DurationInSeconds: null,
                FileSizeBytes: null,
                TextHash: textHash,
                ContentVersion: textHash,
                GeneratedAt: null,
                GenerationStatus: AudioGuideCatalog.GenerationStatusPending,
                ErrorMessage: null,
                IsOutdated: false,
                VoiceType: existing?.VoiceType ?? "standard"));

        try
        {
            logger.LogInformation(
                "[AudioGenerate] Generating POI audio via ElevenLabs. poiId={PoiId}; requestedLang={RequestedLanguage}; normalizedLang={NormalizedLanguage}; effectiveLang={EffectiveLanguage}; providerLang={ProviderLanguage}; storageLang={StorageLanguage}; voiceId={VoiceId}; modelId={ModelId}; outputFormat={OutputFormat}; textLength={TextLength}; textHash={TextHash}; translationStatus={TranslationStatus}",
                poiId,
                request.LanguageCode,
                normalizedLanguageCode,
                effectiveLanguageCode,
                ttsProviderLanguageCode,
                storageLanguageCode,
                voiceId,
                modelId,
                outputFormat,
                transcriptText.Length,
                textHash,
                narration.TranslationStatus);

            var ttsAudio = await GenerateTtsWithFallbackAsync(
                transcriptText,
                effectiveLanguageCode,
                ttsProviderLanguageCode,
                outputFormat,
                voiceCandidates,
                modelCandidates,
                cancellationToken);
            var storedFile = await generatedAudioStorageService.SavePoiAudioAsync(
                poiId,
                storageLanguageCode,
                textHash,
                outputFormat,
                ttsAudio.ContentType,
                ttsAudio.Content,
                cancellationToken);
            var savedGuide = SaveGuide(
                existing?.Id,
                actor,
                new AudioGuideUpsertRequest(
                    "poi",
                    poiId,
                    normalizedLanguageCode,
                    storedFile.PublicUrl,
                    AudioGuideCatalog.SourceTypeGenerated,
                    AudioGuideCatalog.PublicStatusReady,
                    actor.Name,
                    TranscriptText: transcriptText,
                    AudioFilePath: storedFile.RelativePath,
                    AudioFileName: storedFile.FileName,
                    Provider: ttsAudio.Provider,
                    VoiceId: ttsAudio.VoiceId,
                    ModelId: ttsAudio.ModelId,
                    OutputFormat: ttsAudio.OutputFormat,
                    DurationInSeconds: ttsAudio.EstimatedDurationSeconds,
                    FileSizeBytes: storedFile.SizeBytes,
                    TextHash: textHash,
                    ContentVersion: textHash,
                    GeneratedAt: DateTimeOffset.UtcNow,
                    GenerationStatus: AudioGuideCatalog.GenerationStatusSuccess,
                    ErrorMessage: null,
                    IsOutdated: false,
                    VoiceType: existing?.VoiceType ?? "standard"));

            logger.LogInformation(
                "[AudioGenerate] POI audio generated successfully. poiId={PoiId}; requestedLang={RequestedLanguage}; normalizedLang={NormalizedLanguage}; effectiveLang={EffectiveLanguage}; providerLang={ProviderLanguage}; storageLang={StorageLanguage}; audioGuideId={AudioGuideId}; filePath={FilePath}; audioUrl={AudioUrl}; fileSaved={FileSaved}",
                poiId,
                request.LanguageCode,
                normalizedLanguageCode,
                effectiveLanguageCode,
                ttsProviderLanguageCode,
                storageLanguageCode,
                savedGuide.Id,
                savedGuide.AudioFilePath,
                savedGuide.AudioUrl,
                generatedAudioStorageService.Exists(savedGuide.AudioFilePath));

            return new PoiAudioGenerationResult(
                poiId,
                normalizedLanguageCode,
                effectiveLanguageCode,
                true,
                false,
                existing is not null,
                "Generate audio thanh cong.",
                transcriptText,
                textHash,
                savedGuide);
        }
        catch (Exception exception) when (
            exception is TextToSpeechConfigurationException ||
            exception is TextToSpeechGenerationException ||
            exception is HttpRequestException ||
            exception is TaskCanceledException ||
            exception is InvalidOperationException)
        {
            var ttsException = exception as TextToSpeechGenerationException;
            var failureMessage = BuildGenerationFailureMessage(exception);
            logger.LogWarning(
                exception,
                "[AudioGenerate] Failed to generate POI audio. poiId={PoiId}; requestedLang={RequestedLanguage}; normalizedLang={NormalizedLanguage}; effectiveLang={EffectiveLanguage}; providerLang={ProviderLanguage}; voiceId={VoiceId}; modelId={ModelId}; outputFormat={OutputFormat}; textLength={TextLength}; providerStatusCode={ProviderStatusCode}; providerErrorCode={ProviderErrorCode}; providerErrorMessage={ProviderErrorMessage}; providerResponseBody={ProviderResponseBody}",
                poiId,
                request.LanguageCode,
                normalizedLanguageCode,
                effectiveLanguageCode,
                ttsProviderLanguageCode,
                ttsException?.VoiceId ?? voiceId,
                ttsException?.ModelId ?? modelId,
                outputFormat,
                transcriptText.Length,
                ttsException?.StatusCode is null ? null : (int)ttsException.StatusCode.Value,
                ttsException?.ProviderErrorCode,
                ttsException?.ProviderErrorMessage,
                ttsException?.ResponseBody);

            var failedGuide = SaveGuide(
                existing?.Id,
                actor,
                new AudioGuideUpsertRequest(
                    "poi",
                    poiId,
                    normalizedLanguageCode,
                    string.Empty,
                    AudioGuideCatalog.SourceTypeGenerated,
                    AudioGuideCatalog.PublicStatusMissing,
                    actor.Name,
                    TranscriptText: transcriptText,
                    AudioFilePath: string.Empty,
                    AudioFileName: string.Empty,
                    Provider: AudioGuideCatalog.ProviderElevenLabs,
                    VoiceId: voiceId,
                    ModelId: modelId,
                    OutputFormat: outputFormat,
                    DurationInSeconds: null,
                    FileSizeBytes: null,
                    TextHash: textHash,
                    ContentVersion: textHash,
                    GeneratedAt: null,
                    GenerationStatus: AudioGuideCatalog.GenerationStatusFailed,
                    ErrorMessage: failureMessage,
                    IsOutdated: true,
                    VoiceType: existing?.VoiceType ?? "standard"));

            return new PoiAudioGenerationResult(
                poiId,
                normalizedLanguageCode,
                effectiveLanguageCode,
                false,
                false,
                existing is not null,
                $"Generate audio that bai: {failureMessage}",
                transcriptText,
                textHash,
                failedGuide,
                ttsException?.StatusCode is null ? null : (int)ttsException.StatusCode.Value,
                ttsException?.ProviderErrorCode,
                ttsException?.ProviderErrorMessage,
                ttsException?.ResponseBody,
                ttsException?.VoiceId ?? voiceId,
                ttsException?.ModelId ?? modelId,
                outputFormat);
        }
    }

    public async Task<IReadOnlyList<PoiAudioGenerationResult>> GeneratePoiAllLanguagesAsync(
        string poiId,
        PoiAudioBulkGenerationRequest request,
        AdminRequestContext actor,
        CancellationToken cancellationToken)
    {
        var results = new List<PoiAudioGenerationResult>();
        foreach (var languageCode in GetGenerationLanguages())
        {
            cancellationToken.ThrowIfCancellationRequested();
            results.Add(await GeneratePoiLanguageAsync(
                poiId,
                new PoiAudioGenerationRequest(languageCode, null, null, null, request.ForceRegenerate),
                actor,
                cancellationToken));
        }

        return results;
    }

    public async Task<IReadOnlyList<PoiAudioGenerationResult>> GenerateBulkAsync(
        PoiAudioBulkGenerationRequest request,
        AdminRequestContext actor,
        CancellationToken cancellationToken)
    {
        var results = new List<PoiAudioGenerationResult>();
        var poiIds = repository.GetPois(actor)
            .Select(item => item.Id)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(item => item, StringComparer.OrdinalIgnoreCase)
            .ToList();

        foreach (var poiId in poiIds)
        {
            cancellationToken.ThrowIfCancellationRequested();
            foreach (var languageCode in GetGenerationLanguages())
            {
                var existing = GetPoiAudioGuides(poiId, actor)
                    .FirstOrDefault(item => PremiumAccessCatalog.LanguageCodesMatch(item.LanguageCode, languageCode));
                if (!request.ForceRegenerate && !ShouldGenerate(existing, request))
                {
                    continue;
                }

                results.Add(await GeneratePoiLanguageAsync(
                    poiId,
                    new PoiAudioGenerationRequest(languageCode, null, null, null, request.ForceRegenerate),
                    actor,
                    cancellationToken));
            }
        }

        return results;
    }

    private async Task<TextToSpeechResult> GenerateTtsWithFallbackAsync(
        string transcriptText,
        string effectiveLanguageCode,
        string providerLanguageCode,
        string outputFormat,
        IReadOnlyList<string> voiceCandidates,
        IReadOnlyList<string> modelCandidates,
        CancellationToken cancellationToken)
    {
        var attempts = BuildTtsAttempts(voiceCandidates, modelCandidates, outputFormat);
        TextToSpeechGenerationException? lastProviderException = null;

        for (var attemptIndex = 0; attemptIndex < attempts.Count; attemptIndex += 1)
        {
            var attempt = attempts[attemptIndex];
            try
            {
                logger.LogInformation(
                    "[AudioGenerate] ElevenLabs attempt {AttemptNumber}/{AttemptCount}. effectiveLang={EffectiveLanguage}; providerLang={ProviderLanguage}; voiceId={VoiceId}; modelId={ModelId}; outputFormat={OutputFormat}; isFallback={IsFallback}",
                    attemptIndex + 1,
                    attempts.Count,
                    effectiveLanguageCode,
                    providerLanguageCode,
                    attempt.VoiceId,
                    attempt.ModelId,
                    attempt.OutputFormat,
                    attemptIndex > 0);

                return await textToSpeechService.GenerateAudioAsync(
                    new TextToSpeechRequest(
                        transcriptText,
                        effectiveLanguageCode,
                        attempt.VoiceId,
                        attempt.ModelId,
                        attempt.OutputFormat),
                    cancellationToken);
            }
            catch (TextToSpeechGenerationException exception) when (
                attemptIndex < attempts.Count - 1 &&
                ShouldRetryWithNextTtsAttempt(exception))
            {
                lastProviderException = exception;
                var nextAttempt = attempts[attemptIndex + 1];
                logger.LogWarning(
                    exception,
                    "[AudioGenerate] ElevenLabs attempt failed with retryable provider/config error; retrying next voice/model candidate. failedVoiceId={FailedVoiceId}; failedModelId={FailedModelId}; nextVoiceId={NextVoiceId}; nextModelId={NextModelId}; providerStatusCode={ProviderStatusCode}; providerErrorCode={ProviderErrorCode}; providerErrorMessage={ProviderErrorMessage}; providerResponseBody={ProviderResponseBody}",
                    attempt.VoiceId,
                    attempt.ModelId,
                    nextAttempt.VoiceId,
                    nextAttempt.ModelId,
                    exception.StatusCode is null ? null : (int)exception.StatusCode.Value,
                    exception.ProviderErrorCode,
                    exception.ProviderErrorMessage,
                    exception.ResponseBody);
            }
        }

        throw lastProviderException ??
              new TextToSpeechGenerationException("Khong the tao audio TTS bang bat ky voice/model candidate nao.");
    }

    private static IReadOnlyList<TtsGenerationAttempt> BuildTtsAttempts(
        IReadOnlyList<string> voiceCandidates,
        IReadOnlyList<string> modelCandidates,
        string outputFormat)
    {
        var attempts = new List<TtsGenerationAttempt>();
        foreach (var modelId in modelCandidates.Where(value => !string.IsNullOrWhiteSpace(value)))
        {
            foreach (var voiceId in voiceCandidates.Where(value => !string.IsNullOrWhiteSpace(value)))
            {
                if (attempts.Any(item =>
                    string.Equals(item.VoiceId, voiceId, StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(item.ModelId, modelId, StringComparison.OrdinalIgnoreCase)))
                {
                    continue;
                }

                attempts.Add(new TtsGenerationAttempt(voiceId.Trim(), modelId.Trim(), outputFormat));
            }
        }

        if (attempts.Count == 0)
        {
            attempts.Add(new TtsGenerationAttempt(
                TextToSpeechOptions.DefaultVoiceIdValue,
                TextToSpeechOptions.DefaultModelIdValue,
                outputFormat));
        }

        return attempts;
    }

    private static bool ShouldRetryWithNextTtsAttempt(TextToSpeechGenerationException exception)
    {
        var providerText = BuildProviderDebugText(exception);
        int? statusCode = exception.StatusCode is null ? null : (int)exception.StatusCode.Value;
        if (ContainsAny(
            providerText,
            "detected_unusual_activity",
            "unusual activity",
            "free tier usage disabled",
            "free tier",
            "abuse detectors",
            "proxy/vpn"))
        {
            return false;
        }

        if (ContainsAny(providerText, "character limit", "usage cap") ||
            (statusCode != 402 && ContainsAny(providerText, "credit", "quota", "insufficient")))
        {
            return false;
        }

        if (ContainsAny(
            providerText,
            "voice",
            "model",
            "subscription",
            "plan",
            "permission",
            "not allowed",
            "not_authorized",
            "access",
            "invalid"))
        {
            return true;
        }

        return statusCode is 400 or 402 or 403 or 404;
    }

    private static string BuildProviderDebugText(TextToSpeechGenerationException exception)
        => string.Join(
                " ",
                exception.ProviderErrorCode,
                exception.ProviderErrorMessage,
                exception.ResponseBody,
                exception.Message)
            .ToLowerInvariant();

    private static bool ContainsAny(string value, params string[] markers)
        => markers.Any(marker => value.Contains(marker, StringComparison.OrdinalIgnoreCase));

    private static bool ShouldGenerate(AudioGuide? existing, PoiAudioBulkGenerationRequest request)
    {
        if (existing is null)
        {
            return request.IncludeMissing;
        }

        var generationStatus = AudioGuideCatalog.NormalizeGenerationStatus(existing.GenerationStatus);
        if (existing.IsOutdated || string.Equals(generationStatus, AudioGuideCatalog.GenerationStatusOutdated, StringComparison.OrdinalIgnoreCase))
        {
            return request.IncludeOutdated;
        }

        if (string.Equals(generationStatus, AudioGuideCatalog.GenerationStatusFailed, StringComparison.OrdinalIgnoreCase))
        {
            return request.IncludeFailed;
        }

        if (!AudioGuideCatalog.IsReadyForPlayback(existing))
        {
            return request.IncludeMissing;
        }

        return false;
    }

    private IReadOnlyList<string> GetGenerationLanguages()
    {
        var settings = repository.GetSettings();
        var languageCodes = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            PremiumAccessCatalog.NormalizeLanguageCode(settings.DefaultLanguage),
            PremiumAccessCatalog.NormalizeLanguageCode(settings.FallbackLanguage)
        };

        foreach (var languageCode in settings.SupportedLanguages)
        {
            if (!string.IsNullOrWhiteSpace(languageCode))
            {
                languageCodes.Add(PremiumAccessCatalog.NormalizeLanguageCode(languageCode));
            }
        }

        return languageCodes
            .Where(code => !string.IsNullOrWhiteSpace(code))
            .OrderBy(code => code, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private void EnsureLanguageIsSupported(string languageCode)
    {
        var supported = GetGenerationLanguages();
        if (!supported.Contains(languageCode, StringComparer.OrdinalIgnoreCase))
        {
            throw new ApiBadRequestException("Ngon ngu duoc yeu cau chua nam trong cau hinh ho tro audio.");
        }
    }

    private AudioGuide SaveGuide(string? id, AdminRequestContext actor, AudioGuideUpsertRequest request)
        => repository.SaveAudioGuide(id, request with { UpdatedBy = actor.Name }, actor);

    private static string CreateTextHash(string languageCode, string transcriptText)
    {
        var payload = $"{languageCode}\u001f{transcriptText.Trim()}";
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(payload));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static string CollapseExceptionMessage(Exception exception)
        => string.Join(
            " ",
            (exception.Message ?? string.Empty)
                .Split(['\r', '\n', '\t'], StringSplitOptions.RemoveEmptyEntries))
            .Trim();

    private static string BuildGenerationFailureMessage(Exception exception)
    {
        var collapsed = CollapseExceptionMessage(exception);
        if (exception is TextToSpeechGenerationException ttsException)
        {
            if (ContainsAny(BuildProviderDebugText(ttsException), "detected_unusual_activity", "unusual activity"))
            {
                return "ElevenLabs đang từ chối Free Tier vì phát hiện unusual activity. Hãy dùng ElevenLabs API key trả phí hoặc tắt VPN/proxy theo hướng dẫn của ElevenLabs rồi generate lại.";
            }

            var details = new List<string> { collapsed };
            if (ttsException.StatusCode is not null)
            {
                details.Add($"providerStatusCode={(int)ttsException.StatusCode.Value}");
            }

            if (!string.IsNullOrWhiteSpace(ttsException.ProviderErrorCode))
            {
                details.Add($"providerErrorCode={ttsException.ProviderErrorCode}");
            }

            if (!string.IsNullOrWhiteSpace(ttsException.ProviderErrorMessage))
            {
                details.Add($"providerErrorMessage={ttsException.ProviderErrorMessage}");
            }

            if (!string.IsNullOrWhiteSpace(ttsException.ResponseBody) &&
                string.IsNullOrWhiteSpace(ttsException.ProviderErrorMessage))
            {
                details.Add($"providerResponseBody={ttsException.ResponseBody}");
            }

            collapsed = string.Join(" ", details);
        }

        return collapsed.Length <= 1900
            ? collapsed
            : collapsed[..1900] + "...";
    }

    private sealed record TtsGenerationAttempt(string VoiceId, string ModelId, string OutputFormat);
}
