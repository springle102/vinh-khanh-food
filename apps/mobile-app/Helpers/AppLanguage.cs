using System.Globalization;

namespace VinhKhanh.MobileApp.Helpers;

public sealed record AppLanguageDefinition(string Code, string DisplayName, string Flag, string CultureName);

public static class AppLanguage
{
    public const string DefaultLanguage = "vi";
    public const string FallbackLanguage = "en";

    private static readonly IReadOnlyList<AppLanguageDefinition> Definitions =
    [
        new("vi", "Ti\u1EBFng Vi\u1EC7t", "\uD83C\uDDFB\uD83C\uDDF3", "vi-VN"),
        new("en", "English", "\uD83C\uDDFA\uD83C\uDDF8", "en-US"),
        new("zh-CN", "\u4E2D\u6587", "\uD83C\uDDE8\uD83C\uDDF3", "zh-CN"),
        new("ko", "\uD55C\uAD6D\uC5B4", "\uD83C\uDDF0\uD83C\uDDF7", "ko-KR"),
        new("ja", "\u65E5\u672C\u8A9E", "\uD83C\uDDEF\uD83C\uDDF5", "ja-JP"),
        new("fr", "Fran\u00E7ais", "\uD83C\uDDEB\uD83C\uDDF7", "fr-FR")
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
            "vi-VN" => "vi",
            "zh" => "zh-CN",
            "zh-Hans" => "zh-CN",
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
            case "vi":
                AddCandidate(candidates, "vi-VN");
                break;
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
