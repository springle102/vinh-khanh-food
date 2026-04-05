using Microsoft.AspNetCore.Mvc;
using VinhKhanh.BackendApi.Contracts;
using VinhKhanh.BackendApi.Infrastructure;
using VinhKhanh.BackendApi.Models;

namespace VinhKhanh.BackendApi.Controllers;

[ApiController]
[Route("api/v1/admin-users")]
public sealed class AdminUsersController(AdminDataRepository repository) : ControllerBase
{
    [HttpGet]
    public ActionResult<ApiResponse<IReadOnlyList<AdminUser>>> GetUsers()
        => Ok(ApiResponse<IReadOnlyList<AdminUser>>.Ok(repository.GetUsers()));

    [HttpGet("{id}")]
    public ActionResult<ApiResponse<AdminUser>> GetUserById(string id)
    {
        var user = repository.GetUsers().FirstOrDefault(item => item.Id == id);
        return user is null
            ? NotFound(ApiResponse<AdminUser>.Fail("Không tìm thấy tài khoản admin."))
            : Ok(ApiResponse<AdminUser>.Ok(user));
    }

    [HttpPost]
    public ActionResult<ApiResponse<AdminUser>> CreateUser([FromBody] AdminUserUpsertRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Name) || string.IsNullOrWhiteSpace(request.Email))
        {
            return BadRequest(ApiResponse<AdminUser>.Fail("Tên và email là bắt buộc."));
        }

        var saved = repository.SaveUser(null, request);
        return CreatedAtAction(nameof(GetUserById), new { id = saved.Id }, ApiResponse<AdminUser>.Ok(saved, "Tạo tài khoản admin thành công."));
    }

    [HttpPut("{id}")]
    public ActionResult<ApiResponse<AdminUser>> UpdateUser(string id, [FromBody] AdminUserUpsertRequest request)
    {
        var existing = repository.GetUsers().Any(item => item.Id == id);
        if (!existing)
        {
            return NotFound(ApiResponse<AdminUser>.Fail("Không tìm thấy tài khoản admin."));
        }

        var saved = repository.SaveUser(id, request);
        return Ok(ApiResponse<AdminUser>.Ok(saved, "Cập nhật tài khoản admin thành công."));
    }
}
