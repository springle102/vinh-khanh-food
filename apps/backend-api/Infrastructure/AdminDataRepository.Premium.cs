using Microsoft.Data.SqlClient;
using VinhKhanh.BackendApi.Models;

namespace VinhKhanh.BackendApi.Infrastructure;

public sealed partial class AdminDataRepository
{
    public sealed record CustomerLanguageAccessDecision(
        string LanguageCode,
        string AppAccessType,
        bool IsAllowed,
        bool RequiresPremium,
        int PremiumPriceUsd,
        string Message,
        CustomerUser? Customer);

    public CustomerLanguageAccessDecision EvaluateCustomerLanguageAccess(string? customerUserId, string? languageCode)
    {
        using var connection = OpenConnection();
        return EvaluateCustomerLanguageAccess(connection, null, customerUserId, languageCode, null);
    }

    public CustomerUser? UpdateCustomerPremiumStatus(
        string id,
        bool isPremium,
        string actorName,
        string actorRole)
    {
        using var connection = OpenConnection();
        using var transaction = connection.BeginTransaction();

        var existing = GetCustomerUserById(connection, transaction, id);
        if (existing is null)
        {
            transaction.Rollback();
            return null;
        }

        ExecuteNonQuery(
            connection,
            transaction,
            """
            UPDATE dbo.CustomerUsers
            SET IsPremium = ?
            WHERE Id = ?;
            """,
            isPremium,
            id);

        AppendAuditLog(
            connection,
            transaction,
            actorName,
            actorRole,
            isPremium ? "Kich hoat goi Premium" : "Huy goi Premium",
            id);

        var saved = GetCustomerUserById(connection, transaction, id);
        transaction.Commit();
        return saved;
    }

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

        setting.FreeLanguages = [.. setting.SupportedLanguages];
        setting.PremiumLanguages = [];
        setting.PremiumUnlockPriceUsd = 0;
        return setting;
    }

    private CustomerLanguageAccessDecision EvaluateCustomerLanguageAccess(
        SqlConnection connection,
        SqlTransaction? transaction,
        string? customerUserId,
        string? languageCode,
        SystemSetting? settingsOverride)
    {
        var settings = settingsOverride ?? GetSettings(connection, transaction);
        var normalizedLanguageCode = PremiumAccessCatalog.NormalizeLanguageCode(languageCode);
        var supportedLanguages = GetSupportedLanguageCodeSet(settings);

        if (!supportedLanguages.Contains(normalizedLanguageCode))
        {
            return new CustomerLanguageAccessDecision(
                normalizedLanguageCode,
                "public-app",
                false,
                false,
                0,
                "The selected language is not supported yet.",
                null);
        }

        return new CustomerLanguageAccessDecision(
            normalizedLanguageCode,
            "public-app",
            true,
            false,
            0,
            string.Empty,
            null);
    }

    private IReadOnlySet<string> GetAllowedLanguageCodesForCustomer(
        SqlConnection connection,
        SqlTransaction? transaction,
        string? customerUserId,
        SystemSetting? settingsOverride = null)
    {
        return GetSupportedLanguageCodeSet(settingsOverride ?? GetSettings(connection, transaction))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    private static bool HasExactLanguagePackage(IEnumerable<string>? actualLanguages, IEnumerable<string> expectedLanguages)
    {
        var actual = actualLanguages?
            .Where((languageCode) => !string.IsNullOrWhiteSpace(languageCode))
            .Select(PremiumAccessCatalog.NormalizeLanguageCode)
            .ToHashSet(StringComparer.OrdinalIgnoreCase)
            ?? [];
        var expected = expectedLanguages
            .Select(PremiumAccessCatalog.NormalizeLanguageCode)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        return actual.SetEquals(expected);
    }

    private static string NormalizeTtsProvider(string? value)
        => string.Equals(value?.Trim(), "elevenlabs", StringComparison.OrdinalIgnoreCase)
            ? "elevenlabs"
            : "elevenlabs";
}
