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

    [HttpPut]
    public ActionResult<ApiResponse<SystemSetting>> UpdateSettings([FromBody] SystemSettingUpsertRequest request)
    {
        var actor = adminRequestContextResolver.RequireSuperAdmin();
        if (string.IsNullOrWhiteSpace(request.AppName) || string.IsNullOrWhiteSpace(request.SupportEmail))
        {
            return BadRequest(ApiResponse<SystemSetting>.Fail("AppName va supportEmail la bat buoc."));
        }

        if (request.SupportedLanguages is null || request.SupportedLanguages.Count == 0)
        {
            return BadRequest(ApiResponse<SystemSetting>.Fail("Can cau hinh it nhat mot ngon ngu ho tro."));
        }

        var saved = repository.SaveSettings(request, actor);
        return Ok(ApiResponse<SystemSetting>.Ok(saved, "Cap nhat cai dat he thong thanh cong."));
    }
}
