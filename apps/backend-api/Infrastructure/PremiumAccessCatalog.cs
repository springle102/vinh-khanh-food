namespace VinhKhanh.BackendApi.Infrastructure;

public static class PremiumAccessCatalog
{
    public const int DefaultPremiumPriceUsd = 10;

    private static readonly IReadOnlyList<string> FreeLanguageList = ["vi", "en"];
    private static readonly IReadOnlyList<string> PremiumLanguageList = ["zh-CN", "ko", "ja"];
    private static readonly HashSet<string> FreeLanguageSet = new(FreeLanguageList, StringComparer.OrdinalIgnoreCase);
    private static readonly HashSet<string> PremiumLanguageSet = new(PremiumLanguageList, StringComparer.OrdinalIgnoreCase);

    public static IReadOnlyList<string> FreeLanguages => FreeLanguageList;

    public static IReadOnlyList<string> PremiumLanguages => PremiumLanguageList;

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

    public static bool RequiresPremium(string? languageCode)
        => PremiumLanguageSet.Contains(NormalizeLanguageCode(languageCode));

    public static bool IsSupportedLanguage(string? languageCode)
    {
        var normalized = NormalizeLanguageCode(languageCode);
        return FreeLanguageSet.Contains(normalized) || PremiumLanguageSet.Contains(normalized);
    }

    public static bool CanUseLanguage(bool isPremiumUser, string? languageCode)
    {
        var normalized = NormalizeLanguageCode(languageCode);
        return FreeLanguageSet.Contains(normalized) ||
               (isPremiumUser && PremiumLanguageSet.Contains(normalized));
    }

    public static IReadOnlyList<string> GetAllowedLanguages(bool isPremiumUser)
        => isPremiumUser
            ? [.. FreeLanguageList, .. PremiumLanguageList]
            : [.. FreeLanguageList];
}
