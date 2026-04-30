using VinhKhanh.BackendApi.Models;

namespace VinhKhanh.BackendApi.Infrastructure;

public sealed partial class AdminDataRepository
{
    private SystemSetting NormalizeSystemSetting(SystemSetting setting, bool logWarnings)
    {
        setting.AppName = NormalizeSettingString(setting.AppName, "Vinh Khanh Food Guide");
        setting.SupportEmail = NormalizeSettingString(setting.SupportEmail, "support@vinhkhanhfood.local");
        setting.SupportPhone = NormalizeSettingString(setting.SupportPhone, "0900000000");
        setting.ContactAddress = NormalizeSettingString(setting.ContactAddress, "Vinh Khanh Food Street, Ho Chi Minh City");
        setting.SupportInstructions = NormalizeSettingString(
            setting.SupportInstructions,
            "Vui long lien he bo phan ho tro neu ban can khieu nai hoac can tro giup.");
        setting.SupportHours = setting.SupportHours?.Trim() ?? string.Empty;
        if (setting.ContactUpdatedAtUtc == default)
        {
            setting.ContactUpdatedAtUtc = DateTimeOffset.UtcNow;
        }

        setting.DefaultLanguage = PremiumAccessCatalog.NormalizeLanguageCode(setting.DefaultLanguage);
        setting.FallbackLanguage = PremiumAccessCatalog.NormalizeLanguageCode(setting.FallbackLanguage);

        if (setting.SupportedLanguages.Count == 0)
        {
            setting.SupportedLanguages = ["vi", "en", "zh-CN", "ko", "ja"];
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

        if (!setting.SupportedLanguages.Contains(setting.DefaultLanguage, StringComparer.OrdinalIgnoreCase))
        {
            setting.DefaultLanguage = setting.SupportedLanguages.Contains("vi", StringComparer.OrdinalIgnoreCase)
                ? "vi"
                : setting.SupportedLanguages[0];
        }

        if (!setting.SupportedLanguages.Contains(setting.FallbackLanguage, StringComparer.OrdinalIgnoreCase))
        {
            setting.FallbackLanguage = setting.DefaultLanguage;
        }

        setting.GeofenceRadiusMeters = setting.GeofenceRadiusMeters > 0 ? setting.GeofenceRadiusMeters : 30;
        setting.AnalyticsRetentionDays = setting.AnalyticsRetentionDays > 0 ? setting.AnalyticsRetentionDays : 180;
        setting.OfflinePackageMaxSizeMb = Math.Max(0, setting.OfflinePackageMaxSizeMb);
        setting.OfflinePackageDescription = setting.OfflinePackageDescription?.Trim() ?? string.Empty;

        return setting;
    }

    private static string NormalizeSettingString(string? value, string fallback)
        => string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
}
