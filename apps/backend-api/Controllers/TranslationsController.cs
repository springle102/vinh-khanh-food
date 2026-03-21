using Microsoft.AspNetCore.Mvc;
using VinhKhanh.BackendApi.Contracts;
using VinhKhanh.BackendApi.Infrastructure;
using VinhKhanh.BackendApi.Models;

namespace VinhKhanh.BackendApi.Controllers;

[ApiController]
[Route("api/v1/translations")]
public sealed class TranslationsController(AdminDataRepository repository) : ControllerBase
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
        if (string.IsNullOrWhiteSpace(request.EntityId) || string.IsNullOrWhiteSpace(request.LanguageCode))
        {
            return BadRequest(ApiResponse<Translation>.Fail("EntityId va languageCode la bat buoc."));
        }

        var saved = repository.SaveTranslation(null, request);
        return Ok(ApiResponse<Translation>.Ok(saved, "Tao noi dung thuyet minh thanh cong."));
    }

    [HttpPut("{id}")]
    public ActionResult<ApiResponse<Translation>> UpdateTranslation(string id, [FromBody] TranslationUpsertRequest request)
    {
        var existing = repository.GetTranslations().Any(item => item.Id == id);
        if (!existing)
        {
            return NotFound(ApiResponse<Translation>.Fail("Khong tim thay noi dung thuyet minh."));
        }

        var saved = repository.SaveTranslation(id, request);
        return Ok(ApiResponse<Translation>.Ok(saved, "Cap nhat noi dung thuyet minh thanh cong."));
    }

    [HttpDelete("{id}")]
    public ActionResult<ApiResponse<string>> DeleteTranslation(string id)
    {
        var deleted = repository.DeleteTranslation(id);
        return deleted
            ? Ok(ApiResponse<string>.Ok(id, "Xoa noi dung thuyet minh thanh cong."))
            : NotFound(ApiResponse<string>.Fail("Khong tim thay noi dung thuyet minh."));
    }
}
