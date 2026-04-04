using Microsoft.AspNetCore.Mvc;
using VinhKhanh.BackendApi.Contracts;
using VinhKhanh.BackendApi.Infrastructure;
using VinhKhanh.BackendApi.Models;

namespace VinhKhanh.BackendApi.Controllers;

[ApiController]
[Route("api/v1/pois")]
public sealed class PoisController(
    AdminDataRepository repository,
    PoiNarrationService poiNarrationService) : ControllerBase
{
    [HttpGet]
    public ActionResult<ApiResponse<IReadOnlyList<Poi>>> GetPois(
        [FromQuery] string? status,
        [FromQuery] string? categoryId,
        [FromQuery] bool? featured,
        [FromQuery] string? search,
        [FromQuery] string? userId,
        [FromQuery] string? role)
    {
        IEnumerable<Poi> query = repository.GetPois(userId, role);

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
    public ActionResult<ApiResponse<Poi>> GetPoiById(
        string id,
        [FromQuery] string? userId,
        [FromQuery] string? role)
    {
        var poi = repository.GetPois(userId, role).FirstOrDefault(item => item.Id == id);
        return poi is null
            ? NotFound(ApiResponse<Poi>.Fail("Không tìm thấy POI."))
            : Ok(ApiResponse<Poi>.Ok(poi));
    }

    [HttpGet("{id}/detail")]
    public ActionResult<ApiResponse<PoiDetailResponse>> GetPoiDetailById(
        string id,
        [FromQuery] string? userId,
        [FromQuery] string? role)
    {
        var poi = repository.GetPois(userId, role).FirstOrDefault(item => item.Id == id);
        if (poi is null)
        {
            return NotFound(ApiResponse<PoiDetailResponse>.Fail("Không tìm thấy POI."));
        }

        var translations = repository.GetTranslations()
            .Where(item => item.EntityType == "poi" && item.EntityId == id)
            .OrderByDescending(item => item.UpdatedAt)
            .ToList();
        var audioGuides = repository.GetAudioGuides()
            .Where(item => item.EntityType == "poi" && item.EntityId == id)
            .OrderByDescending(item => item.UpdatedAt)
            .ToList();

        return Ok(ApiResponse<PoiDetailResponse>.Ok(new PoiDetailResponse(
            poi,
            translations,
            audioGuides)));
    }

    [HttpGet("{id}/narration")]
    public async Task<ActionResult<ApiResponse<PoiNarrationResponse>>> GetPoiNarration(
        string id,
        [FromQuery] string? languageCode,
        [FromQuery] string? voiceType,
        [FromQuery] string? userId,
        [FromQuery] string? role,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(languageCode))
        {
            return BadRequest(ApiResponse<PoiNarrationResponse>.Fail("LanguageCode là bắt buộc."));
        }

        var narration = await poiNarrationService.ResolveAsync(
            id,
            languageCode,
            voiceType,
            userId,
            role,
            cancellationToken);
        return narration is null
            ? NotFound(ApiResponse<PoiNarrationResponse>.Fail("Không tìm thấy POI."))
            : Ok(ApiResponse<PoiNarrationResponse>.Ok(narration));
    }

    [HttpPost]
    public ActionResult<ApiResponse<Poi>> CreatePoi([FromBody] PoiUpsertRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Slug) || string.IsNullOrWhiteSpace(request.Address))
        {
            return BadRequest(ApiResponse<Poi>.Fail("Slug và địa chỉ POI là bắt buộc."));
        }

        var saved = repository.SavePoi(null, request);
        return CreatedAtAction(nameof(GetPoiById), new { id = saved.Id }, ApiResponse<Poi>.Ok(saved, "Tạo POI thành công."));
    }

    [HttpPut("{id}")]
    public ActionResult<ApiResponse<Poi>> UpdatePoi(string id, [FromBody] PoiUpsertRequest request)
    {
        var existing = repository.GetPois().Any(item => item.Id == id);
        if (!existing)
        {
            return NotFound(ApiResponse<Poi>.Fail("Không tìm thấy POI."));
        }

        var saved = repository.SavePoi(id, request);
        return Ok(ApiResponse<Poi>.Ok(saved, "Cập nhật POI thành công."));
    }

    [HttpDelete("{id}")]
    public ActionResult<ApiResponse<string>> DeletePoi(string id)
    {
        var deleted = repository.DeletePoi(id);
        return deleted
            ? Ok(ApiResponse<string>.Ok(id, "Xóa POI thành công."))
            : NotFound(ApiResponse<string>.Fail("Không tìm thấy POI."));
    }
}
