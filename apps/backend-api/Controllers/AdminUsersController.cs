using Microsoft.AspNetCore.Mvc;
using VinhKhanh.BackendApi.Contracts;
using VinhKhanh.BackendApi.Infrastructure;
using VinhKhanh.BackendApi.Models;

namespace VinhKhanh.BackendApi.Controllers;

[ApiController]
[Route("api/v1/admin-users")]
public sealed class AdminUsersController(
    AdminDataRepository repository,
    AdminRequestContextResolver adminRequestContextResolver) : ControllerBase
{
    [HttpGet]
    public ActionResult<ApiResponse<IReadOnlyList<AdminUser>>> GetUsers()
        => Ok(ApiResponse<IReadOnlyList<AdminUser>>.Ok(
            repository.GetUsers(adminRequestContextResolver.RequireAuthenticatedAdmin())));

    [HttpGet("{id}")]
    public ActionResult<ApiResponse<AdminUser>> GetUserById(string id)
    {
        var actor = adminRequestContextResolver.RequireAuthenticatedAdmin();
        var user = repository.GetUsers(actor).FirstOrDefault(item => item.Id == id);
        return user is null
            ? NotFound(ApiResponse<AdminUser>.Fail("Khong tim thay tai khoan admin."))
            : Ok(ApiResponse<AdminUser>.Ok(user));
    }

    [HttpPost]
    public ActionResult<ApiResponse<AdminUser>> CreateUser([FromBody] AdminUserUpsertRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Name) || string.IsNullOrWhiteSpace(request.Email))
        {
            return BadRequest(ApiResponse<AdminUser>.Fail("Ten va email la bat buoc."));
        }

        var actor = adminRequestContextResolver.RequireAuthenticatedAdmin();
        var sanitizedRequest = request with
        {
            ActorName = actor.Name,
            ActorRole = actor.Role
        };

        var saved = repository.SaveUser(null, sanitizedRequest, actor);
        return CreatedAtAction(nameof(GetUserById), new { id = saved.Id }, ApiResponse<AdminUser>.Ok(saved, "Tao tai khoan admin thanh cong."));
    }

    [HttpPut("{id}")]
    public ActionResult<ApiResponse<AdminUser>> UpdateUser(string id, [FromBody] AdminUserUpsertRequest request)
    {
        var actor = adminRequestContextResolver.RequireAuthenticatedAdmin();
        var existing = repository.GetUsers(actor).Any(item => item.Id == id);
        if (!existing)
        {
            return NotFound(ApiResponse<AdminUser>.Fail("Khong tim thay tai khoan admin."));
        }

        var sanitizedRequest = request with
        {
            ActorName = actor.Name,
            ActorRole = actor.Role
        };

        var saved = repository.SaveUser(id, sanitizedRequest, actor);
        return Ok(ApiResponse<AdminUser>.Ok(saved, "Cap nhat tai khoan admin thanh cong."));
    }

    [HttpPatch("{id}/status")]
    public ActionResult<ApiResponse<AdminUser>> UpdateUserStatus(string id, [FromBody] AdminUserStatusUpdateRequest request)
    {
        var actor = adminRequestContextResolver.RequireAuthenticatedAdmin();
        var saved = repository.SaveUserStatus(id, request, actor);
        return Ok(ApiResponse<AdminUser>.Ok(saved, "Cap nhat trang thai tai khoan thanh cong."));
    }
}
