using Microsoft.AspNetCore.Mvc;
using System.Text.Json;
using VinhKhanh.BackendApi.Contracts;
using VinhKhanh.BackendApi.Infrastructure;
using VinhKhanh.BackendApi.Models;

namespace VinhKhanh.BackendApi.Controllers;

[ApiController]
[Route("api/mobile/settings")]
[Route("api/v1/mobile/settings")]
[Route("api/system-settings")]
[Route("api/v1/system-settings")]
[ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
public sealed class MobileSettingsController(
    AdminDataRepository repository,
    ILogger<MobileSettingsController> logger) : ControllerBase
{
    private static readonly JsonSerializerOptions ResponseLogJsonOptions = new(JsonSerializerDefaults.Web);

    [HttpGet]
    public ActionResult<ApiResponse<MobileSystemSettingsResponse>> GetSettings()
    {
        var settings = repository.GetSettings();
        DisableResponseCaching(settings);
        var response = ToMobileSystemSettingsResponse(settings);
        LogSettingsLoaded(settings, response.Languages.EnabledLanguages.Count, response.Contact, response.OfflinePackage);
        logger.LogInformation(
            "[MobileSettings] response JSON={ResponseJson}",
            JsonSerializer.Serialize(response, ResponseLogJsonOptions));
        return Ok(ApiResponse<MobileSystemSettingsResponse>.Ok(response));
    }

    [HttpGet("languages")]
    public ActionResult<ApiResponse<MobileLanguageSettingsResponse>> GetLanguages()
    {
        var settings = repository.GetSettings();
        DisableResponseCaching(settings);
        var response = ToMobileLanguageSettingsResponse(settings);
        logger.LogInformation(
            "[MobileSettings] languages loaded. enabledLanguagesCount={EnabledLanguagesCount}; enabledLanguages={EnabledLanguages}; defaultLanguage={DefaultLanguage}",
            response.EnabledLanguages.Count,
            string.Join(",", response.EnabledLanguages),
            response.DefaultLanguage);
        return Ok(ApiResponse<MobileLanguageSettingsResponse>.Ok(response));
    }

    [HttpGet("contact")]
    public ActionResult<ApiResponse<MobileContactSettingsResponse>> GetContactSettings()
    {
        var settings = repository.GetSettings();
        DisableResponseCaching(settings);
        var response = ToMobileContactSettingsResponse(settings);
        logger.LogInformation(
            "[MobileSettings] contact loaded. contactExists={ContactExists}; updatedAtUtc={UpdatedAtUtc}; hasPhone={HasPhone}; hasEmail={HasEmail}; hasAddress={HasAddress}; hasComplaintGuide={HasComplaintGuide}",
            HasContact(response),
            response.UpdatedAtUtc,
            !string.IsNullOrWhiteSpace(response.Phone),
            !string.IsNullOrWhiteSpace(response.Email),
            !string.IsNullOrWhiteSpace(response.Address),
            !string.IsNullOrWhiteSpace(response.ComplaintGuide));
        logger.LogInformation(
            "[MobileSettings] contact response JSON={ContactJson}",
            JsonSerializer.Serialize(response, ResponseLogJsonOptions));
        return Ok(ApiResponse<MobileContactSettingsResponse>.Ok(response));
    }

    [HttpGet("offline-package")]
    public ActionResult<ApiResponse<MobileOfflinePackageSettingsResponse>> GetOfflinePackageSettings()
    {
        var settings = repository.GetSettings();
        DisableResponseCaching(settings);
        var response = ToMobileOfflinePackageSettingsResponse(settings);
        logger.LogInformation(
            "[MobileSettings] offline package metadata loaded. downloadsEnabled={DownloadsEnabled}; maxPackageSizeMb={MaxPackageSizeMb}; hasDescription={HasDescription}",
            response.DownloadsEnabled,
            response.MaxPackageSizeMb,
            !string.IsNullOrWhiteSpace(response.Description));
        return Ok(ApiResponse<MobileOfflinePackageSettingsResponse>.Ok(response));
    }

    private void DisableResponseCaching(SystemSetting settings)
    {
        Response.Headers.CacheControl = "no-store, no-cache, max-age=0, must-revalidate";
        Response.Headers.Pragma = "no-cache";
        Response.Headers.Expires = "0";
        Response.Headers["X-Settings-Updated-At"] = settings.ContactUpdatedAtUtc.ToString("O");
    }

    private static MobileSystemSettingsResponse ToMobileSystemSettingsResponse(SystemSetting settings)
        => new(
            ToMobileLanguageSettingsResponse(settings),
            ToMobileContactSettingsResponse(settings),
            ToMobileOfflinePackageSettingsResponse(settings));

    private static MobileContactSettingsResponse ToMobileContactSettingsResponse(SystemSetting settings)
        => new(
            NormalizeContactValue(settings.AppName),
            NormalizeContactValue(settings.SupportPhone),
            NormalizeContactValue(settings.SupportEmail),
            NormalizeContactValue(settings.ContactAddress),
            NormalizeContactValue(settings.SupportInstructions),
            NormalizeContactValue(settings.SupportHours),
            settings.ContactUpdatedAtUtc);

    private static string NormalizeContactValue(string? value)
        => value?.Trim() ?? string.Empty;

    private static MobileOfflinePackageSettingsResponse ToMobileOfflinePackageSettingsResponse(SystemSetting settings)
        => new(
            settings.OfflinePackageDownloadsEnabled,
            Math.Max(0, settings.OfflinePackageMaxSizeMb),
            NormalizeContactValue(settings.OfflinePackageDescription));

    private static MobileLanguageSettingsResponse ToMobileLanguageSettingsResponse(SystemSetting settings)
    {
        var enabledLanguages = settings.SupportedLanguages
            .Select(PremiumAccessCatalog.NormalizeLanguageCode)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var defaultLanguage = PremiumAccessCatalog.NormalizeLanguageCode(settings.DefaultLanguage);
        if (enabledLanguages.Count == 0)
        {
            enabledLanguages.Add(LanguageRegistry.DefaultLanguageCode);
            defaultLanguage = LanguageRegistry.DefaultLanguageCode;
        }

        if (!enabledLanguages.Contains(defaultLanguage))
        {
            defaultLanguage = enabledLanguages.Contains(LanguageRegistry.DefaultLanguageCode)
                ? LanguageRegistry.DefaultLanguageCode
                : enabledLanguages.First();
        }

        var languages = LanguageRegistry.SupportedLanguages
            .Where(language => enabledLanguages.Contains(language.InternalCode))
            .Select(language => new MobileLanguageOptionResponse(language.InternalCode, language.DisplayName))
            .ToList();
        if (languages.Count == 0)
        {
            var fallback = LanguageRegistry.GetDefinition(LanguageRegistry.DefaultLanguageCode);
            languages.Add(new MobileLanguageOptionResponse(fallback.InternalCode, fallback.DisplayName));
            defaultLanguage = fallback.InternalCode;
        }

        return new MobileLanguageSettingsResponse(defaultLanguage, languages)
        {
            EnabledLanguages = languages.Select(language => language.Code).ToList()
        };
    }

    private void LogSettingsLoaded(
        SystemSetting settings,
        int enabledLanguagesCount,
        MobileContactSettingsResponse contact,
        MobileOfflinePackageSettingsResponse offlinePackage)
    {
        logger.LogInformation(
            "[MobileSettings] settings loaded. enabledLanguagesCount={EnabledLanguagesCount}; contactExists={ContactExists}; offlineDownloadsEnabled={OfflineDownloadsEnabled}; contactUpdatedAtUtc={ContactUpdatedAtUtc}",
            enabledLanguagesCount,
            HasContact(contact),
            offlinePackage.DownloadsEnabled,
            settings.ContactUpdatedAtUtc);
    }

    private static bool HasContact(MobileContactSettingsResponse contact)
        => !string.IsNullOrWhiteSpace(contact.SystemName) ||
           !string.IsNullOrWhiteSpace(contact.Phone) ||
           !string.IsNullOrWhiteSpace(contact.Email) ||
           !string.IsNullOrWhiteSpace(contact.Address) ||
           !string.IsNullOrWhiteSpace(contact.ComplaintGuide) ||
           !string.IsNullOrWhiteSpace(contact.SupportHours);
}
