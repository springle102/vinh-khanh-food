using Microsoft.AspNetCore.Mvc;
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
    ILogger<TranslationsController> logger) : ControllerBase
{
    [HttpGet]
    public ActionResult<ApiResponse<IReadOnlyList<Translation>>> GetTranslations(
        [FromQuery] string? entityType,
        [FromQuery] string? entityId,
        [FromQuery] string? languageCode)
    {
        var actor = adminRequestContextResolver.RequireAuthenticatedAdmin();
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
    public ActionResult<ApiResponse<Translation>> CreateTranslation([FromBody] TranslationUpsertRequest request)
    {
        var actor = adminRequestContextResolver.RequireAuthenticatedAdmin();

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
        return Ok(ApiResponse<Translation>.Ok(saved, "Tao noi dung thuyet minh thanh cong."));
    }

    [HttpPut("{id}")]
    public ActionResult<ApiResponse<Translation>> UpdateTranslation(string id, [FromBody] TranslationUpsertRequest request)
    {
        var actor = adminRequestContextResolver.RequireAuthenticatedAdmin();

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
        return Ok(ApiResponse<Translation>.Ok(saved, "Cap nhat noi dung thuyet minh thanh cong."));
    }

    [HttpDelete("{id}")]
    public ActionResult<ApiResponse<string>> DeleteTranslation(string id)
    {
        var actor = adminRequestContextResolver.RequireAuthenticatedAdmin();
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

        if (!string.IsNullOrWhiteSpace(customerUserId))
        {
            var accessDecision = repository.EvaluateCustomerLanguageAccess(customerUserId, request.TargetLanguageCode);
            if (!accessDecision.IsAllowed)
            {
                return StatusCode(StatusCodes.Status403Forbidden, ApiResponse<TextTranslationResponse>.Fail(accessDecision.Message));
            }

            request = request with { TargetLanguageCode = accessDecision.LanguageCode };
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
            "route" => repository.GetRoutes(actor).Any(item =>
                item.Id == entityId &&
                (actor.IsSuperAdmin || string.Equals(item.OwnerUserId, actor.UserId, StringComparison.OrdinalIgnoreCase))),
            _ => false
        };
    }

    private static string NormalizeEntityType(string value)
        => string.Equals(value.Trim(), "food-item", StringComparison.OrdinalIgnoreCase)
            ? "food_item"
            : value.Trim().ToLowerInvariant();
}
