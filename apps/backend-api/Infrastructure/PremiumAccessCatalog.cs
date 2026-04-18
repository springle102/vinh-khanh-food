namespace VinhKhanh.BackendApi.Infrastructure;

// Kept as a compatibility language helper; premium language gating has been removed.
public static class PremiumAccessCatalog
{
    private static readonly IReadOnlyList<string> SupportedLanguageList = ["vi", "en", "zh-CN", "ko", "ja"];
    private static readonly HashSet<string> SupportedLanguageSet = new(SupportedLanguageList, StringComparer.OrdinalIgnoreCase);

    public static IReadOnlyList<string> FreeLanguages => SupportedLanguageList;

    public static IReadOnlyList<string> PremiumLanguages => [];

    public static string NormalizeLanguageCode(string? languageCode)
    {
        if (string.IsNullOrWhiteSpace(languageCode))
        {
            return "vi";
        }

        return languageCode.Trim() switch
        {
            "vi-VN" => "vi",
            "en-US" => "en",
            "zh" => "zh-CN",
            "zh-Hans" => "zh-CN",
            "ko-KR" => "ko",
            "ja-JP" => "ja",
            _ => languageCode.Trim()
        };
    }

    public static bool RequiresPremium(string? languageCode) => false;

    public static bool IsSupportedLanguage(string? languageCode)
        => SupportedLanguageSet.Contains(NormalizeLanguageCode(languageCode));

    public static bool CanUseLanguage(bool isPremiumUser, string? languageCode)
        => IsSupportedLanguage(languageCode);

    public static IReadOnlyList<string> GetAllowedLanguages(bool isPremiumUser)
        => SupportedLanguageList;
}
