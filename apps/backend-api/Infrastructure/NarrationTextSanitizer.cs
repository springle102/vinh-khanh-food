using System.Text.RegularExpressions;

namespace VinhKhanh.BackendApi.Infrastructure;

internal static partial class NarrationTextSanitizer
{
    public static string Clean(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var normalized = value.Replace("\r\n", "\n").Trim();
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return string.Empty;
        }

        var lines = normalized
            .Split('\n')
            .Select(line => line.TrimEnd())
            .ToList();

        while (lines.Count > 0 && string.IsNullOrWhiteSpace(lines[0]))
        {
            lines.RemoveAt(0);
        }

        if (lines.Count > 0 && TranslationHeaderLineRegex().IsMatch(lines[0].Trim()))
        {
            lines.RemoveAt(0);

            while (lines.Count > 0 && string.IsNullOrWhiteSpace(lines[0]))
            {
                lines.RemoveAt(0);
            }
        }

        normalized = string.Join("\n", lines).Trim();
        normalized = TranslationHeaderInlineRegex().Replace(normalized, string.Empty).Trim();

        return normalized;
    }

    [GeneratedRegex(@"^\*{0,2}\s*[\p{L}\s-]+translation\s*:\s*\*{0,2}\s*$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex TranslationHeaderLineRegex();

    [GeneratedRegex(@"^\*{0,2}\s*[\p{L}\s-]+translation\s*:\s*\*{0,2}\s*", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex TranslationHeaderInlineRegex();
}
