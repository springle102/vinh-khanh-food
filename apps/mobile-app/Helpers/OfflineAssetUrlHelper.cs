namespace VinhKhanh.MobileApp.Helpers;

public static class OfflineAssetUrlHelper
{
    public static IEnumerable<string> BuildLookupKeys(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            yield break;
        }

        var keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var key in EnumerateLookupKeys(value.Trim()))
        {
            if (!string.IsNullOrWhiteSpace(key) && keys.Add(key))
            {
                yield return key;
            }
        }
    }

    public static bool TryResolveAssetPath(
        IReadOnlyDictionary<string, string> assetMap,
        string? value,
        out string localPath)
    {
        localPath = string.Empty;
        if (assetMap.Count == 0 || string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        foreach (var key in BuildLookupKeys(value))
        {
            if (assetMap.TryGetValue(key, out var mappedPath) &&
                !string.IsNullOrWhiteSpace(mappedPath))
            {
                localPath = mappedPath;
                return true;
            }
        }

        return false;
    }

    private static IEnumerable<string> EnumerateLookupKeys(string value)
    {
        yield return value;

        if (Uri.TryCreate(value, UriKind.Absolute, out var absoluteUri))
        {
            yield return absoluteUri.AbsoluteUri;
            foreach (var pathKey in EnumeratePathLookupKeys(absoluteUri.AbsolutePath))
            {
                yield return pathKey;
            }

            yield break;
        }

        foreach (var pathKey in EnumeratePathLookupKeys(value))
        {
            yield return pathKey;
        }
    }

    private static IEnumerable<string> EnumeratePathLookupKeys(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            yield break;
        }

        var normalizedPath = path.Replace('\\', '/').Trim();
        yield return normalizedPath;

        var unescapedPath = Uri.UnescapeDataString(normalizedPath);
        yield return unescapedPath;

        if (!normalizedPath.StartsWith("/", StringComparison.Ordinal))
        {
            yield return $"/{normalizedPath}";
        }
        else
        {
            yield return normalizedPath.TrimStart('/');
        }

        if (!string.Equals(normalizedPath, unescapedPath, StringComparison.Ordinal))
        {
            if (!unescapedPath.StartsWith("/", StringComparison.Ordinal))
            {
                yield return $"/{unescapedPath}";
            }
            else
            {
                yield return unescapedPath.TrimStart('/');
            }
        }
    }
}
