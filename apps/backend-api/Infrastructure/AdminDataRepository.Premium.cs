using Microsoft.Data.SqlClient;
using VinhKhanh.BackendApi.Models;

namespace VinhKhanh.BackendApi.Infrastructure;

public sealed partial class AdminDataRepository
{
    public sealed record CustomerLanguageAccessDecision(
        string LanguageCode,
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

        if (setting.PremiumUnlockPriceUsd <= 0)
        {
            if (logWarnings)
            {
                _logger.LogWarning(
                    "Premium price was missing or invalid in system settings. Falling back to {FallbackPriceUsd} USD.",
                    PremiumAccessCatalog.DefaultPremiumPriceUsd);
            }

            setting.PremiumUnlockPriceUsd = PremiumAccessCatalog.DefaultPremiumPriceUsd;
        }

        if (!HasExactLanguagePackage(setting.FreeLanguages, PremiumAccessCatalog.FreeLanguages))
        {
            if (logWarnings)
            {
                _logger.LogWarning("Free language package was missing or invalid. Falling back to the default package.");
            }

            setting.FreeLanguages = [.. PremiumAccessCatalog.FreeLanguages];
        }
        else
        {
            setting.FreeLanguages = [.. PremiumAccessCatalog.FreeLanguages];
        }

        if (!HasExactLanguagePackage(setting.PremiumLanguages, PremiumAccessCatalog.PremiumLanguages))
        {
            if (logWarnings)
            {
                _logger.LogWarning("Premium language package was missing or invalid. Falling back to the default package.");
            }

            setting.PremiumLanguages = [.. PremiumAccessCatalog.PremiumLanguages];
        }
        else
        {
            setting.PremiumLanguages = [.. PremiumAccessCatalog.PremiumLanguages];
        }

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
        var premiumPriceUsd = settings.PremiumUnlockPriceUsd > 0
            ? settings.PremiumUnlockPriceUsd
            : PremiumAccessCatalog.DefaultPremiumPriceUsd;

        if (!PremiumAccessCatalog.IsSupportedLanguage(normalizedLanguageCode))
        {
            return new CustomerLanguageAccessDecision(
                normalizedLanguageCode,
                false,
                false,
                premiumPriceUsd,
                "The selected language is not supported yet.",
                null);
        }

        var customer = string.IsNullOrWhiteSpace(customerUserId)
            ? null
            : GetCustomerUserById(connection, transaction, customerUserId);
        var isAllowed = PremiumAccessCatalog.CanUseLanguage(customer?.IsPremium == true, normalizedLanguageCode);
        var requiresPremium = PremiumAccessCatalog.RequiresPremium(normalizedLanguageCode);
        var message = isAllowed
            ? string.Empty
            : $"This language requires Premium. Upgrade to Premium ({premiumPriceUsd} USD) to continue.";

        return new CustomerLanguageAccessDecision(
            normalizedLanguageCode,
            isAllowed,
            requiresPremium,
            premiumPriceUsd,
            message,
            customer);
    }

    private IReadOnlySet<string> GetAllowedLanguageCodesForCustomer(
        SqlConnection connection,
        SqlTransaction? transaction,
        string? customerUserId,
        SystemSetting? settingsOverride = null)
    {
        var decisionSeed = EvaluateCustomerLanguageAccess(
            connection,
            transaction,
            customerUserId,
            PremiumAccessCatalog.FreeLanguages[0],
            settingsOverride);

        return PremiumAccessCatalog.GetAllowedLanguages(decisionSeed.Customer?.IsPremium == true)
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
