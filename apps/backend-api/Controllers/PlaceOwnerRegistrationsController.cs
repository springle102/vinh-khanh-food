using Microsoft.AspNetCore.Mvc;
using VinhKhanh.BackendApi.Contracts;
using VinhKhanh.BackendApi.Infrastructure;
using VinhKhanh.BackendApi.Models;

namespace VinhKhanh.BackendApi.Controllers;

[ApiController]
[Route("api/v1/place-owner-registrations")]
public sealed class PlaceOwnerRegistrationsController(
    AdminDataRepository repository,
    AdminRequestContextResolver adminRequestContextResolver) : ControllerBase
{
    [HttpPost]
    public ActionResult<ApiResponse<PlaceOwnerRegistrationResponse>> Create([FromBody] PlaceOwnerRegistrationRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Name) ||
            string.IsNullOrWhiteSpace(request.Email) ||
            string.IsNullOrWhiteSpace(request.Password) ||
            string.IsNullOrWhiteSpace(request.ConfirmPassword) ||
            string.IsNullOrWhiteSpace(request.Phone))
        {
            return BadRequest(ApiResponse<PlaceOwnerRegistrationResponse>.Fail(
                "Họ tên, email, mật khẩu, xác nhận mật khẩu và số điện thoại là bắt buộc."));
        }

        var saved = repository.CreatePlaceOwnerRegistration(request);
        return Ok(ApiResponse<PlaceOwnerRegistrationResponse>.Ok(
            ToPlaceOwnerRegistrationResponse(saved),
            "Gửi đăng ký thành công. Tài khoản của bạn đang chờ admin phê duyệt."));
    }

    [HttpPost("access")]
    public ActionResult<ApiResponse<PlaceOwnerRegistrationResponse>> Access([FromBody] PlaceOwnerRegistrationAccessRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Email) || string.IsNullOrWhiteSpace(request.Password))
        {
            return BadRequest(ApiResponse<PlaceOwnerRegistrationResponse>.Fail("Email và mật khẩu là bắt buộc."));
        }

        var registration = repository.AccessPlaceOwnerRegistration(request);
        return Ok(ApiResponse<PlaceOwnerRegistrationResponse>.Ok(ToPlaceOwnerRegistrationResponse(registration)));
    }

    [HttpPut("{id}/self")]
    public ActionResult<ApiResponse<PlaceOwnerRegistrationResponse>> Resubmit(
        string id,
        [FromBody] PlaceOwnerRegistrationResubmitRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Name) ||
            string.IsNullOrWhiteSpace(request.Email) ||
            string.IsNullOrWhiteSpace(request.Password) ||
            string.IsNullOrWhiteSpace(request.ConfirmPassword) ||
            string.IsNullOrWhiteSpace(request.Phone) ||
            string.IsNullOrWhiteSpace(request.CurrentPassword))
        {
            return BadRequest(ApiResponse<PlaceOwnerRegistrationResponse>.Fail(
                "Họ tên, email, mật khẩu, xác nhận mật khẩu, số điện thoại và mật khẩu hiện tại là bắt buộc."));
        }

        var saved = repository.ResubmitPlaceOwnerRegistration(id, request);
        return Ok(ApiResponse<PlaceOwnerRegistrationResponse>.Ok(
            ToPlaceOwnerRegistrationResponse(saved),
            "Đã cập nhật và gửi lại hồ sơ đăng ký."));
    }

    [HttpPost("{id}/approve")]
    public ActionResult<ApiResponse<PlaceOwnerRegistrationResponse>> Approve(string id)
    {
        var actor = adminRequestContextResolver.RequireSuperAdmin();
        var saved = repository.ApprovePlaceOwnerRegistration(id, actor);
        return Ok(ApiResponse<PlaceOwnerRegistrationResponse>.Ok(
            ToPlaceOwnerRegistrationResponse(saved),
            "Đã phê duyệt hồ sơ chủ quán."));
    }

    [HttpPost("{id}/reject")]
    public ActionResult<ApiResponse<PlaceOwnerRegistrationResponse>> Reject(
        string id,
        [FromBody] PlaceOwnerRegistrationDecisionRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Reason))
        {
            return BadRequest(ApiResponse<PlaceOwnerRegistrationResponse>.Fail("Lý do từ chối là bắt buộc."));
        }

        var actor = adminRequestContextResolver.RequireSuperAdmin();
        var saved = repository.RejectPlaceOwnerRegistration(id, request.Reason, actor);
        return Ok(ApiResponse<PlaceOwnerRegistrationResponse>.Ok(
            ToPlaceOwnerRegistrationResponse(saved),
            "Đã từ chối hồ sơ chủ quán."));
    }

    private static PlaceOwnerRegistrationResponse ToPlaceOwnerRegistrationResponse(AdminUser user)
        => new(
            user.Id,
            user.Name,
            user.Email,
            user.Phone,
            user.Status,
            user.ApprovalStatus,
            user.RejectionReason,
            user.CreatedAt,
            user.RegistrationSubmittedAt,
            user.RegistrationReviewedAt);
}
