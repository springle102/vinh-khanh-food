using System.Globalization;
using System.Text;

namespace VinhKhanh.BackendApi.Infrastructure;

internal static class LocalizationContentPolicy
{
    private const int StrongRomanizedMarkerThreshold = 3;
    private const int ShortTextRomanizedMarkerThreshold = 2;
    private const int ShortTextWordThreshold = 12;

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

    public static bool IsSourceLanguage(string? languageCode)
        => string.Equals(
            PremiumAccessCatalog.NormalizeLanguageCode(languageCode),
            "vi",
            StringComparison.OrdinalIgnoreCase);

    public static bool IsUsableTextForLanguage(string? value, string? languageCode)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        return IsSourceLanguage(languageCode) || !LooksLikeVietnameseContent(value);
    }

    public static string? CleanForLanguage(string? value, string? languageCode)
    {
        var cleaned = NarrationTextSanitizer.Clean(value);
        if (string.IsNullOrWhiteSpace(cleaned))
        {
            return null;
        }

        return IsUsableTextForLanguage(cleaned, languageCode) ? cleaned : null;
    }

    public static bool LooksLikeVietnameseContent(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var cleaned = NarrationTextSanitizer.Clean(value);
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
