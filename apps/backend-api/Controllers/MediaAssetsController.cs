using Microsoft.AspNetCore.Mvc;
using VinhKhanh.BackendApi.Contracts;
using VinhKhanh.BackendApi.Infrastructure;
using VinhKhanh.BackendApi.Models;

namespace VinhKhanh.BackendApi.Controllers;

[ApiController]
[Route("api/v1/media-assets")]
public sealed class MediaAssetsController(
    AdminDataRepository repository,
    AdminRequestContextResolver adminRequestContextResolver,
    ResponseUrlNormalizer responseUrlNormalizer) : ControllerBase
{
    [HttpGet]
    public ActionResult<ApiResponse<IReadOnlyList<MediaAsset>>> GetMediaAssets(
        [FromQuery] string? entityType,
        [FromQuery] string? entityId,
        [FromQuery] string? type)
    {
        var actor = adminRequestContextResolver.RequireAuthenticatedAdmin();
        IEnumerable<MediaAsset> query = repository.GetMediaAssets(actor);

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

        return Ok(ApiResponse<IReadOnlyList<MediaAsset>>.Ok(
            query
                .OrderByDescending(item => item.CreatedAt)
                .Select(responseUrlNormalizer.Normalize)
                .ToList()));
    }

    [HttpPost]
    public ActionResult<ApiResponse<MediaAsset>> CreateMediaAsset([FromBody] MediaAssetUpsertRequest request)
    {
        var actor = adminRequestContextResolver.RequireAuthenticatedAdmin();
        if (string.IsNullOrWhiteSpace(request.EntityId) || string.IsNullOrWhiteSpace(request.Url))
        {
            return BadRequest(ApiResponse<MediaAsset>.Fail("EntityId va url la bat buoc."));
        }

        if (!CanManageEntity(actor, request.EntityType, request.EntityId))
        {
            return NotFound(ApiResponse<MediaAsset>.Fail("Khong tim thay tai nguyen de cap nhat media."));
        }

        var saved = responseUrlNormalizer.Normalize(repository.SaveMediaAsset(null, request, actor));
        return Ok(ApiResponse<MediaAsset>.Ok(saved, "Tao media asset thanh cong."));
    }

    [HttpPut("{id}")]
    public ActionResult<ApiResponse<MediaAsset>> UpdateMediaAsset(string id, [FromBody] MediaAssetUpsertRequest request)
    {
        var actor = adminRequestContextResolver.RequireAuthenticatedAdmin();
        var existing = repository.GetMediaAssets(actor).FirstOrDefault(item => item.Id == id);
        if (existing is null || !CanManageEntity(actor, request.EntityType, request.EntityId))
        {
            return NotFound(ApiResponse<MediaAsset>.Fail("Khong tim thay media asset."));
        }

        var saved = responseUrlNormalizer.Normalize(repository.SaveMediaAsset(id, request, actor));
        return Ok(ApiResponse<MediaAsset>.Ok(saved, "Cap nhat media asset thanh cong."));
    }

    [HttpDelete("{id}")]
    public ActionResult<ApiResponse<string>> DeleteMediaAsset(string id)
    {
        var actor = adminRequestContextResolver.RequireAuthenticatedAdmin();
        var existing = repository.GetMediaAssets(actor).FirstOrDefault(item => item.Id == id);
        if (existing is null)
        {
            return NotFound(ApiResponse<string>.Fail("Khong tim thay media asset."));
        }

        var deleted = repository.DeleteMediaAsset(id, actor);
        return deleted
            ? Ok(ApiResponse<string>.Ok(id, "Xoa media asset thanh cong."))
            : NotFound(ApiResponse<string>.Fail("Khong tim thay media asset."));
    }

    private bool CanManageEntity(AdminRequestContext actor, string? entityType, string? entityId)
    {
        if (string.IsNullOrWhiteSpace(entityType) || string.IsNullOrWhiteSpace(entityId))
        {
            return false;
        }

        return NormalizeEntityType(entityType) switch
        {
            "poi" => repository.GetPois(actor).Any(item => item.Id == entityId),
            "food_item" => repository.GetFoodItems(actor).Any(item => item.Id == entityId),
            "promotion" => repository.GetPromotions(actor).Any(item => item.Id == entityId),
            "route" => repository.GetRoutes(actor).Any(item =>
                item.Id == entityId &&
                (actor.IsSuperAdmin || string.Equals(item.OwnerUserId, actor.UserId, StringComparison.OrdinalIgnoreCase))),
            _ => false
        };
    }

    private static string NormalizeEntityType(string value)
        => string.Equals(value.Trim(), "food-item", StringComparison.OrdinalIgnoreCase)
            ? "food_item"
            : string.Equals(value.Trim(), "place", StringComparison.OrdinalIgnoreCase)
                ? "poi"
                : value.Trim().ToLowerInvariant();
}
