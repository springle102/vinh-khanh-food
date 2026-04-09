using Microsoft.AspNetCore.Mvc;
using VinhKhanh.BackendApi.Contracts;
using VinhKhanh.BackendApi.Infrastructure;
using VinhKhanh.BackendApi.Models;

namespace VinhKhanh.BackendApi.Controllers;

[ApiController]
[Route("api/v1/users")]
public sealed class UsersController(
    AdminDataRepository repository,
    AdminRequestContextResolver adminRequestContextResolver) : ControllerBase
{
    [HttpGet]
    public ActionResult<ApiResponse<IReadOnlyList<EndUser>>> GetUsers()
        => Ok(ApiResponse<IReadOnlyList<EndUser>>.Ok(
            repository.GetEndUsers(adminRequestContextResolver.RequireAuthenticatedAdmin())));

    [HttpGet("{id}")]
    public ActionResult<ApiResponse<EndUser>> GetUserById(string id)
    {
        var user = repository.GetEndUserById(id, adminRequestContextResolver.RequireAuthenticatedAdmin());
        return user is null
            ? NotFound(ApiResponse<EndUser>.Fail("Khong tim thay nguoi dung cuoi."))
            : Ok(ApiResponse<EndUser>.Ok(user));
    }
}
