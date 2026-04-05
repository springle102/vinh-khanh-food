using Microsoft.AspNetCore.Mvc;
using VinhKhanh.BackendApi.Contracts;
using VinhKhanh.BackendApi.Infrastructure;
using VinhKhanh.BackendApi.Models;

namespace VinhKhanh.BackendApi.Controllers;

[ApiController]
[Route("api/v1/translations")]
public sealed class TranslationsController(
    AdminDataRepository repository,
    TranslationProxyService translationProxyService,
    ILogger<TranslationsController> logger) : ControllerBase
{
    [HttpGet]
    public ActionResult<ApiResponse<IReadOnlyList<Translation>>> GetTranslations(
        [FromQuery] string? entityType,
        [FromQuery] string? entityId,
        [FromQuery] string? languageCode)
    {
        IEnumerable<Translation> query = repository.GetTranslations();

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
            return BadRequest(ApiResponse<Translation>.Fail("EntityId và languageCode là bắt buộc."));
        }

        var saved = repository.SaveTranslation(null, request);
        return Ok(ApiResponse<Translation>.Ok(saved, "Tạo nội dung thuyết minh thành công."));
    }

    [HttpPut("{id}")]
    public ActionResult<ApiResponse<Translation>> UpdateTranslation(string id, [FromBody] TranslationUpsertRequest request)
    {
        logger.LogInformation(
            "UpdateTranslation request received. translationId={TranslationId}, entityType={EntityType}, entityId={EntityId}, languageCode={LanguageCode}, title={Title}, shortTextLength={ShortTextLength}, fullTextLength={FullTextLength}",
            id,
            request.EntityType,
            request.EntityId,
            request.LanguageCode,
            request.Title,
            request.ShortText?.Length ?? 0,
            request.FullText?.Length ?? 0);

        var existing = repository.GetTranslations().Any(item => item.Id == id);
        if (!existing)
        {
            return NotFound(ApiResponse<Translation>.Fail("Không tìm thấy nội dung thuyết minh."));
        }

        var saved = repository.SaveTranslation(id, request);
        return Ok(ApiResponse<Translation>.Ok(saved, "Cập nhật nội dung thuyết minh thành công."));
    }

    [HttpDelete("{id}")]
    public ActionResult<ApiResponse<string>> DeleteTranslation(string id)
    {
        var deleted = repository.DeleteTranslation(id);
        return deleted
            ? Ok(ApiResponse<string>.Ok(id, "Xóa nội dung thuyết minh thành công."))
            : NotFound(ApiResponse<string>.Fail("Không tìm thấy nội dung thuyết minh."));
    }

    [HttpPost("translate")]
    public async Task<ActionResult<ApiResponse<TextTranslationResponse>>> TranslateTexts(
        [FromBody] TextTranslationRequest request,
        CancellationToken cancellationToken)
    {
        if (request.Texts is null || request.Texts.Count == 0)
        {
            return BadRequest(ApiResponse<TextTranslationResponse>.Fail("Cần ít nhất một đoạn văn bản để dịch."));
        }

        if (string.IsNullOrWhiteSpace(request.TargetLanguageCode))
        {
            return BadRequest(ApiResponse<TextTranslationResponse>.Fail("TargetLanguageCode là bắt buộc."));
        }

        var translated = await translationProxyService.TranslateAsync(request, cancellationToken);
        return Ok(ApiResponse<TextTranslationResponse>.Ok(translated));
    }
}
