using Microsoft.AspNetCore.Mvc;
using VinhKhanh.BackendApi.Contracts;
using VinhKhanh.BackendApi.Infrastructure;
using VinhKhanh.BackendApi.Models;

namespace VinhKhanh.BackendApi.Controllers;

[ApiController]
[Route("api/v1/places")]
public sealed class PlacesController(AdminDataRepository repository) : ControllerBase
{
    [HttpGet]
    public ActionResult<ApiResponse<IReadOnlyList<Place>>> GetPlaces(
        [FromQuery] string? status,
        [FromQuery] string? categoryId,
        [FromQuery] bool? featured,
        [FromQuery] string? search)
    {
        IEnumerable<Place> query = repository.GetPlaces();

        if (!string.IsNullOrWhiteSpace(status))
        {
            query = query.Where(item => item.Status.Equals(status, StringComparison.OrdinalIgnoreCase));
        }

        if (!string.IsNullOrWhiteSpace(categoryId))
        {
            query = query.Where(item => item.CategoryId == categoryId);
        }

        if (featured.HasValue)
        {
            query = query.Where(item => item.Featured == featured.Value);
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            query = query.Where(item =>
                item.Slug.Contains(search, StringComparison.OrdinalIgnoreCase) ||
                item.Address.Contains(search, StringComparison.OrdinalIgnoreCase) ||
                item.Tags.Any(tag => tag.Contains(search, StringComparison.OrdinalIgnoreCase)));
        }

        return Ok(ApiResponse<IReadOnlyList<Place>>.Ok(query.OrderByDescending(item => item.UpdatedAt).ToList()));
    }

    [HttpGet("{id}")]
    public ActionResult<ApiResponse<Place>> GetPlaceById(string id)
    {
        var place = repository.GetPlaces().FirstOrDefault(item => item.Id == id);
        return place is null
            ? NotFound(ApiResponse<Place>.Fail("Khong tim thay dia diem."))
            : Ok(ApiResponse<Place>.Ok(place));
    }

    [HttpPost]
    public ActionResult<ApiResponse<Place>> CreatePlace([FromBody] PlaceUpsertRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Slug) || string.IsNullOrWhiteSpace(request.Address))
        {
            return BadRequest(ApiResponse<Place>.Fail("Slug va dia chi la bat buoc."));
        }

        var saved = repository.SavePlace(null, request);
        return CreatedAtAction(nameof(GetPlaceById), new { id = saved.Id }, ApiResponse<Place>.Ok(saved, "Tao dia diem thanh cong."));
    }

    [HttpPut("{id}")]
    public ActionResult<ApiResponse<Place>> UpdatePlace(string id, [FromBody] PlaceUpsertRequest request)
    {
        var existing = repository.GetPlaces().Any(item => item.Id == id);
        if (!existing)
        {
            return NotFound(ApiResponse<Place>.Fail("Khong tim thay dia diem."));
        }

        var saved = repository.SavePlace(id, request);
        return Ok(ApiResponse<Place>.Ok(saved, "Cap nhat dia diem thanh cong."));
    }

    [HttpDelete("{id}")]
    public ActionResult<ApiResponse<string>> DeletePlace(string id)
    {
        var deleted = repository.DeletePlace(id);
        return deleted
            ? Ok(ApiResponse<string>.Ok(id, "Xoa dia diem thanh cong."))
            : NotFound(ApiResponse<string>.Fail("Khong tim thay dia diem."));
    }
}
