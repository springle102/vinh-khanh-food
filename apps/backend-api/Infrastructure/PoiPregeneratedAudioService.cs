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
            .FirstOrDefault(item => string.Equals(item.LanguageCode, normalizedLanguageCode, StringComparison.OrdinalIgnoreCase));
        var options = optionsAccessor.Value;
        var effectiveLanguageCode = PremiumAccessCatalog.NormalizeLanguageCode(narration.EffectiveLanguageCode);
        var voiceId = string.IsNullOrWhiteSpace(request.VoiceId)
            ? options.ResolveVoiceId(effectiveLanguageCode)
            : request.VoiceId.Trim();
        var modelId = string.IsNullOrWhiteSpace(request.ModelId)
            ? options.ModelId
            : request.ModelId.Trim();
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
            string.Equals(existing.VoiceId, voiceId, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(existing.ModelId, modelId, StringComparison.OrdinalIgnoreCase) &&
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
                "Generating POI audio via ElevenLabs. poiId={PoiId}; requestedLanguageCode={RequestedLanguageCode}; effectiveLanguageCode={EffectiveLanguageCode}; voiceId={VoiceId}; modelId={ModelId}; outputFormat={OutputFormat}",
                poiId,
                normalizedLanguageCode,
                effectiveLanguageCode,
                voiceId,
                modelId,
                outputFormat);

            var ttsAudio = await textToSpeechService.GenerateAudioAsync(
                new TextToSpeechRequest(
                    transcriptText,
                    effectiveLanguageCode,
                    voiceId,
                    modelId,
                    outputFormat),
                cancellationToken);
            var storedFile = await generatedAudioStorageService.SavePoiAudioAsync(
                poiId,
                normalizedLanguageCode,
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
                "POI audio generated successfully. poiId={PoiId}; languageCode={LanguageCode}; audioGuideId={AudioGuideId}; filePath={FilePath}",
                poiId,
                normalizedLanguageCode,
                savedGuide.Id,
                savedGuide.AudioFilePath);

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
            logger.LogWarning(
                exception,
                "Failed to generate POI audio. poiId={PoiId}; languageCode={LanguageCode}; voiceId={VoiceId}; modelId={ModelId}",
                poiId,
                normalizedLanguageCode,
                voiceId,
                modelId);

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
                    ErrorMessage: CollapseExceptionMessage(exception),
                    IsOutdated: true,
                    VoiceType: existing?.VoiceType ?? "standard"));

            return new PoiAudioGenerationResult(
                poiId,
                normalizedLanguageCode,
                effectiveLanguageCode,
                false,
                false,
                existing is not null,
                $"Generate audio that bai: {CollapseExceptionMessage(exception)}",
                transcriptText,
                textHash,
                failedGuide);
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
                    .FirstOrDefault(item => string.Equals(item.LanguageCode, languageCode, StringComparison.OrdinalIgnoreCase));
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
}
