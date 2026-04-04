using System.Globalization;

namespace VinhKhanh.MobileApp.Helpers;

public sealed record AppLanguageDefinition(string Code, string DisplayName, string Flag, string CultureName);

public static class AppLanguage
{
    public const string DefaultLanguage = "vi";
    public const string FallbackLanguage = "en";

    private static readonly IReadOnlyList<AppLanguageDefinition> Definitions =
    [
        new("vi", "Tiếng Việt", "🇻🇳", "vi-VN"),
        new("en", "English", "🇬🇧", "en-US"),
        new("zh-CN", "中文", "🇨🇳", "zh-CN"),
        new("ko", "한국어", "🇰🇷", "ko-KR"),
        new("ja", "日本語", "🇯🇵", "ja-JP"),
        new("fr", "Français", "🇫🇷", "fr-FR")
    ];

    public static IReadOnlyList<AppLanguageDefinition> SupportedLanguages => Definitions;

    public static string NormalizeCode(string? languageCode)
    {
        if (string.IsNullOrWhiteSpace(languageCode))
        {
            return DefaultLanguage;
        }

        return languageCode.Trim() switch
        {
            "zh" => "zh-CN",
            "fr-FR" => "fr",
            "en-US" => "en",
            "ja-JP" => "ja",
            "ko-KR" => "ko",
            _ => languageCode.Trim()
        };
    }

    public static IReadOnlyList<string> GetCandidateCodes(string? languageCode, bool includeEnglishFallback = true)
    {
        var normalized = NormalizeCode(languageCode);
        var candidates = new List<string>();
        AddCandidate(candidates, normalized);

        var separatorIndex = normalized.IndexOf('-');
        if (separatorIndex > 0)
        {
            AddCandidate(candidates, normalized[..separatorIndex]);
        }

        switch (normalized)
        {
            case "zh":
                AddCandidate(candidates, "zh-CN");
                break;
            case "zh-CN":
                AddCandidate(candidates, "zh");
                break;
            case "en":
                AddCandidate(candidates, "en-US");
                break;
            case "ja":
                AddCandidate(candidates, "ja-JP");
                break;
            case "ko":
                AddCandidate(candidates, "ko-KR");
                break;
            case "fr":
                AddCandidate(candidates, "fr-FR");
                break;
        }

        if (includeEnglishFallback)
        {
            AddCandidate(candidates, FallbackLanguage);
            AddCandidate(candidates, "en-US");
        }

        return candidates;
    }

    public static AppLanguageDefinition GetDefinition(string? languageCode)
    {
        var normalized = NormalizeCode(languageCode);
        return Definitions.FirstOrDefault(item => string.Equals(item.Code, normalized, StringComparison.OrdinalIgnoreCase))
            ?? Definitions.First(item => string.Equals(item.Code, DefaultLanguage, StringComparison.OrdinalIgnoreCase));
    }

    public static CultureInfo CreateCulture(string? languageCode)
        => CultureInfo.GetCultureInfo(GetDefinition(languageCode).CultureName);

    private static void AddCandidate(ICollection<string> values, string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return;
        }

        if (!values.Contains(value, StringComparer.OrdinalIgnoreCase))
        {
            values.Add(value.Trim());
        }
    }
}
