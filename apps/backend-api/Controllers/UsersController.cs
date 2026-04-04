using Microsoft.AspNetCore.Mvc;
using VinhKhanh.BackendApi.Contracts;
using VinhKhanh.BackendApi.Infrastructure;
using VinhKhanh.BackendApi.Models;

namespace VinhKhanh.BackendApi.Controllers;

[ApiController]
[Route("api/v1/users")]
public sealed class UsersController(AdminDataRepository repository) : ControllerBase
{
    [HttpGet]
    public ActionResult<ApiResponse<IReadOnlyList<EndUser>>> GetUsers(
        [FromQuery] string? userId,
        [FromQuery] string? role)
        => Ok(ApiResponse<IReadOnlyList<EndUser>>.Ok(repository.GetEndUsers(userId, role)));

    [HttpGet("{id}")]
    public ActionResult<ApiResponse<EndUser>> GetUserById(
        string id,
        [FromQuery] string? userId,
        [FromQuery] string? role)
    {
        var user = repository.GetEndUserById(id, userId, role);
        return user is null
            ? NotFound(ApiResponse<EndUser>.Fail("Không tìm thấy người dùng cuối."))
            : Ok(ApiResponse<EndUser>.Ok(user));
    }

    [HttpGet("{id}/history")]
    public ActionResult<ApiResponse<IReadOnlyList<EndUserPoiVisit>>> GetUserHistory(
        string id,
        [FromQuery] string? userId,
        [FromQuery] string? role)
    {
        var user = repository.GetEndUserById(id, userId, role);
        if (user is null)
        {
            return NotFound(ApiResponse<IReadOnlyList<EndUserPoiVisit>>.Fail("Không tìm thấy người dùng cuối."));
        }

        return Ok(ApiResponse<IReadOnlyList<EndUserPoiVisit>>.Ok(repository.GetEndUserHistory(id, userId, role)));
    }

    [HttpPatch("{id}/status")]
    public ActionResult<ApiResponse<EndUser>> UpdateUserStatus(string id, [FromBody] EndUserStatusUpdateRequest request)
    {
        var updated = repository.UpdateEndUserStatus(id, request);
        return updated is null
            ? NotFound(ApiResponse<EndUser>.Fail("Không tìm thấy người dùng cuối."))
            : Ok(ApiResponse<EndUser>.Ok(updated, "Cập nhật trạng thái người dùng thành công."));
    }
}
