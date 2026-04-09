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

        var translations = repository.GetTranslations(actor)
            .Where(item => item.EntityType == "poi" && item.EntityId == id)
            .OrderByDescending(item => item.UpdatedAt)
            .ToList();
        var audioGuides = repository.GetAudioGuides(actor)
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
        [FromQuery] string? customerUserId,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(languageCode))
        {
            return BadRequest(ApiResponse<PoiNarrationResponse>.Fail("LanguageCode la bat buoc."));
        }

        var actor = adminRequestContextResolver.TryGetCurrentAdmin();
        var accessDecision = repository.EvaluateCustomerLanguageAccess(customerUserId, languageCode);
        var bypassPremiumGate = actor is not null || string.IsNullOrWhiteSpace(customerUserId);
        if (!accessDecision.IsAllowed && !(bypassPremiumGate && accessDecision.RequiresPremium))
        {
            return StatusCode(StatusCodes.Status403Forbidden, ApiResponse<PoiNarrationResponse>.Fail(accessDecision.Message));
        }

        var narration = await poiNarrationService.ResolveAsync(
            id,
            accessDecision.LanguageCode,
            voiceType,
            actor,
            cancellationToken);
        return narration is null
            ? NotFound(ApiResponse<PoiNarrationResponse>.Fail("Khong tim thay POI."))
            : Ok(ApiResponse<PoiNarrationResponse>.Ok(narration));
    }

    [HttpPost]
    public ActionResult<ApiResponse<Poi>> CreatePoi([FromBody] PoiUpsertRequest request)
    {
        var actor = adminRequestContextResolver.RequireAuthenticatedAdmin();

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

    [HttpDelete("{id}")]
    public ActionResult<ApiResponse<string>> DeletePoi(string id)
    {
        var deleted = repository.DeletePoi(id, adminRequestContextResolver.RequireAuthenticatedAdmin());
        return deleted
            ? Ok(ApiResponse<string>.Ok(id, "Xoa POI thanh cong."))
            : NotFound(ApiResponse<string>.Fail("Khong tim thay POI."));
    }
}
