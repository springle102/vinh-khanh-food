using Microsoft.AspNetCore.Mvc;
using VinhKhanh.BackendApi.Contracts;

namespace VinhKhanh.BackendApi.Controllers;

[ApiController]
[Route("api/v1/users")]
public sealed class UsersController : ControllerBase
{
    [HttpGet]
    public ActionResult<ApiResponse<string>> GetUsers()
        => StatusCode(StatusCodes.Status410Gone, ApiResponse<string>.Fail("End-user account APIs have been deprecated."));

    [HttpGet("{id}")]
    public ActionResult<ApiResponse<string>> GetUserById(string id)
        => StatusCode(StatusCodes.Status410Gone, ApiResponse<string>.Fail("End-user account APIs have been deprecated."));
}
