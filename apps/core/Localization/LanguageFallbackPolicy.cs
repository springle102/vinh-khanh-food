namespace VinhKhanh.Core.Localization;

public static class LanguageFallbackPolicy
{
    public const string DefaultLanguage = "vi";

    public static string Normalize(string? languageCode)
    {
        var value = languageCode?.Trim();
        if (string.IsNullOrWhiteSpace(value))
        {
            return DefaultLanguage;
        }

        return value.Equals("zh", StringComparison.OrdinalIgnoreCase) ||
               value.Equals("zh-cn", StringComparison.OrdinalIgnoreCase)
            ? "zh-CN"
            : value.ToLowerInvariant();
    }

    public static string ResolveText(
        IReadOnlyDictionary<string, string> values,
        string? requestedLanguageCode,
        string? fallbackLanguageCode = null)
    {
        if (values.Count == 0)
        {
            return string.Empty;
        }

        var requested = Normalize(requestedLanguageCode);
        if (values.TryGetValue(requested, out var requestedValue) &&
            !string.IsNullOrWhiteSpace(requestedValue))
        {
            return requestedValue.Trim();
        }

        var fallback = Normalize(fallbackLanguageCode ?? DefaultLanguage);
        if (values.TryGetValue(fallback, out var fallbackValue) &&
            !string.IsNullOrWhiteSpace(fallbackValue))
        {
            return fallbackValue.Trim();
        }

        return values.Values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))?.Trim() ?? string.Empty;
    }
}
