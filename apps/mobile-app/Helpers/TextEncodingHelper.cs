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
        if (!CouldBenefitFromLegacyDecode(trimmed))
        {
            return trimmed;
        }

        var current = trimmed;
        for (var attempt = 0; attempt < 3; attempt += 1)
        {
            var decoded = TryDecodeLegacyUtf8(current);
            if (string.IsNullOrWhiteSpace(decoded) ||
                string.Equals(decoded, current, StringComparison.Ordinal))
            {
                break;
            }

            if (GetSuspiciousScore(decoded) >= GetSuspiciousScore(current))
            {
                break;
            }

            current = decoded.Trim();
            if (!CouldBenefitFromLegacyDecode(current))
            {
                break;
            }
        }

        return current;
    }

    private static bool CouldBenefitFromLegacyDecode(string value)
    {
        foreach (var character in value)
        {
            if (character == '\uFFFD')
            {
                return true;
            }

            if (character is >= '\u0080' and <= '\u00FF')
            {
                return true;
            }

            if (char.IsControl(character) && character is not '\r' and not '\n' and not '\t')
            {
                return true;
            }
        }

        return false;
    }

    private static string TryDecodeLegacyUtf8(string value)
    {
        try
        {
            var bytes = LegacyEncoding.GetBytes(value);
            return Encoding.UTF8.GetString(bytes);
        }
        catch
        {
            return value;
        }
    }

    private static Encoding CreateLegacyEncoding()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        return Encoding.GetEncoding(1252);
    }

    private static bool LooksMisencoded(string value)
        => GetSuspiciousScore(value) >= 12;

    private static int GetSuspiciousScore(string value)
    {
        var score = 0;
        var latinSupplementCount = 0;
        var latinSupplementRun = 0;

        foreach (var marker in MojibakeMarkers)
        {
            if (value.Contains(marker, StringComparison.Ordinal))
            {
                score += marker.Length == 1 ? 4 : 10;
            }
        }

        for (var index = 0; index < value.Length; index += 1)
        {
            var character = value[index];
            if (character == '\uFFFD')
            {
                score += 25;
                continue;
            }

            if (character is >= '\u0080' and <= '\u00BF')
            {
                score += 4;
            }

            if (index + 1 < value.Length &&
                IsLikelyMojibakeLead(character) &&
                IsLikelyMojibakeTrail(value[index + 1]))
            {
                score += 12;
            }

            if (character is >= '\u00C0' and <= '\u00FF')
            {
                latinSupplementCount += 1;
                latinSupplementRun += 1;

                if (latinSupplementRun >= 3)
                {
                    score += 6;
                }

                continue;
            }

            latinSupplementRun = 0;

            if (char.IsControl(character) && character is not '\r' and not '\n' and not '\t')
            {
                score += 20;
            }
        }

        if (latinSupplementCount >= 4)
        {
            score += latinSupplementCount * 2;
        }

        return score;
    }

    private static bool IsLikelyMojibakeLead(char value)
        => value is >= '\u00C0' and <= '\u00FF';

    private static bool IsLikelyMojibakeTrail(char value)
        => value is >= '\u0080' and <= '\u00BF';
}
