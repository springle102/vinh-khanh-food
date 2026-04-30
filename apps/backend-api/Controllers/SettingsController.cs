using Microsoft.AspNetCore.Mvc;
using VinhKhanh.BackendApi.Contracts;
using VinhKhanh.BackendApi.Infrastructure;
using VinhKhanh.BackendApi.Models;

namespace VinhKhanh.BackendApi.Controllers;

[ApiController]
[Route("api/v1/settings")]
public sealed class SettingsController(
    AdminDataRepository repository,
    AdminRequestContextResolver adminRequestContextResolver) : ControllerBase
{
    [HttpGet]
    public ActionResult<ApiResponse<SystemSetting>> GetSettings()
    {
        adminRequestContextResolver.RequireSuperAdmin();
        return Ok(ApiResponse<SystemSetting>.Ok(repository.GetSettings()));
    }

    [HttpPut("languages")]
    [HttpPatch("languages")]
    public ActionResult<ApiResponse<AppLanguageSettingsResponse>> UpdateLanguageSettings(
        [FromBody] AppLanguageSettingsUpdateRequest request)
    {
        var actor = adminRequestContextResolver.RequireSuperAdmin();
        var validationError = ValidateLanguageSettings(request.DefaultLanguage, request.EnabledLanguages);
        if (validationError is not null)
        {
            return BadRequest(ApiResponse<AppLanguageSettingsResponse>.Fail(validationError));
        }

        var saved = repository.SaveLanguageSettings(request, actor);
        return Ok(ApiResponse<AppLanguageSettingsResponse>.Ok(
            ToLanguageSettingsResponse(saved),
            "Cap nhat ngon ngu ung dung thanh cong."));
    }

    [HttpPut("contact")]
    [HttpPatch("contact")]
    public ActionResult<ApiResponse<SystemContactSettingsResponse>> UpdateContactSettings(
        [FromBody] SystemContactSettingsUpdateRequest request)
    {
        var actor = adminRequestContextResolver.RequireSuperAdmin();
        var validationError = ValidateContactSettings(
            request.AppName,
            request.SupportPhone,
            request.SupportEmail,
            request.SupportInstructions);
        if (validationError is not null)
        {
            return BadRequest(ApiResponse<SystemContactSettingsResponse>.Fail(validationError));
        }

        var saved = repository.SaveContactSettings(request, actor);
        return Ok(ApiResponse<SystemContactSettingsResponse>.Ok(
            ToContactSettingsResponse(saved),
            "Cap nhat thong tin lien he thanh cong."));
    }

    public static AppLanguageSettingsResponse ToLanguageSettingsResponse(SystemSetting settings)
    {
        var enabledLanguages = settings.SupportedLanguages
            .Select(PremiumAccessCatalog.NormalizeLanguageCode)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var defaultLanguage = PremiumAccessCatalog.NormalizeLanguageCode(settings.DefaultLanguage);
        var languages = LanguageRegistry.SupportedLanguages
            .Select(language => new AppLanguageSettingResponse(
                language.InternalCode,
                language.DisplayName,
                enabledLanguages.Contains(language.InternalCode),
                string.Equals(language.InternalCode, defaultLanguage, StringComparison.OrdinalIgnoreCase)))
            .ToList();

        return new AppLanguageSettingsResponse(defaultLanguage, languages);
    }

    public static SystemContactSettingsResponse ToContactSettingsResponse(SystemSetting settings)
        => new(
            settings.AppName,
            settings.SupportPhone,
            settings.SupportEmail,
            settings.ContactAddress,
            settings.SupportInstructions,
            settings.SupportHours,
            settings.ContactUpdatedAtUtc);

    private static string? ValidateLanguageSettings(string defaultLanguage, IReadOnlyCollection<string>? enabledLanguages)
    {
        if (enabledLanguages is null || enabledLanguages.Count == 0)
        {
            return "Can cau hinh it nhat mot ngon ngu dang bat.";
        }

        var normalizedEnabledLanguages = enabledLanguages
            .Where(code => !string.IsNullOrWhiteSpace(code))
            .Select(PremiumAccessCatalog.NormalizeLanguageCode)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (normalizedEnabledLanguages.Count == 0)
        {
            return "Can cau hinh it nhat mot ngon ngu dang bat.";
        }

        if (normalizedEnabledLanguages.Any(code => !LanguageRegistry.IsSupported(code)))
        {
            return "Danh sach ngon ngu co ma khong duoc he thong ho tro.";
        }

        var normalizedDefaultLanguage = PremiumAccessCatalog.NormalizeLanguageCode(defaultLanguage);
        if (!LanguageRegistry.IsSupported(normalizedDefaultLanguage))
        {
            return "Ngon ngu mac dinh khong duoc he thong ho tro.";
        }

        if (!normalizedEnabledLanguages.Contains(normalizedDefaultLanguage, StringComparer.OrdinalIgnoreCase))
        {
            return "Ngon ngu mac dinh phai nam trong danh sach ngon ngu dang bat.";
        }

        return null;
    }

    private static string? ValidateContactSettings(
        string appName,
        string supportPhone,
        string supportEmail,
        string supportInstructions)
    {
        if (string.IsNullOrWhiteSpace(appName))
        {
            return "Ten don vi/he thong la bat buoc.";
        }

        if (string.IsNullOrWhiteSpace(supportPhone) || supportPhone.Count(char.IsDigit) < 8)
        {
            return "So dien thoai ho tro phai co it nhat 8 chu so.";
        }

        if (!string.IsNullOrWhiteSpace(supportEmail))
        {
            try
            {
                _ = new System.Net.Mail.MailAddress(supportEmail.Trim());
            }
            catch (FormatException)
            {
                return "Email ho tro khong dung dinh dang.";
            }
        }

        if (string.IsNullOrWhiteSpace(supportInstructions))
        {
            return "Noi dung huong dan khieu nai/ho tro la bat buoc.";
        }

        return null;
    }
}
