using Microsoft.AspNetCore.Mvc;
using VinhKhanh.BackendApi.Contracts;
using VinhKhanh.BackendApi.Infrastructure;
using VinhKhanh.BackendApi.Models;

namespace VinhKhanh.BackendApi.Controllers;

[ApiController]
[Route("api/v1/reviews")]
public sealed class ReviewsController(
    AdminDataRepository repository,
    AdminRequestContextResolver adminRequestContextResolver) : ControllerBase
{
    [HttpGet]
    public ActionResult<ApiResponse<IReadOnlyList<Review>>> GetReviews([FromQuery] string? poiId, [FromQuery] string? status)
    {
        var actor = adminRequestContextResolver.TryGetCurrentAdmin();
        IEnumerable<Review> query = repository.GetReviews(actor);

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
            return BadRequest(ApiResponse<Review>.Fail("PoiId va noi dung danh gia la bat buoc."));
        }

        if (request.Rating is < 1 or > 5)
        {
            return BadRequest(ApiResponse<Review>.Fail("So sao danh gia phai tu 1 den 5."));
        }

        var poiExists = repository.GetPois().Any(item => item.Id == request.PoiId);
        if (!poiExists)
        {
            return NotFound(ApiResponse<Review>.Fail("Khong tim thay POI de gui danh gia."));
        }

        var review = repository.CreateReview(request);
        return Ok(ApiResponse<Review>.Ok(review, "Gui danh gia thanh cong, cho duyet tren he thong admin."));
    }

    [HttpPatch("{id}/status")]
    public ActionResult<ApiResponse<Review>> UpdateReviewStatus(string id, [FromBody] ReviewStatusRequest request)
    {
        var actor = adminRequestContextResolver.RequireSuperAdmin();
        if (string.IsNullOrWhiteSpace(request.Status))
        {
            return BadRequest(ApiResponse<Review>.Fail("Trang thai danh gia la bat buoc."));
        }

        var updated = repository.UpdateReviewStatus(id, request, actor);
        return updated is null
            ? NotFound(ApiResponse<Review>.Fail("Khong tim thay danh gia."))
            : Ok(ApiResponse<Review>.Ok(updated, "Cap nhat trang thai danh gia thanh cong."));
    }
}
