using Microsoft.AspNetCore.Mvc;
using VinhKhanh.BackendApi.Contracts;
using VinhKhanh.BackendApi.Infrastructure;
using VinhKhanh.BackendApi.Models;

namespace VinhKhanh.BackendApi.Controllers;

[ApiController]
[Route("api/v1/settings")]
public sealed class SettingsController(AdminDataRepository repository) : ControllerBase
{
    [HttpGet]
    public ActionResult<ApiResponse<SystemSetting>> GetSettings()
        => Ok(ApiResponse<SystemSetting>.Ok(repository.GetSettings()));

    [HttpPut]
    public ActionResult<ApiResponse<SystemSetting>> UpdateSettings([FromBody] SystemSettingUpsertRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.AppName) || string.IsNullOrWhiteSpace(request.SupportEmail))
        {
            return BadRequest(ApiResponse<SystemSetting>.Fail("AppName va supportEmail la bat buoc."));
        }

        if (request.PremiumUnlockPriceUsd <= 0)
        {
            return BadRequest(ApiResponse<SystemSetting>.Fail("Gia goi Premium phai lon hon 0 USD."));
        }

        var saved = repository.SaveSettings(request);
        return Ok(ApiResponse<SystemSetting>.Ok(saved, "Cap nhat cai dat he thong thanh cong."));
    }
}
