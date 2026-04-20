namespace VinhKhanh.BackendApi.Infrastructure;

// Kept as a compatibility language helper; premium language gating has been removed.
public static class PremiumAccessCatalog
{
    private static readonly IReadOnlyList<string> SupportedLanguageList =
        LanguageRegistry.SupportedLanguages.Select(item => item.InternalCode).ToList();

    public static IReadOnlyList<string> FreeLanguages => SupportedLanguageList;

    public static IReadOnlyList<string> PremiumLanguages => [];

    public static string NormalizeLanguageCode(string? languageCode)
        => LanguageRegistry.NormalizeInternalCode(languageCode);

    public static string NormalizeLanguageLookupKey(string? languageCode)
        => LanguageRegistry.NormalizeLookupKey(languageCode);

    public static bool LanguageCodesMatch(string? left, string? right)
        => LanguageRegistry.CodesMatch(left, right);

    public static bool RequiresPremium(string? languageCode) => false;

    public static bool IsSupportedLanguage(string? languageCode)
        => LanguageRegistry.IsSupported(languageCode);

    public static bool CanUseLanguage(bool isPremiumUser, string? languageCode)
        => IsSupportedLanguage(languageCode);

    public static IReadOnlyList<string> GetAllowedLanguages(bool isPremiumUser)
        => SupportedLanguageList;
}
