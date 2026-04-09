using Microsoft.AspNetCore.Mvc;
using VinhKhanh.BackendApi.Contracts;
using VinhKhanh.BackendApi.Infrastructure;
using VinhKhanh.BackendApi.Models;

namespace VinhKhanh.BackendApi.Controllers;

[ApiController]
[Route("api/v1/customer-users")]
public sealed class CustomerUsersController(
    AdminDataRepository repository,
    PremiumPurchaseService premiumPurchaseService) : ControllerBase
{
    [HttpPost]
    public ActionResult<ApiResponse<CustomerUser>> Register([FromBody] CustomerRegistrationRequest request)
    {
        try
        {
            var created = repository.CreateCustomerUser(request);
            return CreatedAtAction(
                nameof(GetCustomerUserById),
                new { id = created.Id },
                ApiResponse<CustomerUser>.Ok(created, "Dang ky tai khoan thanh cong."));
        }
        catch (ArgumentException exception)
        {
            return BadRequest(ApiResponse<CustomerUser>.Fail(exception.Message));
        }
        catch (InvalidOperationException exception)
        {
            return Conflict(ApiResponse<CustomerUser>.Fail(exception.Message));
        }
    }

    [HttpPost("login")]
    public ActionResult<ApiResponse<CustomerUser>> Login([FromBody] CustomerLoginRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Identifier) || string.IsNullOrWhiteSpace(request.Password))
        {
            return BadRequest(ApiResponse<CustomerUser>.Fail("Email, username hoac so dien thoai va mat khau la bat buoc."));
        }

        var customer = repository.LoginCustomer(request.Identifier, request.Password);
        return customer is null
            ? Unauthorized(ApiResponse<CustomerUser>.Fail("Thong tin dang nhap khong hop le."))
            : Ok(ApiResponse<CustomerUser>.Ok(customer, "Dang nhap thanh cong."));
    }

    [HttpGet("{id}")]
    public ActionResult<ApiResponse<CustomerUser>> GetCustomerUserById(string id)
    {
        var customer = repository.GetCustomerUserById(id);
        return customer is null
            ? NotFound(ApiResponse<CustomerUser>.Fail("Khong tim thay khach hang."))
            : Ok(ApiResponse<CustomerUser>.Ok(customer));
    }

    [HttpPut("{id}/profile")]
    public ActionResult<ApiResponse<CustomerUser>> UpdateProfile(string id, [FromBody] CustomerProfileUpdateRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Name) ||
            string.IsNullOrWhiteSpace(request.Username) ||
            string.IsNullOrWhiteSpace(request.Email) ||
            string.IsNullOrWhiteSpace(request.Phone))
        {
            return BadRequest(ApiResponse<CustomerUser>.Fail("Ten, username, email va so dien thoai la bat buoc."));
        }

        try
        {
            var updated = repository.UpdateCustomerProfile(id, request);
            return updated is null
                ? NotFound(ApiResponse<CustomerUser>.Fail("Khong tim thay khach hang."))
                : Ok(ApiResponse<CustomerUser>.Ok(updated, "Cap nhat ho so khach hang thanh cong."));
        }
        catch (ArgumentException exception)
        {
            return BadRequest(ApiResponse<CustomerUser>.Fail(exception.Message));
        }
    }

    [HttpPost("{id}/premium/purchase")]
    public ActionResult<ApiResponse<PremiumPurchaseResponse>> PurchasePremium(
        string id,
        [FromBody] PremiumPurchaseRequest request)
    {
        var customer = repository.GetCustomerUserById(id);
        if (customer is null)
        {
            return NotFound(ApiResponse<PremiumPurchaseResponse>.Fail("Khong tim thay khach hang."));
        }

        var response = premiumPurchaseService.Purchase(id, request);
        return Ok(ApiResponse<PremiumPurchaseResponse>.Ok(response, "Nang cap Premium thanh cong."));
    }
}
