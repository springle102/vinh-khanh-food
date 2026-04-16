namespace VinhKhanh.BackendApi.Infrastructure;

internal sealed record DotEnvBootstrapResult(
    IReadOnlyList<string> ExistingFiles,
    IReadOnlyList<string> ImportedKeys);

internal static class DotEnvBootstrapper
{
    public static DotEnvBootstrapResult LoadIntoEnvironment(params string[] candidatePaths)
    {
        var existingFiles = new List<string>();
        var importedKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var path in candidatePaths.Where(value => !string.IsNullOrWhiteSpace(value)).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (!File.Exists(path))
            {
                continue;
            }

            existingFiles.Add(path);

            foreach (var rawLine in File.ReadLines(path))
            {
                var line = rawLine.Trim();
                if (string.IsNullOrWhiteSpace(line) || line.StartsWith('#'))
                {
                    continue;
                }

                var separatorIndex = line.IndexOf('=');
                if (separatorIndex <= 0)
                {
                    continue;
                }

                var key = line[..separatorIndex].Trim();
                if (string.IsNullOrWhiteSpace(key) || !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(key)))
                {
                    continue;
                }

                var value = line[(separatorIndex + 1)..].Trim();
                if (value.Length >= 2 &&
                    ((value.StartsWith('"') && value.EndsWith('"')) || (value.StartsWith('\'') && value.EndsWith('\''))))
                {
                    value = value[1..^1];
                }

                Environment.SetEnvironmentVariable(key, value);
                importedKeys.Add(key);
            }
        }

        return new DotEnvBootstrapResult(existingFiles, importedKeys.ToList());
    }
}
