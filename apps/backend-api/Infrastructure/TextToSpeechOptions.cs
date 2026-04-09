using Microsoft.Extensions.Configuration;

namespace VinhKhanh.BackendApi.Infrastructure;

public sealed class TextToSpeechOptions
{
    public const string ApiKeyConfigKey = "ELEVENLABS_API_KEY";
    public const string DefaultVoiceIdConfigKey = "ELEVENLABS_DEFAULT_VOICE_ID";
    public const string ModelIdConfigKey = "ELEVENLABS_MODEL_ID";
    public const string OutputFormatConfigKey = "ELEVENLABS_OUTPUT_FORMAT";

    public const string DefaultVoiceIdValue = "JBFqnCBsd6RMkjVDRZzb";
    public const string DefaultModelIdValue = "eleven_v3";
    public const string DefaultOutputFormatValue = "mp3_44100_128";

    public string? ApiKey { get; set; }
    public string DefaultVoiceId { get; set; } = DefaultVoiceIdValue;
    public string ModelId { get; set; } = DefaultModelIdValue;
    public string OutputFormat { get; set; } = DefaultOutputFormatValue;

    public static void ApplyConfiguration(TextToSpeechOptions options, IConfiguration configuration)
    {
        options.ApiKey = ResolveValue(configuration, ApiKeyConfigKey, options.ApiKey, null);
        options.DefaultVoiceId = ResolveValue(configuration, DefaultVoiceIdConfigKey, options.DefaultVoiceId, DefaultVoiceIdValue)!;
        options.ModelId = ResolveValue(configuration, ModelIdConfigKey, options.ModelId, DefaultModelIdValue)!;
        options.OutputFormat = ResolveValue(configuration, OutputFormatConfigKey, options.OutputFormat, DefaultOutputFormatValue)!;
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
}
