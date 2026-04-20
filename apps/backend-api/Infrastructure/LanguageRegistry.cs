namespace VinhKhanh.BackendApi.Infrastructure;

public sealed record SupportedLanguageDefinition(
    string InternalCode,
    string DisplayName,
    string Locale,
    string TranslationCode,
    string TtsProviderCode,
    string StorageCode,
    string DefaultVoiceId,
    string DefaultModelId,
    string? FallbackVoiceId = null,
    string? FallbackModelId = null);

public static class LanguageRegistry
{
    public const string DefaultLanguageCode = "vi";
    public const string SimplifiedChineseCode = "zh-CN";
    public const string DefaultTtsModelId = TextToSpeechOptions.DefaultModelIdValue;

    private static readonly IReadOnlyList<SupportedLanguageDefinition> Definitions =
    [
        new("vi", "Tiếng Việt", "vi-VN", "vi", "vi", "vi", "JBFqnCBsd6RMkjVDRZzb", DefaultTtsModelId),
        new("en", "English", "en-US", "en", "en", "en", "EXAVITQu4vr4xnSDxMaL", DefaultTtsModelId),
        new(SimplifiedChineseCode, "中文", "zh-CN", "zh-CN", "zh", "zh-CN", TextToSpeechOptions.DefaultVoiceIdValue, DefaultTtsModelId),
        new("ko", "한국어", "ko-KR", "ko", "ko", "ko", "onwK4e9ZLuTAKqWW03F9", DefaultTtsModelId),
        new("ja", "日本語", "ja-JP", "ja", "ja", "ja", "XrExE9yKIg1WjnnlVkGX", DefaultTtsModelId)
    ];

    private static readonly IReadOnlyDictionary<string, SupportedLanguageDefinition> DefinitionsByInternalCode =
        Definitions.ToDictionary(item => item.InternalCode, StringComparer.OrdinalIgnoreCase);

    private static readonly IReadOnlyDictionary<string, string> Aliases =
        BuildAliasMap();

    public static IReadOnlyList<SupportedLanguageDefinition> SupportedLanguages => Definitions;

    public static string NormalizeInternalCode(string? languageCode)
    {
        if (string.IsNullOrWhiteSpace(languageCode))
        {
            return DefaultLanguageCode;
        }

        var key = NormalizeAliasKey(languageCode);
        return Aliases.TryGetValue(key, out var internalCode)
            ? internalCode
            : ResolveBaseLanguageAlias(key);
    }

    public static string NormalizeLookupKey(string? languageCode)
    {
        var definition = GetDefinition(languageCode);
        return definition.InternalCode == SimplifiedChineseCode
            ? "zh"
            : definition.InternalCode.ToLowerInvariant();
    }

    public static bool CodesMatch(string? left, string? right)
        => string.Equals(
            NormalizeLookupKey(left),
            NormalizeLookupKey(right),
            StringComparison.OrdinalIgnoreCase);

    public static SupportedLanguageDefinition GetDefinition(string? languageCode)
    {
        var internalCode = NormalizeInternalCode(languageCode);
        return DefinitionsByInternalCode.TryGetValue(internalCode, out var definition)
            ? definition
            : DefinitionsByInternalCode[DefaultLanguageCode];
    }

    public static bool IsSupported(string? languageCode)
        => DefinitionsByInternalCode.ContainsKey(NormalizeInternalCode(languageCode));

    public static string GetTranslationCode(string? languageCode)
        => GetDefinition(languageCode).TranslationCode;

    public static string GetTtsProviderCode(string? languageCode)
        => GetDefinition(languageCode).TtsProviderCode;

    public static string GetLocale(string? languageCode)
        => GetDefinition(languageCode).Locale;

    public static string GetStorageCode(string? languageCode)
        => GetDefinition(languageCode).StorageCode;

    private static IReadOnlyDictionary<string, string> BuildAliasMap()
    {
        var aliases = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var definition in Definitions)
        {
            aliases[NormalizeAliasKey(definition.InternalCode)] = definition.InternalCode;
            aliases[NormalizeAliasKey(definition.Locale)] = definition.InternalCode;
            aliases[NormalizeAliasKey(definition.TranslationCode)] = definition.InternalCode;
            aliases[NormalizeAliasKey(definition.TtsProviderCode)] = definition.InternalCode;
        }

        aliases["zh"] = SimplifiedChineseCode;
        aliases["zh-cn"] = SimplifiedChineseCode;
        aliases["zh-hans"] = SimplifiedChineseCode;
        aliases["zh-sg"] = SimplifiedChineseCode;
        aliases["zh-tw"] = SimplifiedChineseCode;
        aliases["zh-hant"] = SimplifiedChineseCode;

        return aliases;
    }

    private static string NormalizeAliasKey(string value)
        => value.Trim().Replace('_', '-').ToLowerInvariant();

    private static string ResolveBaseLanguageAlias(string normalizedKey)
    {
        var baseLanguage = normalizedKey
            .Split('-', 2, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .FirstOrDefault();
        if (string.IsNullOrWhiteSpace(baseLanguage))
        {
            return DefaultLanguageCode;
        }

        return Aliases.TryGetValue(baseLanguage, out var internalCode)
            ? internalCode
            : baseLanguage;
    }
}
