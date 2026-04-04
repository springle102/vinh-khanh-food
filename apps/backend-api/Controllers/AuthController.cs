using Microsoft.AspNetCore.Mvc;
using VinhKhanh.BackendApi.Contracts;
using VinhKhanh.BackendApi.Infrastructure;

namespace VinhKhanh.BackendApi.Controllers;

[ApiController]
[Route("api/v1/auth")]
public sealed class AuthController(AdminDataRepository repository) : ControllerBase
{
    [HttpGet("login-options")]
    public ActionResult<ApiResponse<IReadOnlyList<LoginAccountOptionResponse>>> GetLoginOptions([FromQuery] string? portal)
    {
        var normalizedPortal = string.IsNullOrWhiteSpace(portal) ? null : portal.Trim().ToLowerInvariant();
        var options = repository.GetLoginAccountOptions(normalizedPortal);
        return Ok(ApiResponse<IReadOnlyList<LoginAccountOptionResponse>>.Ok(options));
    }

    [HttpPost("login")]
    public ActionResult<ApiResponse<AuthTokensResponse>> Login([FromBody] LoginRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Email) || string.IsNullOrWhiteSpace(request.Password))
        {
            return BadRequest(ApiResponse<AuthTokensResponse>.Fail("Email và mật khẩu là bắt buộc."));
        }

        var portal = string.IsNullOrWhiteSpace(request.Portal) ? null : request.Portal.Trim().ToLowerInvariant();
        var result = repository.Login(request.Email, request.Password, portal);
        return result is null
            ? Unauthorized(ApiResponse<AuthTokensResponse>.Fail("Thông tin đăng nhập không hợp lệ."))
            : Ok(ApiResponse<AuthTokensResponse>.Ok(result, "Đăng nhập thành công."));
    }

    [HttpPost("refresh")]
    public ActionResult<ApiResponse<AuthTokensResponse>> Refresh([FromBody] RefreshTokenRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.RefreshToken))
        {
            return BadRequest(ApiResponse<AuthTokensResponse>.Fail("Refresh token là bắt buộc."));
        }

        var result = repository.Refresh(request.RefreshToken);
        return result is null
            ? Unauthorized(ApiResponse<AuthTokensResponse>.Fail("Refresh token không hợp lệ hoặc đã hết hạn."))
            : Ok(ApiResponse<AuthTokensResponse>.Ok(result, "Làm mới phiên đăng nhập thành công."));
    }

    [HttpPost("logout")]
    public ActionResult<ApiResponse<string>> Logout([FromBody] LogoutRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.RefreshToken))
        {
            return BadRequest(ApiResponse<string>.Fail("Refresh token là bắt buộc."));
        }

        repository.Logout(request.RefreshToken);
        return Ok(ApiResponse<string>.Ok("logged_out", "Đăng xuất thành công."));
    }
}
