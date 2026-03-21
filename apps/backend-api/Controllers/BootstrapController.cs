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
    public ActionResult<ApiResponse<AdminBootstrapResponse>> GetBootstrap()
        => Ok(ApiResponse<AdminBootstrapResponse>.Ok(repository.GetBootstrap()));

    [HttpGet("dashboard/summary")]
    public ActionResult<ApiResponse<DashboardSummaryResponse>> GetDashboardSummary()
        => Ok(ApiResponse<DashboardSummaryResponse>.Ok(repository.GetDashboardSummary()));

    [HttpGet("categories")]
    public ActionResult<ApiResponse<IReadOnlyList<PlaceCategory>>> GetCategories()
        => Ok(ApiResponse<IReadOnlyList<PlaceCategory>>.Ok(repository.GetCategories()));

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
