using Microsoft.Extensions.Configuration;

namespace VinhKhanh.BackendApi.Infrastructure;

public sealed class TextToSpeechOptions
{
    public const string ApiKeyConfigKey = "ELEVENLABS_API_KEY";
    public const string DefaultVoiceIdConfigKey = "ELEVENLABS_DEFAULT_VOICE_ID";
    public const string ModelIdConfigKey = "ELEVENLABS_MODEL_ID";
    public const string OutputFormatConfigKey = "ELEVENLABS_OUTPUT_FORMAT";
    public const string CacheDurationMinutesConfigKey = "ELEVENLABS_CACHE_DURATION_MINUTES";
    public const string AudioStorageRootConfigKey = "AUDIO_STORAGE_ROOT";
    public const string AudioPublicBasePathConfigKey = "AUDIO_PUBLIC_BASE_PATH";
    public const string AudioPublicBaseUrlConfigKey = "AUDIO_PUBLIC_BASE_URL";
    public const string AutoRegenerateWhenTextChangesConfigKey = "AUDIO_AUTO_REGENERATE_WHEN_TEXT_CHANGES";
    public const string AutoGenerateWhenPoiSavedConfigKey = "AUDIO_AUTO_GENERATE_WHEN_POI_SAVED";

    public const string DefaultVoiceIdValue = "JBFqnCBsd6RMkjVDRZzb";
    public const string DefaultModelIdValue = "eleven_flash_v2_5";
    public const string DefaultOutputFormatValue = "mp3_44100_128";
    public const int DefaultCacheDurationMinutesValue = 120;
    public const string DefaultAudioStorageRootValue = "storage/audio";
    public const string DefaultAudioPublicBasePathValue = "/storage/audio";

    public string? ApiKey { get; set; }
    public string DefaultVoiceId { get; set; } = DefaultVoiceIdValue;
    public string ModelId { get; set; } = DefaultModelIdValue;
    public string OutputFormat { get; set; } = DefaultOutputFormatValue;
    public int CacheDurationMinutes { get; set; } = DefaultCacheDurationMinutesValue;
    public string AudioStorageRoot { get; set; } = DefaultAudioStorageRootValue;
    public string AudioPublicBasePath { get; set; } = DefaultAudioPublicBasePathValue;
    public string? AudioPublicBaseUrl { get; set; }
    public bool AutoRegenerateWhenTextChanges { get; set; } = true;
    public bool AutoGenerateWhenPoiSaved { get; set; }
    public Dictionary<string, string> VoiceIdsByLanguage { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    public static void ApplyConfiguration(TextToSpeechOptions options, IConfiguration configuration)
    {
        options.ApiKey = ResolveValue(configuration, ApiKeyConfigKey, options.ApiKey, null);
        options.DefaultVoiceId = ResolveValue(configuration, DefaultVoiceIdConfigKey, options.DefaultVoiceId, DefaultVoiceIdValue)!;
        options.ModelId = ResolveValue(configuration, ModelIdConfigKey, options.ModelId, DefaultModelIdValue)!;
        options.OutputFormat = ResolveValue(configuration, OutputFormatConfigKey, options.OutputFormat, DefaultOutputFormatValue)!;
        options.CacheDurationMinutes = ResolvePositiveInt(
            configuration,
            CacheDurationMinutesConfigKey,
            options.CacheDurationMinutes,
            DefaultCacheDurationMinutesValue);
        options.AudioStorageRoot = ResolveValue(
            configuration,
            AudioStorageRootConfigKey,
            options.AudioStorageRoot,
            DefaultAudioStorageRootValue)!;
        options.AudioPublicBasePath = ResolveValue(
            configuration,
            AudioPublicBasePathConfigKey,
            options.AudioPublicBasePath,
            DefaultAudioPublicBasePathValue)!;
        options.AudioPublicBaseUrl = ResolveValue(
            configuration,
            AudioPublicBaseUrlConfigKey,
            options.AudioPublicBaseUrl,
            null);
        options.AutoRegenerateWhenTextChanges = ResolveBool(
            configuration,
            AutoRegenerateWhenTextChangesConfigKey,
            options.AutoRegenerateWhenTextChanges,
            true);
        options.AutoGenerateWhenPoiSaved = ResolveBool(
            configuration,
            AutoGenerateWhenPoiSavedConfigKey,
            options.AutoGenerateWhenPoiSaved,
            false);

        var configuredVoiceIds = configuration
            .GetSection("ElevenLabs:VoiceIdsByLanguage")
            .GetChildren()
            .Where(section => !string.IsNullOrWhiteSpace(section.Key) && !string.IsNullOrWhiteSpace(section.Value))
            .GroupBy(section => LanguageRegistry.NormalizeInternalCode(section.Key), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => group.Last().Value!.Trim(),
                StringComparer.OrdinalIgnoreCase);
        if (configuredVoiceIds.Count > 0)
        {
            options.VoiceIdsByLanguage = new Dictionary<string, string>(configuredVoiceIds, StringComparer.OrdinalIgnoreCase);
        }
    }

    public string ResolveVoiceId(string? languageCode)
        => ResolveVoiceCandidates(languageCode).First();

    public IReadOnlyList<string> ResolveVoiceCandidates(string? languageCode, string? requestedVoiceId = null)
    {
        if (!string.IsNullOrWhiteSpace(requestedVoiceId))
        {
            return new[] { requestedVoiceId.Trim() };
        }

        var definition = LanguageRegistry.GetDefinition(languageCode);
        var candidates = new List<string>();
        if (VoiceIdsByLanguage.TryGetValue(definition.InternalCode, out var configuredVoiceId) &&
            !string.IsNullOrWhiteSpace(configuredVoiceId))
        {
            AddCandidate(candidates, configuredVoiceId);
        }

        AddCandidate(candidates, definition.DefaultVoiceId);
        AddCandidate(candidates, definition.FallbackVoiceId);
        AddCandidate(candidates, DefaultVoiceId);
        AddCandidate(candidates, DefaultVoiceIdValue);

        return candidates.Count == 0
            ? new[] { DefaultVoiceIdValue }
            : candidates;
    }

    public string ResolveModelId(string? languageCode, string? requestedModelId = null)
        => ResolveModelCandidates(languageCode, requestedModelId).First();

    public IReadOnlyList<string> ResolveModelCandidates(string? languageCode, string? requestedModelId = null)
    {
        if (!string.IsNullOrWhiteSpace(requestedModelId))
        {
            return new[] { requestedModelId.Trim() };
        }

        var definition = LanguageRegistry.GetDefinition(languageCode);
        var candidates = new List<string>();
        AddCandidate(candidates, definition.DefaultModelId);
        AddCandidate(candidates, definition.FallbackModelId);
        AddCandidate(candidates, ModelId);
        AddCandidate(candidates, DefaultModelIdValue);

        return candidates.Count == 0
            ? new[] { DefaultModelIdValue }
            : candidates;
    }

    private static void AddCandidate(ICollection<string> candidates, string? value)
    {
        var normalized = value?.Trim();
        if (string.IsNullOrWhiteSpace(normalized) ||
            candidates.Contains(normalized, StringComparer.OrdinalIgnoreCase))
        {
            return;
        }

        candidates.Add(normalized);
    }

    private static string? ResolveValue(
        IConfiguration configuration,
        string configKey,
        string? currentValue,
        string? fallbackValue)
    {
        var resolved = FirstNonEmpty(
            Environment.GetEnvironmentVariable(configKey),
            configuration[configKey],
            configKey switch
            {
                ApiKeyConfigKey => configuration["ElevenLabs:ApiKey"],
                DefaultVoiceIdConfigKey => configuration["ElevenLabs:DefaultVoiceId"],
                ModelIdConfigKey => configuration["ElevenLabs:ModelId"],
                OutputFormatConfigKey => configuration["ElevenLabs:OutputFormat"],
                AudioStorageRootConfigKey => configuration["AudioGeneration:StorageRoot"],
                AudioPublicBasePathConfigKey => configuration["AudioGeneration:PublicBasePath"],
                AudioPublicBaseUrlConfigKey => configuration["AudioGeneration:PublicBaseUrl"],
                _ => null
            },
            currentValue,
            fallbackValue);

        return string.IsNullOrWhiteSpace(resolved)
            ? fallbackValue
            : resolved.Trim();
    }

    private static string? FirstNonEmpty(params string?[] values)
        => values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));

    private static int ResolvePositiveInt(
        IConfiguration configuration,
        string configKey,
        int currentValue,
        int fallbackValue)
    {
        var rawValue = FirstNonEmpty(
            Environment.GetEnvironmentVariable(configKey),
            configuration[configKey],
            configKey switch
            {
                CacheDurationMinutesConfigKey => configuration["ElevenLabs:CacheDurationMinutes"],
                _ => null
            },
            currentValue > 0 ? currentValue.ToString() : null,
            fallbackValue.ToString());

        return int.TryParse(rawValue, out var parsed) && parsed > 0
            ? parsed
            : fallbackValue;
    }

    private static bool ResolveBool(
        IConfiguration configuration,
        string configKey,
        bool currentValue,
        bool fallbackValue)
    {
        var rawValue = FirstNonEmpty(
            Environment.GetEnvironmentVariable(configKey),
            configuration[configKey],
            configKey switch
            {
                AutoRegenerateWhenTextChangesConfigKey => configuration["AudioGeneration:AutoRegenerateWhenTextChanges"],
                AutoGenerateWhenPoiSavedConfigKey => configuration["AudioGeneration:AutoGenerateWhenPoiSaved"],
                _ => null
            },
            currentValue.ToString(),
            fallbackValue.ToString());

        return bool.TryParse(rawValue, out var parsed)
            ? parsed
            : fallbackValue;
    }
}
