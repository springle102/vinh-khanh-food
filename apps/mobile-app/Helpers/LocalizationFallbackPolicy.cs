using System.Globalization;
using System.Text;

namespace VinhKhanh.MobileApp.Helpers;

public static class LocalizationFallbackPolicy
{
    private const int StrongRomanizedMarkerThreshold = 3;
    private const int ShortTextRomanizedMarkerThreshold = 2;
    private const int ShortTextWordThreshold = 12;
    private static readonly string[] SourceLanguageCandidates =
    [
        AppLanguage.DefaultLanguage,
        "vi-VN"
    ];

    private static readonly string[] VietnameseMojibakeMarkers =
    [
        "\u00c3\u00a1\u00c2\u00ba",
        "\u00c3\u00a1\u00c2\u00bb",
        "\u00c3\u201e",
        "\u00c3\u2020",
        "\u00c4\u2018",
        "\u00c4\u0090",
        "\u00c6\u00b0",
        "\u00c6\u00a1"
    ];

    private static readonly string[] VietnameseRomanizedMarkers =
    [
        "nhom",
        "nguoi",
        "tang",
        "nuoc",
        "uong",
        "khach",
        "gio",
        "phut",
        "phuong",
        "quan 4",
        "quan oc",
        "mon an",
        "mon ngot",
        "dia diem",
        "hai san",
        "an vat",
        "gia dinh",
        "khach quoc te",
        "tong hop",
        "uu dai",
        "thuc don",
        "do song",
        "dac san",
        "khong gian"
    ];

    public static IReadOnlyList<string> GetDisplayTextFallbackCandidates(string? languageCode)
    {
        var normalizedLanguageCode = AppLanguage.NormalizeCode(languageCode);
        var candidates = new List<string>();

        AddCandidate(candidates, normalizedLanguageCode);

        var separatorIndex = normalizedLanguageCode.IndexOf('-');
        if (separatorIndex > 0)
        {
            AddCandidate(candidates, normalizedLanguageCode[..separatorIndex]);
        }

        if (IsSourceLanguage(normalizedLanguageCode))
        {
            foreach (var sourceLanguageCandidate in SourceLanguageCandidates)
            {
                AddCandidate(candidates, sourceLanguageCandidate);
            }
        }

        AddCandidate(candidates, AppLanguage.FallbackLanguage);
        AddCandidate(candidates, "en-US");
        return candidates;
    }

    public static bool IsSourceLanguage(string? languageCode)
        => string.Equals(
            AppLanguage.NormalizeCode(languageCode),
            AppLanguage.DefaultLanguage,
            StringComparison.OrdinalIgnoreCase);

    public static bool CanUseSourceLanguageText(string? requestedLanguageCode)
        => IsSourceLanguage(requestedLanguageCode);

    public static bool IsUsableTextForLanguage(string? value, string? languageCode)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        return IsSourceLanguage(languageCode) ||
               (!LooksLikeVietnameseContent(value) && !LooksLikeTechnicalIdentifier(value));
    }

    public static string SourceTextForLanguage(string? value, string? requestedLanguageCode)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var normalizedValue = value.Trim();
        return IsSourceLanguage(requestedLanguageCode) ||
               IsUsableTextForLanguage(normalizedValue, requestedLanguageCode)
            ? normalizedValue
            : string.Empty;
    }

    public static bool LooksLikeTechnicalIdentifier(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var trimmed = value.Trim();
        if (trimmed.Any(char.IsWhiteSpace))
        {
            return false;
        }

        var normalized = trimmed.ToLowerInvariant();
        var knownPrefixes = new[]
        {
            "runtime-",
            "food-",
            "poi-",
            "route-",
            "tour-",
            "promotion-",
            "promo-",
            "audio-",
            "media-",
            "category-"
        };

        if (!knownPrefixes.Any(prefix => normalized.StartsWith(prefix, StringComparison.Ordinal)))
        {
            return false;
        }

        var hasSeparator = trimmed.IndexOfAny(['-', '_']) >= 0;
        if (!hasSeparator)
        {
            return false;
        }

        var hasUppercase = trimmed.Any(character => character is >= 'A' and <= 'Z');
        var isAsciiSlug = trimmed.All(character =>
            char.IsLetterOrDigit(character) || character is '-' or '_');

        return isAsciiSlug && !hasUppercase;
    }

    private static void AddCandidate(ICollection<string> candidates, string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return;
        }

        if (!candidates.Contains(value, StringComparer.OrdinalIgnoreCase))
        {
            candidates.Add(value.Trim());
        }
    }

    private static bool LooksLikeVietnameseContent(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var cleaned = value.Trim();
        if (VietnameseMojibakeMarkers.Any(marker =>
                cleaned.Contains(marker, StringComparison.OrdinalIgnoreCase)))
        {
            return true;
        }

        var normalized = cleaned.Normalize(NormalizationForm.FormD);
        foreach (var character in normalized)
        {
            if (character is '\u0111' or '\u0110' or '\u0306' or '\u031b' or '\u0323' or '\u0309')
            {
                return true;
            }
        }

        var romanized = RemoveDiacritics(normalized).ToLowerInvariant();
        var words = romanized
            .Split([' ', '\r', '\n', '\t', '.', ',', ';', ':', '-', '_', '/', '\\', '(', ')', '[', ']'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (words.Length == 0)
        {
            return false;
        }

        var padded = $" {string.Join(' ', words)} ";
        var markerHitCount = VietnameseRomanizedMarkers.Count(marker =>
            padded.Contains($" {marker} ", StringComparison.OrdinalIgnoreCase));

        return markerHitCount >= StrongRomanizedMarkerThreshold ||
               (markerHitCount >= ShortTextRomanizedMarkerThreshold && words.Length <= ShortTextWordThreshold);
    }

    private static string RemoveDiacritics(string value)
    {
        var builder = new StringBuilder(value.Length);
        foreach (var character in value)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(character) == UnicodeCategory.NonSpacingMark)
            {
                continue;
            }

            builder.Append(character switch
            {
                '\u0110' => 'D',
                '\u0111' => 'd',
                _ => character
            });
        }

        return builder.ToString().Normalize(NormalizationForm.FormC);
    }
}
