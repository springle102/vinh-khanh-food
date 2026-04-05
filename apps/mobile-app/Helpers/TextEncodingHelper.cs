using System.Text;

namespace VinhKhanh.MobileApp.Helpers;

public static class TextEncodingHelper
{
    private static readonly string[] MojibakeMarkers =
    [
        "Ã",
        "Â",
        "Ä",
        "Å",
        "Æ",
        "Ç",
        "Ð",
        "Ñ",
        "Ø",
        "Ù",
        "Þ",
        "ß",
        "áº",
        "á»",
        "â€",
        "âœ",
        "ã€",
        "ãƒ",
        "äº",
        "æ°",
        "ç™",
        "è¿",
        "ì„",
        "ìŠ",
        "ë¡",
        "ë°",
        "í˜",
        "åŒ",
        "åˆ",
        "æ–",
        "ìž",
        "ë‹",
        "ë¬",
        "í•",
        "\u0090",
        "\u009d"
    ];

    private static readonly Encoding LegacyEncoding = CreateLegacyEncoding();

    public static string NormalizeDisplayText(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var trimmed = value.Trim();
        if (!LooksMisencoded(trimmed))
        {
            return trimmed;
        }

        try
        {
            var bytes = LegacyEncoding.GetBytes(trimmed);
            var decoded = Encoding.UTF8.GetString(bytes);

            return GetSuspiciousScore(decoded) < GetSuspiciousScore(trimmed)
                ? decoded.Trim()
                : trimmed;
        }
        catch
        {
            return trimmed;
        }
    }

    private static Encoding CreateLegacyEncoding()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        return Encoding.GetEncoding(1252);
    }

    private static bool LooksMisencoded(string value)
        => MojibakeMarkers.Any(marker => value.Contains(marker, StringComparison.Ordinal));

    private static int GetSuspiciousScore(string value)
    {
        var score = 0;

        foreach (var marker in MojibakeMarkers)
        {
            if (value.Contains(marker, StringComparison.Ordinal))
            {
                score += marker.Length == 1 ? 4 : 10;
            }
        }

        foreach (var character in value)
        {
            if (character == '\uFFFD')
            {
                score += 25;
                continue;
            }

            if (char.IsControl(character) && character is not '\r' and not '\n' and not '\t')
            {
                score += 20;
            }
        }

        return score;
    }
}
