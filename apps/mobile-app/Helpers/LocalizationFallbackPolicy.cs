using System.Globalization;
using System.Text;

namespace VinhKhanh.MobileApp.Helpers;

public static class LocalizationFallbackPolicy
{
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
        => AppLanguage.GetCandidateCodes(languageCode);

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

        return IsSourceLanguage(languageCode) || !LooksLikeVietnameseContent(value);
    }

    public static string SourceTextForLanguage(string? value, string? requestedLanguageCode)
        => CanUseSourceLanguageText(requestedLanguageCode) && !string.IsNullOrWhiteSpace(value)
            ? value.Trim()
            : string.Empty;

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
        var padded = $" {string.Join(' ', romanized.Split([' ', '\r', '\n', '\t', '.', ',', ';', ':', '-', '_', '/', '\\', '(', ')', '[', ']'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))} ";
        return VietnameseRomanizedMarkers.Any(marker =>
            padded.Contains($" {marker} ", StringComparison.OrdinalIgnoreCase));
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
