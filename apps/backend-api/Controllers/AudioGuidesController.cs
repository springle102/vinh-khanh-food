using Microsoft.AspNetCore.Mvc;
using VinhKhanh.BackendApi.Contracts;
using VinhKhanh.BackendApi.Infrastructure;
using VinhKhanh.BackendApi.Models;

namespace VinhKhanh.BackendApi.Controllers;

[ApiController]
[Route("api/v1/audio-guides")]
public sealed class AudioGuidesController(
    AdminDataRepository repository,
    AdminRequestContextResolver adminRequestContextResolver,
    PoiPregeneratedAudioService poiPregeneratedAudioService,
    ILogger<AudioGuidesController> logger) : ControllerBase
{
    [HttpGet]
    public ActionResult<ApiResponse<IReadOnlyList<AudioGuide>>> GetAudioGuides(
        [FromQuery] string? entityType,
        [FromQuery] string? entityId,
        [FromQuery] string? languageCode,
        [FromQuery] string? status,
        [FromQuery] string? generationStatus,
        [FromQuery] bool? isOutdated)
    {
        var actor = adminRequestContextResolver.RequireAuthenticatedAdmin();
        EnsureTourAudioAccess(actor, entityType: entityType);
        IEnumerable<AudioGuide> query = repository.GetAudioGuides(actor);

        if (!string.IsNullOrWhiteSpace(entityType))
        {
            query = query.Where(item => item.EntityType.Equals(entityType, StringComparison.OrdinalIgnoreCase));
        }

        if (!string.IsNullOrWhiteSpace(entityId))
        {
            query = query.Where(item => item.EntityId == entityId);
        }

        if (!string.IsNullOrWhiteSpace(languageCode))
        {
            query = query.Where(item => item.LanguageCode.Equals(languageCode, StringComparison.OrdinalIgnoreCase));
        }

        if (!string.IsNullOrWhiteSpace(status))
        {
            query = query.Where(item => item.Status.Equals(status, StringComparison.OrdinalIgnoreCase));
        }

        if (!string.IsNullOrWhiteSpace(generationStatus))
        {
            query = query.Where(item => item.GenerationStatus.Equals(generationStatus, StringComparison.OrdinalIgnoreCase));
        }

        if (isOutdated.HasValue)
        {
            query = query.Where(item => item.IsOutdated == isOutdated.Value);
        }

        return Ok(ApiResponse<IReadOnlyList<AudioGuide>>.Ok(query.OrderByDescending(item => item.UpdatedAt).ToList()));
    }

    [HttpGet("metadata/{id}")]
    public ActionResult<ApiResponse<AudioGuide>> GetAudioGuideMetadata(string id)
    {
        var actor = adminRequestContextResolver.RequireAuthenticatedAdmin();
        EnsureTourAudioAccess(actor, audioGuideId: id);
        var audioGuide = repository.GetAudioGuides(actor).FirstOrDefault(item => item.Id == id);
        return audioGuide is null
            ? NotFound(ApiResponse<AudioGuide>.Fail("Khong tim thay audio guide."))
            : Ok(ApiResponse<AudioGuide>.Ok(audioGuide));
    }

    [HttpGet("poi/{poiId}/status")]
    public ActionResult<ApiResponse<IReadOnlyList<AudioGuide>>> GetPoiAudioStatus(string poiId)
    {
        var actor = adminRequestContextResolver.RequireAuthenticatedAdmin();
        if (!CanManagePoi(actor, poiId))
        {
            return NotFound(ApiResponse<IReadOnlyList<AudioGuide>>.Fail("Khong tim thay POI de xem trang thai audio."));
        }

        var items = poiPregeneratedAudioService.GetPoiAudioGuides(poiId, actor);
        return Ok(ApiResponse<IReadOnlyList<AudioGuide>>.Ok(items));
    }

    [HttpPost("poi/{poiId}/generate")]
    public async Task<ActionResult<ApiResponse<PoiAudioGenerationResult>>> GeneratePoiLanguageAudio(
        string poiId,
        [FromBody] PoiAudioGenerationRequest request,
        CancellationToken cancellationToken)
    {
        var actor = adminRequestContextResolver.RequireAuthenticatedAdmin();
        if (!CanManagePoi(actor, poiId))
        {
            return NotFound(ApiResponse<PoiAudioGenerationResult>.Fail("Khong tim thay POI de generate audio."));
        }

        if (string.IsNullOrWhiteSpace(request.LanguageCode))
        {
            return BadRequest(ApiResponse<PoiAudioGenerationResult>.Fail("LanguageCode la bat buoc."));
        }

        logger.LogInformation(
            "Admin requested single-language POI audio generation. poiId={PoiId}; languageCode={LanguageCode}; force={ForceRegenerate}; actor={Actor}",
            poiId,
            request.LanguageCode,
            request.ForceRegenerate,
            actor.UserId);

        var result = await poiPregeneratedAudioService.GeneratePoiLanguageAsync(
            poiId,
            request,
            actor,
            cancellationToken);

        return Ok(ApiResponse<PoiAudioGenerationResult>.Ok(
            result,
            result.Success ? result.Message : $"Generate that bai: {result.Message}"));
    }

    [HttpPost("poi/{poiId}/regenerate")]
    public Task<ActionResult<ApiResponse<PoiAudioGenerationResult>>> RegeneratePoiLanguageAudio(
        string poiId,
        [FromBody] PoiAudioGenerationRequest request,
        CancellationToken cancellationToken)
        => GeneratePoiLanguageAudio(
            poiId,
            request with { ForceRegenerate = true },
            cancellationToken);

    [HttpPost("poi/{poiId}/generate-all")]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<PoiAudioGenerationResult>>>> GeneratePoiAllLanguagesAudio(
        string poiId,
        [FromBody] PoiAudioBulkGenerationRequest? request,
        CancellationToken cancellationToken)
    {
        var actor = adminRequestContextResolver.RequireAuthenticatedAdmin();
        if (!CanManagePoi(actor, poiId))
        {
            return NotFound(ApiResponse<IReadOnlyList<PoiAudioGenerationResult>>.Fail("Khong tim thay POI de generate audio."));
        }

        var effectiveRequest = request ?? new PoiAudioBulkGenerationRequest();
        logger.LogInformation(
            "Admin requested all-language POI audio generation. poiId={PoiId}; force={ForceRegenerate}; actor={Actor}",
            poiId,
            effectiveRequest.ForceRegenerate,
            actor.UserId);

        var results = await poiPregeneratedAudioService.GeneratePoiAllLanguagesAsync(
            poiId,
            effectiveRequest,
            actor,
            cancellationToken);

        return Ok(ApiResponse<IReadOnlyList<PoiAudioGenerationResult>>.Ok(results));
    }

    [HttpPost("bulk/generate")]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<PoiAudioGenerationResult>>>> GenerateBulkAudio(
        [FromBody] PoiAudioBulkGenerationRequest? request,
        CancellationToken cancellationToken)
    {
        var actor = adminRequestContextResolver.RequireAuthenticatedAdmin();
        EnsureBulkGenerationAccess(actor);

        var effectiveRequest = request ?? new PoiAudioBulkGenerationRequest();
        logger.LogInformation(
            "Admin requested bulk POI audio generation. force={ForceRegenerate}; includeMissing={IncludeMissing}; includeFailed={IncludeFailed}; includeOutdated={IncludeOutdated}; actor={Actor}",
            effectiveRequest.ForceRegenerate,
            effectiveRequest.IncludeMissing,
            effectiveRequest.IncludeFailed,
            effectiveRequest.IncludeOutdated,
            actor.UserId);

        var results = await poiPregeneratedAudioService.GenerateBulkAsync(
            effectiveRequest,
            actor,
            cancellationToken);

        return Ok(ApiResponse<IReadOnlyList<PoiAudioGenerationResult>>.Ok(results));
    }

    [HttpPost]
    public ActionResult<ApiResponse<AudioGuide>> CreateAudioGuide([FromBody] AudioGuideUpsertRequest request)
    {
        var actor = adminRequestContextResolver.RequireAuthenticatedAdmin();
        EnsureTourAudioAccess(actor, entityType: request.EntityType);
        if (string.IsNullOrWhiteSpace(request.EntityId) || string.IsNullOrWhiteSpace(request.LanguageCode))
        {
            return BadRequest(ApiResponse<AudioGuide>.Fail("EntityId va languageCode la bat buoc."));
        }

        if (!CanManageEntity(actor, request.EntityType, request.EntityId))
        {
            return NotFound(ApiResponse<AudioGuide>.Fail("Khong tim thay tai nguyen de cap nhat audio guide."));
        }

        var saved = repository.SaveAudioGuide(null, request with { UpdatedBy = actor.Name }, actor);
        return Ok(ApiResponse<AudioGuide>.Ok(saved, "Tao audio guide thanh cong."));
    }

    [HttpPut("{id}")]
    public ActionResult<ApiResponse<AudioGuide>> UpdateAudioGuide(string id, [FromBody] AudioGuideUpsertRequest request)
    {
        var actor = adminRequestContextResolver.RequireAuthenticatedAdmin();
        EnsureTourAudioAccess(actor, entityType: request.EntityType, audioGuideId: id);
        var existing = repository.GetAudioGuides(actor).FirstOrDefault(item => item.Id == id);
        if (existing is null || !CanManageEntity(actor, request.EntityType, request.EntityId))
        {
            return NotFound(ApiResponse<AudioGuide>.Fail("Khong tim thay audio guide."));
        }

        var saved = repository.SaveAudioGuide(id, request with { UpdatedBy = actor.Name }, actor);
        return Ok(ApiResponse<AudioGuide>.Ok(saved, "Cap nhat audio guide thanh cong."));
    }

    [HttpDelete("{id}")]
    public ActionResult<ApiResponse<string>> DeleteAudioGuide(string id)
    {
        var actor = adminRequestContextResolver.RequireAuthenticatedAdmin();
        EnsureTourAudioAccess(actor, audioGuideId: id);
        var existing = repository.GetAudioGuides(actor).FirstOrDefault(item => item.Id == id);
        if (existing is null)
        {
            return NotFound(ApiResponse<string>.Fail("Khong tim thay audio guide."));
        }

        var deleted = repository.DeleteAudioGuide(id, actor);
        return deleted
            ? Ok(ApiResponse<string>.Ok(id, "Xoa audio guide thanh cong."))
            : NotFound(ApiResponse<string>.Fail("Khong tim thay audio guide."));
    }

    private bool CanManagePoi(AdminRequestContext actor, string poiId)
        => repository.GetPois(actor).Any(item => string.Equals(item.Id, poiId, StringComparison.OrdinalIgnoreCase));

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

    private void EnsureBulkGenerationAccess(AdminRequestContext actor)
    {
        if (!actor.IsSuperAdmin)
        {
            throw new ApiForbiddenException("Chi super admin moi duoc generate audio hang loat.");
        }
    }

    private void EnsureTourAudioAccess(
        AdminRequestContext actor,
        string? entityType = null,
        string? audioGuideId = null)
    {
        if (actor.IsSuperAdmin)
        {
            return;
        }

        if (IsRouteEntityType(entityType) ||
            (!string.IsNullOrWhiteSpace(audioGuideId) && repository.IsRouteAudioGuide(audioGuideId)))
        {
            throw new ApiForbiddenException("Chi Super Admin moi duoc quan ly audio guide cua tour.");
        }
    }

    private static bool IsRouteEntityType(string? entityType) =>
        string.Equals(entityType?.Trim(), "route", StringComparison.OrdinalIgnoreCase);
}
