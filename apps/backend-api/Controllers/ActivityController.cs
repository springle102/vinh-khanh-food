using Microsoft.AspNetCore.Mvc;
using VinhKhanh.BackendApi.Contracts;
using VinhKhanh.BackendApi.Infrastructure;
using VinhKhanh.BackendApi.Models;

namespace VinhKhanh.BackendApi.Controllers;

[ApiController]
[Route("api/v1/activity")]
public sealed class ActivityController(
    AdminDataRepository repository,
    AdminRequestContextResolver adminRequestContextResolver) : ControllerBase
{
    [HttpGet("audit-logs")]
    public ActionResult<ApiResponse<IReadOnlyList<AuditLog>>> GetAuditLogs()
        => Ok(ApiResponse<IReadOnlyList<AuditLog>>.Ok(
            repository.GetAuditLogs(adminRequestContextResolver.RequireAuthenticatedAdmin())));
}
