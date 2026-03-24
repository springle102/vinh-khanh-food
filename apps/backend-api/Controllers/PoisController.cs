using Microsoft.AspNetCore.Mvc;
using VinhKhanh.BackendApi.Contracts;
using VinhKhanh.BackendApi.Infrastructure;
using VinhKhanh.BackendApi.Models;

namespace VinhKhanh.BackendApi.Controllers;

[ApiController]
[Route("api/v1/pois")]
public sealed class PoisController(AdminDataRepository repository) : ControllerBase
{
    [HttpGet]
    public ActionResult<ApiResponse<IReadOnlyList<Poi>>> GetPois(
        [FromQuery] string? status,
        [FromQuery] string? categoryId,
        [FromQuery] bool? featured,
        [FromQuery] string? search)
    {
        IEnumerable<Poi> query = repository.GetPois();

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

        return Ok(ApiResponse<IReadOnlyList<Poi>>.Ok(query.OrderByDescending(item => item.UpdatedAt).ToList()));
    }

    [HttpGet("{id}")]
    public ActionResult<ApiResponse<Poi>> GetPoiById(string id)
    {
        var poi = repository.GetPois().FirstOrDefault(item => item.Id == id);
        return poi is null
            ? NotFound(ApiResponse<Poi>.Fail("Khong tim thay POI."))
            : Ok(ApiResponse<Poi>.Ok(poi));
    }

    [HttpPost]
    public ActionResult<ApiResponse<Poi>> CreatePoi([FromBody] PoiUpsertRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Slug) || string.IsNullOrWhiteSpace(request.Address))
        {
            return BadRequest(ApiResponse<Poi>.Fail("Slug va dia chi POI la bat buoc."));
        }

        var saved = repository.SavePoi(null, request);
        return CreatedAtAction(nameof(GetPoiById), new { id = saved.Id }, ApiResponse<Poi>.Ok(saved, "Tao POI thanh cong."));
    }

    [HttpPut("{id}")]
    public ActionResult<ApiResponse<Poi>> UpdatePoi(string id, [FromBody] PoiUpsertRequest request)
    {
        var existing = repository.GetPois().Any(item => item.Id == id);
        if (!existing)
        {
            return NotFound(ApiResponse<Poi>.Fail("Khong tim thay POI."));
        }

        var saved = repository.SavePoi(id, request);
        return Ok(ApiResponse<Poi>.Ok(saved, "Cap nhat POI thanh cong."));
    }

    [HttpDelete("{id}")]
    public ActionResult<ApiResponse<string>> DeletePoi(string id)
    {
        var deleted = repository.DeletePoi(id);
        return deleted
            ? Ok(ApiResponse<string>.Ok(id, "Xoa POI thanh cong."))
            : NotFound(ApiResponse<string>.Fail("Khong tim thay POI."));
    }
}
