using Microsoft.AspNetCore.Mvc;
using VinhKhanh.BackendApi.Contracts;
using VinhKhanh.BackendApi.Infrastructure;
using VinhKhanh.BackendApi.Models;

namespace VinhKhanh.BackendApi.Controllers;

[ApiController]
[Route("api/v1/pois")]
public sealed class PoisController(
    AdminDataRepository repository,
    AdminRequestContextResolver adminRequestContextResolver,
    PoiNarrationService poiNarrationService,
    ILogger<PoisController> logger) : ControllerBase
{
    [HttpGet]
    public ActionResult<ApiResponse<IReadOnlyList<Poi>>> GetPois(
        [FromQuery] string? status,
        [FromQuery] string? categoryId,
        [FromQuery] bool? featured,
        [FromQuery] string? search)
    {
        var actor = adminRequestContextResolver.TryGetCurrentAdmin();
        IEnumerable<Poi> query = repository.GetPois(actor);

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
        var actor = adminRequestContextResolver.RequireAuthenticatedAdmin();
        var poi = repository.GetPois(actor).FirstOrDefault(item => item.Id == id);
        return poi is null
            ? NotFound(ApiResponse<Poi>.Fail("Khong tim thay POI."))
            : Ok(ApiResponse<Poi>.Ok(poi));
    }

    [HttpGet("{id}/detail")]
    public ActionResult<ApiResponse<PoiDetailResponse>> GetPoiDetailById(string id)
    {
        var actor = adminRequestContextResolver.RequireAuthenticatedAdmin();
        var poi = repository.GetPois(actor).FirstOrDefault(item => item.Id == id);
        if (poi is null)
        {
            return NotFound(ApiResponse<PoiDetailResponse>.Fail("Khong tim thay POI."));
        }

        var translations = repository.GetTranslations(actor).ToList();
        var foodItems = repository.GetFoodItems(actor)
            .Where(item => string.Equals(item.PoiId, id, StringComparison.OrdinalIgnoreCase))
            .OrderBy(item => item.Name)
            .ToList();
        var foodItemIds = foodItems
            .Select(item => item.Id)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var promotions = repository.GetPromotions(actor)
            .Where(item => string.Equals(item.PoiId, id, StringComparison.OrdinalIgnoreCase))
            .OrderBy(item => item.StartAt)
            .ThenBy(item => item.EndAt)
            .ToList();
        var promotionIds = promotions
            .Select(item => item.Id)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var poiTranslations = translations
            .Where(item => item.EntityType == "poi" && item.EntityId == id)
            .OrderByDescending(item => item.UpdatedAt)
            .ToList();
        var foodItemTranslations = translations
            .Where(item =>
                string.Equals(item.EntityType, "food_item", StringComparison.OrdinalIgnoreCase) &&
                foodItemIds.Contains(item.EntityId))
            .OrderByDescending(item => item.UpdatedAt)
            .ToList();
        var promotionTranslations = translations
            .Where(item =>
                string.Equals(item.EntityType, "promotion", StringComparison.OrdinalIgnoreCase) &&
                promotionIds.Contains(item.EntityId))
            .OrderByDescending(item => item.UpdatedAt)
            .ToList();
        var audioGuides = repository.GetAudioGuides(actor)
            .Where(item => item.EntityType == "poi" && item.EntityId == id)
            .OrderByDescending(item => item.UpdatedAt)
            .ToList();
        var mediaAssets = repository.GetMediaAssets(actor)
            .Where(item =>
                (string.Equals(item.EntityType, "poi", StringComparison.OrdinalIgnoreCase) &&
                 string.Equals(item.EntityId, id, StringComparison.OrdinalIgnoreCase)) ||
                (string.Equals(item.EntityType, "food_item", StringComparison.OrdinalIgnoreCase) &&
                 foodItemIds.Contains(item.EntityId)) ||
                (string.Equals(item.EntityType, "promotion", StringComparison.OrdinalIgnoreCase) &&
                 promotionIds.Contains(item.EntityId)))
            .OrderByDescending(item => item.CreatedAt)
            .ToList();

        return Ok(ApiResponse<PoiDetailResponse>.Ok(new PoiDetailResponse(
            poi,
            poiTranslations,
            audioGuides,
            foodItems,
            foodItemTranslations,
            promotions,
            promotionTranslations,
            mediaAssets)));
    }

    [HttpGet("{id}/narration")]
    public async Task<ActionResult<ApiResponse<PoiNarrationResponse>>> GetPoiNarration(
        string id,
        [FromQuery] string? languageCode,
        [FromQuery] string? voiceType,
        [FromQuery] string? customerUserId,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(languageCode))
        {
            return BadRequest(ApiResponse<PoiNarrationResponse>.Fail("LanguageCode is required."));
        }

        var actor = adminRequestContextResolver.TryGetCurrentAdmin();
        var narration = await poiNarrationService.ResolveAsync(
            id,
            languageCode,
            voiceType,
            actor,
            cancellationToken);
        return narration is null
            ? NotFound(ApiResponse<PoiNarrationResponse>.Fail("POI was not found."))
            : Ok(ApiResponse<PoiNarrationResponse>.Ok(narration));
    }

    [HttpPost]
    public ActionResult<ApiResponse<Poi>> CreatePoi([FromBody] PoiUpsertRequest request)
    {
        var actor = adminRequestContextResolver.RequireAuthenticatedAdmin();
        if (actor.IsSuperAdmin)
        {
            throw new ApiForbiddenException("Super Admin không có quyền tạo hoặc chỉnh sửa nội dung POI.");
        }

        logger.LogInformation(
            "CreatePoi request received. requestedPoiId={RequestedPoiId}, slug={Slug}, address={Address}, tags={Tags}, actorRole={ActorRole}, actorUserId={ActorUserId}",
            request.RequestedId,
            request.Slug,
            request.Address,
            string.Join(", ", request.Tags ?? []),
            actor.Role,
            actor.UserId);

        if (string.IsNullOrWhiteSpace(request.Slug) || string.IsNullOrWhiteSpace(request.Address))
        {
            return BadRequest(ApiResponse<Poi>.Fail("Slug va dia chi POI la bat buoc."));
        }

        var sanitizedRequest = request with
        {
            OwnerUserId = actor.IsPlaceOwner ? actor.UserId : request.OwnerUserId,
            UpdatedBy = actor.Name,
            ActorRole = actor.Role,
            ActorUserId = actor.UserId
        };

        var saved = repository.SavePoi(null, sanitizedRequest, actor);
        return CreatedAtAction(nameof(GetPoiById), new { id = saved.Id }, ApiResponse<Poi>.Ok(saved, "Tao POI thanh cong."));
    }

    [HttpPut("{id}")]
    public ActionResult<ApiResponse<Poi>> UpdatePoi(string id, [FromBody] PoiUpsertRequest request)
    {
        var actor = adminRequestContextResolver.RequireAuthenticatedAdmin();
        if (actor.IsSuperAdmin)
        {
            throw new ApiForbiddenException("Bạn không có quyền chỉnh sửa nội dung POI.");
        }

        logger.LogInformation(
            "UpdatePoi request received. poiId={PoiId}, requestedPoiId={RequestedPoiId}, slug={Slug}, address={Address}, tags={Tags}, actorRole={ActorRole}, actorUserId={ActorUserId}",
            id,
            request.RequestedId,
            request.Slug,
            request.Address,
            string.Join(", ", request.Tags ?? []),
            actor.Role,
            actor.UserId);

        var existing = repository.GetPois(actor).Any(item => item.Id == id);
        if (!existing)
        {
            return NotFound(ApiResponse<Poi>.Fail("Khong tim thay POI."));
        }

        var sanitizedRequest = request with
        {
            OwnerUserId = actor.IsPlaceOwner ? actor.UserId : request.OwnerUserId,
            UpdatedBy = actor.Name,
            ActorRole = actor.Role,
            ActorUserId = actor.UserId
        };

        var saved = repository.SavePoi(id, sanitizedRequest, actor);
        return Ok(ApiResponse<Poi>.Ok(saved, "Cap nhat POI thanh cong."));
    }

    [HttpPost("{id}/approve")]
    public ActionResult<ApiResponse<Poi>> ApprovePoi(string id)
    {
        var actor = adminRequestContextResolver.RequireSuperAdmin();
        var saved = repository.ApprovePoi(id, actor);
        return Ok(ApiResponse<Poi>.Ok(saved, "Đã duyệt POI."));
    }

    [HttpPost("{id}/reject")]
    public ActionResult<ApiResponse<Poi>> RejectPoi(string id, [FromBody] PoiDecisionRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Reason))
        {
            return BadRequest(ApiResponse<Poi>.Fail("Lý do từ chối là bắt buộc."));
        }

        var actor = adminRequestContextResolver.RequireSuperAdmin();
        var saved = repository.RejectPoi(id, request.Reason, actor);
        return Ok(ApiResponse<Poi>.Ok(saved, "Đã từ chối POI."));
    }

    [HttpPatch("{id}/toggle-active")]
    public ActionResult<ApiResponse<Poi>> TogglePoiActive(string id, [FromBody] PoiActiveToggleRequest request)
    {
        var actor = adminRequestContextResolver.RequireAuthenticatedAdmin();
        var saved = repository.TogglePoiActive(id, request.IsActive, actor);
        return Ok(ApiResponse<Poi>.Ok(
            saved,
            request.IsActive ? "POI đã được bật hoạt động." : "POI đã được chuyển sang ngừng hoạt động."));
    }

    [HttpDelete("{id}")]
    public ActionResult<ApiResponse<string>> DeletePoi(string id)
    {
        var deleted = repository.DeletePoi(id, adminRequestContextResolver.RequireAuthenticatedAdmin());
        return deleted
            ? Ok(ApiResponse<string>.Ok(id, "Xoa POI thanh cong."))
            : NotFound(ApiResponse<string>.Fail("Khong tim thay POI."));
    }
}
