using Microsoft.AspNetCore.Mvc;
using VinhKhanh.BackendApi.Contracts;
using VinhKhanh.BackendApi.Infrastructure;
using VinhKhanh.BackendApi.Models;

namespace VinhKhanh.BackendApi.Controllers;

[ApiController]
[Route("api/v1/customer-users")]
public sealed class CustomerUsersController(AdminDataRepository repository) : ControllerBase
{
    [HttpGet("{id}")]
    public ActionResult<ApiResponse<CustomerUser>> GetCustomerUserById(string id)
    {
        var customer = repository.GetCustomerUserById(id);
        return customer is null
            ? NotFound(ApiResponse<CustomerUser>.Fail("Không tìm thấy khách hàng."))
            : Ok(ApiResponse<CustomerUser>.Ok(customer));
    }

    [HttpPut("{id}/profile")]
    public ActionResult<ApiResponse<CustomerUser>> UpdateProfile(string id, [FromBody] CustomerProfileUpdateRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Name) ||
            string.IsNullOrWhiteSpace(request.Email) ||
            string.IsNullOrWhiteSpace(request.Phone))
        {
            return BadRequest(ApiResponse<CustomerUser>.Fail("Tên, email và số điện thoại là bắt buộc."));
        }

        try
        {
            var updated = repository.UpdateCustomerProfile(id, request);
            return updated is null
                ? NotFound(ApiResponse<CustomerUser>.Fail("Không tìm thấy khách hàng."))
                : Ok(ApiResponse<CustomerUser>.Ok(updated, "Cập nhật hồ sơ khách hàng thành công."));
        }
        catch (ArgumentException exception)
        {
            return BadRequest(ApiResponse<CustomerUser>.Fail(exception.Message));
        }
    }
}
