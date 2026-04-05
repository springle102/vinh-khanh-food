using Microsoft.AspNetCore.Mvc;
using VinhKhanh.BackendApi.Contracts;
using VinhKhanh.BackendApi.Infrastructure;
using VinhKhanh.BackendApi.Models;

namespace VinhKhanh.BackendApi.Controllers;

[ApiController]
[Route("api/v1/media-assets")]
public sealed class MediaAssetsController(AdminDataRepository repository) : ControllerBase
{
    [HttpGet]
    public ActionResult<ApiResponse<IReadOnlyList<MediaAsset>>> GetMediaAssets(
        [FromQuery] string? entityType,
        [FromQuery] string? entityId,
        [FromQuery] string? type)
    {
        IEnumerable<MediaAsset> query = repository.GetMediaAssets();

        if (!string.IsNullOrWhiteSpace(entityType))
        {
            query = query.Where(item => item.EntityType.Equals(entityType, StringComparison.OrdinalIgnoreCase));
        }

        if (!string.IsNullOrWhiteSpace(entityId))
        {
            query = query.Where(item => item.EntityId == entityId);
        }

        if (!string.IsNullOrWhiteSpace(type))
        {
            query = query.Where(item => item.Type.Equals(type, StringComparison.OrdinalIgnoreCase));
        }

        return Ok(ApiResponse<IReadOnlyList<MediaAsset>>.Ok(query.OrderByDescending(item => item.CreatedAt).ToList()));
    }

    [HttpPost]
    public ActionResult<ApiResponse<MediaAsset>> CreateMediaAsset([FromBody] MediaAssetUpsertRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.EntityId) || string.IsNullOrWhiteSpace(request.Url))
        {
            return BadRequest(ApiResponse<MediaAsset>.Fail("EntityId và url là bắt buộc."));
        }

        var saved = repository.SaveMediaAsset(null, request);
        return Ok(ApiResponse<MediaAsset>.Ok(saved, "Tạo media asset thành công."));
    }

    [HttpPut("{id}")]
    public ActionResult<ApiResponse<MediaAsset>> UpdateMediaAsset(string id, [FromBody] MediaAssetUpsertRequest request)
    {
        var existing = repository.GetMediaAssets().Any(item => item.Id == id);
        if (!existing)
        {
            return NotFound(ApiResponse<MediaAsset>.Fail("Không tìm thấy media asset."));
        }

        var saved = repository.SaveMediaAsset(id, request);
        return Ok(ApiResponse<MediaAsset>.Ok(saved, "Cập nhật media asset thành công."));
    }

    [HttpDelete("{id}")]
    public ActionResult<ApiResponse<string>> DeleteMediaAsset(string id)
    {
        var deleted = repository.DeleteMediaAsset(id);
        return deleted
            ? Ok(ApiResponse<string>.Ok(id, "Xóa media asset thành công."))
            : NotFound(ApiResponse<string>.Fail("Không tìm thấy media asset."));
    }
}
