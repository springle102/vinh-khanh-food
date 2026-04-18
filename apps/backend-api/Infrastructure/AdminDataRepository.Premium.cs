using VinhKhanh.BackendApi.Models;

namespace VinhKhanh.BackendApi.Infrastructure;

public sealed partial class AdminDataRepository
{
    private SystemSetting NormalizeSystemSetting(SystemSetting setting, bool logWarnings)
    {
        setting.DefaultLanguage = PremiumAccessCatalog.NormalizeLanguageCode(setting.DefaultLanguage);
        setting.FallbackLanguage = PremiumAccessCatalog.NormalizeLanguageCode(setting.FallbackLanguage);
        var originalTtsProvider = setting.TtsProvider;
        setting.TtsProvider = NormalizeTtsProvider(setting.TtsProvider);

        if (logWarnings &&
            !string.Equals(originalTtsProvider, setting.TtsProvider, StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogWarning(
                "TTS provider setting '{OriginalTtsProvider}' is no longer supported. Falling back to {NormalizedTtsProvider}.",
                originalTtsProvider,
                setting.TtsProvider);
        }

        if (setting.SupportedLanguages.Count == 0)
        {
            setting.SupportedLanguages = [setting.DefaultLanguage, setting.FallbackLanguage];
        }

        setting.SupportedLanguages = setting.SupportedLanguages
            .Where(code => !string.IsNullOrWhiteSpace(code))
            .Select(PremiumAccessCatalog.NormalizeLanguageCode)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (setting.SupportedLanguages.Count == 0)
        {
            if (logWarnings)
            {
                _logger.LogWarning(
                    "Supported languages were missing in system settings. Falling back to default + fallback language.");
            }

            setting.SupportedLanguages = ["vi", "en"];
        }

        return setting;
    }

    private static string NormalizeTtsProvider(string? value)
        => string.Equals(value?.Trim(), "elevenlabs", StringComparison.OrdinalIgnoreCase)
            ? "elevenlabs"
            : "elevenlabs";
}
