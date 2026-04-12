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
    public ActionResult<ApiResponse<string>> Register([FromBody] CustomerRegistrationRequest request)
        => StatusCode(StatusCodes.Status410Gone, ApiResponse<string>.Fail("Customer registration has been removed from the public Android app."));

    [HttpPost("login")]
    public ActionResult<ApiResponse<string>> Login([FromBody] CustomerLoginRequest request)
        => StatusCode(StatusCodes.Status410Gone, ApiResponse<string>.Fail("Customer sign-in has been removed from the public Android app."));

    [HttpGet("{id}")]
    public ActionResult<ApiResponse<string>> GetCustomerUserById(string id)
        => StatusCode(StatusCodes.Status410Gone, ApiResponse<string>.Fail("Customer account APIs have been deprecated."));

    [HttpPut("{id}/profile")]
    public ActionResult<ApiResponse<string>> UpdateProfile(string id, [FromBody] CustomerProfileUpdateRequest request)
        => StatusCode(StatusCodes.Status410Gone, ApiResponse<string>.Fail("Customer profile APIs have been deprecated."));

    [HttpPost("{id}/premium/purchase")]
    public ActionResult<ApiResponse<string>> PurchasePremium(
        string id,
        [FromBody] PremiumPurchaseRequest request)
        => StatusCode(StatusCodes.Status410Gone, ApiResponse<string>.Fail("Premium purchases have been removed from the public Android app."));
}
