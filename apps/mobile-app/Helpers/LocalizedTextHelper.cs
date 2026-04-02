using System.Reflection;
using VinhKhanh.MobileApp.Models;

namespace VinhKhanh.MobileApp.Helpers;

public static class LocalizedTextHelper
{
    public static string GetLocalizedText(object? source, string? languageCode)
    {
        if (source is null)
        {
            return string.Empty;
        }

        return source switch
        {
            string text => text,
            LocalizedTextSet set => Resolve(set.Values, languageCode),
            IReadOnlyDictionary<string, string> values => Resolve(values, languageCode),
            IDictionary<string, string> values => Resolve(values.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.OrdinalIgnoreCase), languageCode),
            _ => Resolve(ReadFromObject(source), languageCode)
        };
    }

    private static string Resolve(IReadOnlyDictionary<string, string> values, string? languageCode)
    {
        if (values.Count == 0)
        {
            return string.Empty;
        }

        foreach (var candidate in GetLanguageCandidates(languageCode))
        {
            if (values.TryGetValue(candidate, out var value) && !string.IsNullOrWhiteSpace(value))
            {
                return value.Trim();
            }
        }

        return values.Values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))?.Trim() ?? string.Empty;
    }

    private static IReadOnlyDictionary<string, string> ReadFromObject(object source)
    {
        var dictionary = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var property in source.GetType().GetProperties(BindingFlags.Instance | BindingFlags.Public))
        {
            if (property.PropertyType != typeof(string) || property.GetIndexParameters().Length > 0)
            {
                continue;
            }

            var value = property.GetValue(source) as string;
            if (!string.IsNullOrWhiteSpace(value))
            {
                dictionary[property.Name] = value.Trim();
            }
        }

        return dictionary;
    }

    private static IEnumerable<string> GetLanguageCandidates(string? languageCode)
    {
        var normalized = NormalizeLanguageCode(languageCode);
        var candidates = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        AddCandidate(normalized);
        var separatorIndex = normalized.IndexOf('-');
        if (separatorIndex > 0)
        {
            AddCandidate(normalized[..separatorIndex]);
        }

        switch (normalized)
        {
            case "zh":
                AddCandidate("zh-CN");
                break;
            case "zh-CN":
                AddCandidate("zh");
                break;
            case "en-US":
                AddCandidate("en");
                break;
            case "ja-JP":
                AddCandidate("ja");
                break;
            case "ko-KR":
                AddCandidate("ko");
                break;
        }

        AddCandidate("en");
        AddCandidate("vi");

        return candidates;

        void AddCandidate(string? value)
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                candidates.Add(value.Trim());
            }
        }
    }

    private static string NormalizeLanguageCode(string? languageCode)
    {
        if (string.IsNullOrWhiteSpace(languageCode))
        {
            return "en";
        }

        return languageCode.Trim() switch
        {
            "zh" => "zh-CN",
            "fr-FR" => "fr",
            "en-US" => "en",
            "ja-JP" => "ja",
            "ko-KR" => "ko",
            _ => languageCode.Trim()
        };
    }
}
