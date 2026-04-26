using Microsoft.AspNetCore.Mvc;
using VinhKhanh.BackendApi.Contracts;
using VinhKhanh.BackendApi.Infrastructure;

namespace VinhKhanh.BackendApi.Controllers;

[ApiController]
[Route("api/admin/dashboard")]
public sealed class AdminDashboardController(
    AdminDataRepository repository,
    AdminRequestContextResolver adminRequestContextResolver) : ControllerBase
{
    [HttpGet("online-users")]
    [HttpGet("/api/v1/dashboard/online-users")]
    public ActionResult<ApiResponse<OnlineUsersResponse>> GetOnlineUsers()
    {
        _ = adminRequestContextResolver.RequireAuthenticatedAdmin();
        Response.Headers.CacheControl = "no-store, no-cache, must-revalidate, proxy-revalidate";
        Response.Headers.Pragma = "no-cache";
        Response.Headers.Expires = "0";

        return Ok(ApiResponse<OnlineUsersResponse>.Ok(repository.GetOnlineUsers()));
    }
}
