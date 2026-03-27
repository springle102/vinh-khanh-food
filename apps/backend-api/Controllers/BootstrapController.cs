using Microsoft.AspNetCore.Mvc;
using VinhKhanh.BackendApi.Contracts;
using VinhKhanh.BackendApi.Infrastructure;
using VinhKhanh.BackendApi.Models;

namespace VinhKhanh.BackendApi.Controllers;

[ApiController]
[Route("api/v1")]
public sealed class BootstrapController(AdminDataRepository repository) : ControllerBase
{
    [HttpGet("bootstrap")]
    public ActionResult<ApiResponse<AdminBootstrapResponse>> GetBootstrap(
        [FromQuery] string? userId,
        [FromQuery] string? role)
        => Ok(ApiResponse<AdminBootstrapResponse>.Ok(repository.GetBootstrap(userId, role)));

    [HttpGet("dashboard/summary")]
    public ActionResult<ApiResponse<DashboardSummaryResponse>> GetDashboardSummary()
        => Ok(ApiResponse<DashboardSummaryResponse>.Ok(repository.GetDashboardSummary()));

    [HttpGet("categories")]
    public ActionResult<ApiResponse<IReadOnlyList<PoiCategory>>> GetCategories()
        => Ok(ApiResponse<IReadOnlyList<PoiCategory>>.Ok(repository.GetCategories()));

    [HttpGet("customer-users")]
    public ActionResult<ApiResponse<IReadOnlyList<CustomerUser>>> GetCustomerUsers()
        => Ok(ApiResponse<IReadOnlyList<CustomerUser>>.Ok(repository.GetCustomerUsers()));

    [HttpGet("analytics/view-logs")]
    public ActionResult<ApiResponse<IReadOnlyList<ViewLog>>> GetViewLogs()
        => Ok(ApiResponse<IReadOnlyList<ViewLog>>.Ok(repository.GetViewLogs()));

    [HttpGet("analytics/audio-listen-logs")]
    public ActionResult<ApiResponse<IReadOnlyList<AudioListenLog>>> GetAudioListenLogs()
        => Ok(ApiResponse<IReadOnlyList<AudioListenLog>>.Ok(repository.GetAudioListenLogs()));
}
