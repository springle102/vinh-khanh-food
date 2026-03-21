using Microsoft.AspNetCore.Mvc;
using VinhKhanh.BackendApi.Contracts;
using VinhKhanh.BackendApi.Infrastructure;
using VinhKhanh.BackendApi.Models;

namespace VinhKhanh.BackendApi.Controllers;

[ApiController]
[Route("api/v1/audio-guides")]
public sealed class AudioGuidesController(AdminDataRepository repository) : ControllerBase
{
    [HttpGet]
    public ActionResult<ApiResponse<IReadOnlyList<AudioGuide>>> GetAudioGuides(
        [FromQuery] string? entityType,
        [FromQuery] string? entityId,
        [FromQuery] string? languageCode,
        [FromQuery] string? status)
    {
        IEnumerable<AudioGuide> query = repository.GetAudioGuides();

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

        return Ok(ApiResponse<IReadOnlyList<AudioGuide>>.Ok(query.OrderByDescending(item => item.UpdatedAt).ToList()));
    }

    [HttpPost]
    public ActionResult<ApiResponse<AudioGuide>> CreateAudioGuide([FromBody] AudioGuideUpsertRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.EntityId) || string.IsNullOrWhiteSpace(request.LanguageCode))
        {
            return BadRequest(ApiResponse<AudioGuide>.Fail("EntityId va languageCode la bat buoc."));
        }

        var saved = repository.SaveAudioGuide(null, request);
        return Ok(ApiResponse<AudioGuide>.Ok(saved, "Tao audio guide thanh cong."));
    }

    [HttpPut("{id}")]
    public ActionResult<ApiResponse<AudioGuide>> UpdateAudioGuide(string id, [FromBody] AudioGuideUpsertRequest request)
    {
        var existing = repository.GetAudioGuides().Any(item => item.Id == id);
        if (!existing)
        {
            return NotFound(ApiResponse<AudioGuide>.Fail("Khong tim thay audio guide."));
        }

        var saved = repository.SaveAudioGuide(id, request);
        return Ok(ApiResponse<AudioGuide>.Ok(saved, "Cap nhat audio guide thanh cong."));
    }

    [HttpDelete("{id}")]
    public ActionResult<ApiResponse<string>> DeleteAudioGuide(string id)
    {
        var deleted = repository.DeleteAudioGuide(id);
        return deleted
            ? Ok(ApiResponse<string>.Ok(id, "Xoa audio guide thanh cong."))
            : NotFound(ApiResponse<string>.Fail("Khong tim thay audio guide."));
    }
}
