using Microsoft.AspNetCore.Mvc;
using VinhKhanh.BackendApi.Contracts;
using VinhKhanh.BackendApi.Infrastructure;
using VinhKhanh.BackendApi.Models;

namespace VinhKhanh.BackendApi.Controllers;

[ApiController]
[Route("api/v1/tours")]
public sealed class ToursController(AdminDataRepository repository) : ControllerBase
{
    [HttpGet]
    public ActionResult<ApiResponse<IReadOnlyList<TourRoute>>> GetTours(
        [FromQuery] string? theme,
        [FromQuery] bool? isActive)
    {
        IEnumerable<TourRoute> query = repository.GetRoutes();

        if (!string.IsNullOrWhiteSpace(theme))
        {
            query = query.Where(item => item.Theme.Equals(theme, StringComparison.OrdinalIgnoreCase));
        }

        if (isActive.HasValue)
        {
            query = query.Where(item => item.IsActive == isActive.Value);
        }

        return Ok(ApiResponse<IReadOnlyList<TourRoute>>.Ok(query.ToList()));
    }

    [HttpPost]
    public ActionResult<ApiResponse<TourRoute>> CreateTour([FromBody] TourRouteUpsertRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
        {
            return BadRequest(ApiResponse<TourRoute>.Fail("Tên tour là bắt buộc."));
        }

        if (request.DurationMinutes <= 0)
        {
            return BadRequest(ApiResponse<TourRoute>.Fail("Thời lượng tour phải lớn hơn 0 phút."));
        }

        if (request.StopPoiIds is null || request.StopPoiIds.Count == 0)
        {
            return BadRequest(ApiResponse<TourRoute>.Fail("Tour phải có ít nhất một điểm đến."));
        }

        var saved = repository.SaveRoute(null, request);
        return Ok(ApiResponse<TourRoute>.Ok(saved, "Tạo tour thành công."));
    }

    [HttpPut("{id}")]
    public ActionResult<ApiResponse<TourRoute>> UpdateTour(string id, [FromBody] TourRouteUpsertRequest request)
    {
        var existing = repository.GetRoutes().Any(item => item.Id == id);
        if (!existing)
        {
            return NotFound(ApiResponse<TourRoute>.Fail("Không tìm thấy tour."));
        }

        var saved = repository.SaveRoute(id, request);
        return Ok(ApiResponse<TourRoute>.Ok(saved, "Cập nhật tour thành công."));
    }

    [HttpDelete("{id}")]
    public ActionResult<ApiResponse<string>> DeleteTour(string id)
    {
        var deleted = repository.DeleteRoute(id);
        return deleted
            ? Ok(ApiResponse<string>.Ok(id, "Xóa tour thành công."))
            : NotFound(ApiResponse<string>.Fail("Không tìm thấy tour."));
    }
}
