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
    public ActionResult<ApiResponse<IReadOnlyList<EndUser>>> GetUsers()
        => Ok(ApiResponse<IReadOnlyList<EndUser>>.Ok(repository.GetEndUsers()));

    [HttpGet("{id}")]
    public ActionResult<ApiResponse<EndUser>> GetUserById(string id)
    {
        var user = repository.GetEndUserById(id);
        return user is null
            ? NotFound(ApiResponse<EndUser>.Fail("Khong tim thay nguoi dung cuoi."))
            : Ok(ApiResponse<EndUser>.Ok(user));
    }

    [HttpGet("{id}/history")]
    public ActionResult<ApiResponse<IReadOnlyList<EndUserPoiVisit>>> GetUserHistory(string id)
    {
        var user = repository.GetEndUserById(id);
        if (user is null)
        {
            return NotFound(ApiResponse<IReadOnlyList<EndUserPoiVisit>>.Fail("Khong tim thay nguoi dung cuoi."));
        }

        return Ok(ApiResponse<IReadOnlyList<EndUserPoiVisit>>.Ok(repository.GetEndUserHistory(id)));
    }

    [HttpPatch("{id}/status")]
    public ActionResult<ApiResponse<EndUser>> UpdateUserStatus(string id, [FromBody] EndUserStatusUpdateRequest request)
    {
        var updated = repository.UpdateEndUserStatus(id, request);
        return updated is null
            ? NotFound(ApiResponse<EndUser>.Fail("Khong tim thay nguoi dung cuoi."))
            : Ok(ApiResponse<EndUser>.Ok(updated, "Cap nhat trang thai nguoi dung thanh cong."));
    }
}
