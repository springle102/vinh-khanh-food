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
                ApiResponse<CustomerUser>.Ok(created, "Account created successfully."));
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
            return BadRequest(ApiResponse<CustomerUser>.Fail("Email, username, or phone number and password are required."));
        }

        var customer = repository.LoginCustomer(request.Identifier, request.Password);
        return customer is null
            ? Unauthorized(ApiResponse<CustomerUser>.Fail("Invalid sign-in information."))
            : Ok(ApiResponse<CustomerUser>.Ok(customer, "Signed in successfully."));
    }

    [HttpGet("{id}")]
    public ActionResult<ApiResponse<CustomerUser>> GetCustomerUserById(string id)
    {
        var customer = repository.GetCustomerUserById(id);
        return customer is null
            ? NotFound(ApiResponse<CustomerUser>.Fail("Customer was not found."))
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
            return BadRequest(ApiResponse<CustomerUser>.Fail("Name, username, email, and phone number are required."));
        }

        try
        {
            var updated = repository.UpdateCustomerProfile(id, request);
            return updated is null
                ? NotFound(ApiResponse<CustomerUser>.Fail("Customer was not found."))
                : Ok(ApiResponse<CustomerUser>.Ok(updated, "Customer profile updated successfully."));
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
            return NotFound(ApiResponse<PremiumPurchaseResponse>.Fail("Customer was not found."));
        }

        var response = premiumPurchaseService.Purchase(id, request);
        return Ok(ApiResponse<PremiumPurchaseResponse>.Ok(response, "Premium upgraded successfully."));
    }
}
