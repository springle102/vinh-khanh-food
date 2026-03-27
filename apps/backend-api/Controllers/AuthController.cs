using Microsoft.AspNetCore.Mvc;
using VinhKhanh.BackendApi.Contracts;
using VinhKhanh.BackendApi.Infrastructure;

namespace VinhKhanh.BackendApi.Controllers;

[ApiController]
[Route("api/v1/auth")]
public sealed class AuthController(AdminDataRepository repository) : ControllerBase
{
    [HttpPost("login")]
    public ActionResult<ApiResponse<AuthTokensResponse>> Login([FromBody] LoginRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Email) || string.IsNullOrWhiteSpace(request.Password))
        {
            return BadRequest(ApiResponse<AuthTokensResponse>.Fail("Email va mat khau la bat buoc."));
        }

        var portal = string.IsNullOrWhiteSpace(request.Portal) ? null : request.Portal.Trim().ToLowerInvariant();
        var result = repository.Login(request.Email, request.Password, portal);
        return result is null
            ? Unauthorized(ApiResponse<AuthTokensResponse>.Fail("Thong tin dang nhap khong hop le."))
            : Ok(ApiResponse<AuthTokensResponse>.Ok(result, "Dang nhap thanh cong."));
    }

    [HttpPost("refresh")]
    public ActionResult<ApiResponse<AuthTokensResponse>> Refresh([FromBody] RefreshTokenRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.RefreshToken))
        {
            return BadRequest(ApiResponse<AuthTokensResponse>.Fail("Refresh token la bat buoc."));
        }

        var result = repository.Refresh(request.RefreshToken);
        return result is null
            ? Unauthorized(ApiResponse<AuthTokensResponse>.Fail("Refresh token khong hop le hoac da het han."))
            : Ok(ApiResponse<AuthTokensResponse>.Ok(result, "Lam moi phien dang nhap thanh cong."));
    }

    [HttpPost("logout")]
    public ActionResult<ApiResponse<string>> Logout([FromBody] LogoutRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.RefreshToken))
        {
            return BadRequest(ApiResponse<string>.Fail("Refresh token la bat buoc."));
        }

        repository.Logout(request.RefreshToken);
        return Ok(ApiResponse<string>.Ok("logged_out", "Dang xuat thanh cong."));
    }
}
