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
    public ActionResult<ApiResponse<IReadOnlyList<Review>>> GetReviews([FromQuery] string? poiId, [FromQuery] string? status)
    {
        IEnumerable<Review> query = repository.GetReviews();

        if (!string.IsNullOrWhiteSpace(poiId))
        {
            query = query.Where(item => item.PoiId == poiId);
        }

        if (!string.IsNullOrWhiteSpace(status))
        {
            query = query.Where(item => item.Status.Equals(status, StringComparison.OrdinalIgnoreCase));
        }

        return Ok(ApiResponse<IReadOnlyList<Review>>.Ok(query.OrderByDescending(item => item.CreatedAt).ToList()));
    }

    [HttpPost]
    public ActionResult<ApiResponse<Review>> CreateReview([FromBody] ReviewCreateRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.PoiId) || string.IsNullOrWhiteSpace(request.Comment))
        {
            return BadRequest(ApiResponse<Review>.Fail("PoiId và nội dung đánh giá là bắt buộc."));
        }

        if (request.Rating is < 1 or > 5)
        {
            return BadRequest(ApiResponse<Review>.Fail("Số sao đánh giá phải từ 1 đến 5."));
        }

        var poiExists = repository.GetPois().Any(item => item.Id == request.PoiId);
        if (!poiExists)
        {
            return NotFound(ApiResponse<Review>.Fail("Không tìm thấy POI để gửi đánh giá."));
        }

        var review = repository.CreateReview(request);
        return Ok(ApiResponse<Review>.Ok(review, "Gửi đánh giá thành công, chờ duyệt trên hệ thống admin."));
    }

    [HttpPatch("{id}/status")]
    public ActionResult<ApiResponse<Review>> UpdateReviewStatus(string id, [FromBody] ReviewStatusRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Status))
        {
            return BadRequest(ApiResponse<Review>.Fail("Trạng thái đánh giá là bắt buộc."));
        }

        var updated = repository.UpdateReviewStatus(id, request);
        return updated is null
            ? NotFound(ApiResponse<Review>.Fail("Không tìm thấy đánh giá."))
            : Ok(ApiResponse<Review>.Ok(updated, "Cập nhật trạng thái đánh giá thành công."));
    }
}
