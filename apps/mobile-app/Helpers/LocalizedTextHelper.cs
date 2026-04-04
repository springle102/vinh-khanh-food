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

        foreach (var candidate in AppLanguage.GetCandidateCodes(languageCode))
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

}
