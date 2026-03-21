using Microsoft.AspNetCore.Mvc;
using VinhKhanh.BackendApi.Contracts;
using VinhKhanh.BackendApi.Infrastructure;
using VinhKhanh.BackendApi.Models;

namespace VinhKhanh.BackendApi.Controllers;

[ApiController]
[Route("api/v1/reviews")]
public sealed class ReviewsController(AdminDataRepository repository) : ControllerBase
{
    [HttpGet]
    public ActionResult<ApiResponse<IReadOnlyList<Review>>> GetReviews([FromQuery] string? placeId, [FromQuery] string? status)
    {
        IEnumerable<Review> query = repository.GetReviews();

        if (!string.IsNullOrWhiteSpace(placeId))
        {
            query = query.Where(item => item.PlaceId == placeId);
        }

        if (!string.IsNullOrWhiteSpace(status))
        {
            query = query.Where(item => item.Status.Equals(status, StringComparison.OrdinalIgnoreCase));
        }

        return Ok(ApiResponse<IReadOnlyList<Review>>.Ok(query.OrderByDescending(item => item.CreatedAt).ToList()));
    }

    [HttpPatch("{id}/status")]
    public ActionResult<ApiResponse<Review>> UpdateReviewStatus(string id, [FromBody] ReviewStatusRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Status))
        {
            return BadRequest(ApiResponse<Review>.Fail("Trang thai danh gia la bat buoc."));
        }

        var updated = repository.UpdateReviewStatus(id, request);
        return updated is null
            ? NotFound(ApiResponse<Review>.Fail("Khong tim thay danh gia."))
            : Ok(ApiResponse<Review>.Ok(updated, "Cap nhat trang thai danh gia thanh cong."));
    }
}
