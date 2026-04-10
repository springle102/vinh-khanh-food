using Microsoft.Extensions.Configuration;

namespace VinhKhanh.BackendApi.Infrastructure;

public sealed class TextToSpeechOptions
{
    public const string ApiKeyConfigKey = "ELEVENLABS_API_KEY";
    public const string DefaultVoiceIdConfigKey = "ELEVENLABS_DEFAULT_VOICE_ID";
    public const string ModelIdConfigKey = "ELEVENLABS_MODEL_ID";
    public const string OutputFormatConfigKey = "ELEVENLABS_OUTPUT_FORMAT";
    public const string CacheDurationMinutesConfigKey = "ELEVENLABS_CACHE_DURATION_MINUTES";

    public const string DefaultVoiceIdValue = "JBFqnCBsd6RMkjVDRZzb";
    public const string DefaultModelIdValue = "eleven_flash_v2_5";
    public const string DefaultOutputFormatValue = "mp3_44100_128";
    public const int DefaultCacheDurationMinutesValue = 120;

    public string? ApiKey { get; set; }
    public string DefaultVoiceId { get; set; } = DefaultVoiceIdValue;
    public string ModelId { get; set; } = DefaultModelIdValue;
    public string OutputFormat { get; set; } = DefaultOutputFormatValue;
    public int CacheDurationMinutes { get; set; } = DefaultCacheDurationMinutesValue;

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
}
