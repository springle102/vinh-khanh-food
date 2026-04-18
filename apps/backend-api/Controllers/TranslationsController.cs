using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using VinhKhanh.BackendApi.Contracts;
using VinhKhanh.BackendApi.Infrastructure;
using VinhKhanh.BackendApi.Models;

namespace VinhKhanh.BackendApi.Controllers;

[ApiController]
[Route("api/v1/translations")]
public sealed class TranslationsController(
    AdminDataRepository repository,
    AdminRequestContextResolver adminRequestContextResolver,
    TranslationProxyService translationProxyService,
    PoiPregeneratedAudioService poiPregeneratedAudioService,
    IOptions<TextToSpeechOptions> textToSpeechOptions,
    ILogger<TranslationsController> logger) : ControllerBase
{
    [HttpGet]
    public ActionResult<ApiResponse<IReadOnlyList<Translation>>> GetTranslations(
        [FromQuery] string? entityType,
        [FromQuery] string? entityId,
        [FromQuery] string? languageCode)
    {
        var actor = adminRequestContextResolver.RequireAuthenticatedAdmin();
        EnsureTourTranslationAccess(actor, entityType: entityType);
        IEnumerable<Translation> query = repository.GetTranslations(actor);

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

        return Ok(ApiResponse<IReadOnlyList<Translation>>.Ok(query.OrderByDescending(item => item.UpdatedAt).ToList()));
    }

    [HttpPost]
    public async Task<ActionResult<ApiResponse<Translation>>> CreateTranslation([FromBody] TranslationUpsertRequest request, CancellationToken cancellationToken)
    {
        var actor = adminRequestContextResolver.RequireAuthenticatedAdmin();
        EnsureTourTranslationAccess(actor, entityType: request.EntityType);

        logger.LogInformation(
            "CreateTranslation request received. entityType={EntityType}, entityId={EntityId}, languageCode={LanguageCode}, title={Title}, shortTextLength={ShortTextLength}, fullTextLength={FullTextLength}",
            request.EntityType,
            request.EntityId,
            request.LanguageCode,
            request.Title,
            request.ShortText?.Length ?? 0,
            request.FullText?.Length ?? 0);

        if (string.IsNullOrWhiteSpace(request.EntityId) || string.IsNullOrWhiteSpace(request.LanguageCode))
        {
            return BadRequest(ApiResponse<Translation>.Fail("EntityId va languageCode la bat buoc."));
        }

        if (!CanManageEntity(actor, request.EntityType, request.EntityId))
        {
            return NotFound(ApiResponse<Translation>.Fail("Khong tim thay tai nguyen de cap nhat noi dung thuyet minh."));
        }

        var saved = repository.SaveTranslation(null, request with { UpdatedBy = actor.Name }, actor);
        await TryAutoRegeneratePoiAudioAsync(saved, actor, cancellationToken);
        return Ok(ApiResponse<Translation>.Ok(saved, "Tao noi dung thuyet minh thanh cong."));
    }

    [HttpPut("{id}")]
    public async Task<ActionResult<ApiResponse<Translation>>> UpdateTranslation(string id, [FromBody] TranslationUpsertRequest request, CancellationToken cancellationToken)
    {
        var actor = adminRequestContextResolver.RequireAuthenticatedAdmin();
        EnsureTourTranslationAccess(actor, entityType: request.EntityType, translationId: id);

        logger.LogInformation(
            "UpdateTranslation request received. translationId={TranslationId}, entityType={EntityType}, entityId={EntityId}, languageCode={LanguageCode}, title={Title}, shortTextLength={ShortTextLength}, fullTextLength={FullTextLength}",
            id,
            request.EntityType,
            request.EntityId,
            request.LanguageCode,
            request.Title,
            request.ShortText?.Length ?? 0,
            request.FullText?.Length ?? 0);

        var existing = repository.GetTranslations(actor).FirstOrDefault(item => item.Id == id);
        if (existing is null || !CanManageEntity(actor, request.EntityType, request.EntityId))
        {
            return NotFound(ApiResponse<Translation>.Fail("Khong tim thay noi dung thuyet minh."));
        }

        var saved = repository.SaveTranslation(id, request with { UpdatedBy = actor.Name }, actor);
        await TryAutoRegeneratePoiAudioAsync(saved, actor, cancellationToken);
        return Ok(ApiResponse<Translation>.Ok(saved, "Cap nhat noi dung thuyet minh thanh cong."));
    }

    [HttpDelete("{id}")]
    public ActionResult<ApiResponse<string>> DeleteTranslation(string id)
    {
        var actor = adminRequestContextResolver.RequireAuthenticatedAdmin();
        EnsureTourTranslationAccess(actor, translationId: id);
        var existing = repository.GetTranslations(actor).FirstOrDefault(item => item.Id == id);
        if (existing is null)
        {
            return NotFound(ApiResponse<string>.Fail("Khong tim thay noi dung thuyet minh."));
        }

        var deleted = repository.DeleteTranslation(id, actor);
        return deleted
            ? Ok(ApiResponse<string>.Ok(id, "Xoa noi dung thuyet minh thanh cong."))
            : NotFound(ApiResponse<string>.Fail("Khong tim thay noi dung thuyet minh."));
    }

    [HttpPost("translate")]
    public async Task<ActionResult<ApiResponse<TextTranslationResponse>>> TranslateTexts(
        [FromBody] TextTranslationRequest request,
        [FromQuery] string? customerUserId,
        CancellationToken cancellationToken)
    {
        if (request.Texts is null || request.Texts.Count == 0)
        {
            return BadRequest(ApiResponse<TextTranslationResponse>.Fail("Can it nhat mot doan van ban de dich."));
        }

        if (string.IsNullOrWhiteSpace(request.TargetLanguageCode))
        {
            return BadRequest(ApiResponse<TextTranslationResponse>.Fail("TargetLanguageCode la bat buoc."));
        }

        var translated = await translationProxyService.TranslateAsync(request, cancellationToken);
        return Ok(ApiResponse<TextTranslationResponse>.Ok(translated));
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

    private void EnsureTourTranslationAccess(
        AdminRequestContext actor,
        string? entityType = null,
        string? translationId = null)
    {
        if (actor.IsSuperAdmin)
        {
            return;
        }

        if (IsRouteEntityType(entityType) ||
            (!string.IsNullOrWhiteSpace(translationId) && repository.IsRouteTranslation(translationId)))
        {
            throw new ApiForbiddenException("Chi Super Admin moi duoc quan ly noi dung cua tour.");
        }
    }

    private static bool IsRouteEntityType(string? entityType) =>
        string.Equals(entityType?.Trim(), "route", StringComparison.OrdinalIgnoreCase);

    private async Task TryAutoRegeneratePoiAudioAsync(
        Translation translation,
        AdminRequestContext actor,
        CancellationToken cancellationToken)
    {
        if (!textToSpeechOptions.Value.AutoRegenerateWhenTextChanges)
        {
            return;
        }

        if (!string.Equals(NormalizeEntityType(translation.EntityType), "poi", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        if (!CanManageEntity(actor, translation.EntityType, translation.EntityId))
        {
            return;
        }

        try
        {
            logger.LogInformation(
                "Auto-regenerating POI audio after narration save. poiId={PoiId}; languageCode={LanguageCode}; actor={Actor}",
                translation.EntityId,
                translation.LanguageCode,
                actor.UserId);

            await poiPregeneratedAudioService.GeneratePoiLanguageAsync(
                translation.EntityId,
                new PoiAudioGenerationRequest(
                    translation.LanguageCode,
                    null,
                    null,
                    null,
                    ForceRegenerate: true),
                actor,
                cancellationToken);
        }
        catch (Exception exception) when (
            exception is InvalidOperationException ||
            exception is HttpRequestException ||
            exception is TaskCanceledException)
        {
            logger.LogWarning(
                exception,
                "Auto-regenerate POI audio failed after narration save. poiId={PoiId}; languageCode={LanguageCode}",
                translation.EntityId,
                translation.LanguageCode);
        }
    }
}
