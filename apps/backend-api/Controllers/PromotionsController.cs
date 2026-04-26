using Microsoft.AspNetCore.Mvc;
using VinhKhanh.BackendApi.Contracts;
using VinhKhanh.BackendApi.Infrastructure;
using VinhKhanh.BackendApi.Models;

namespace VinhKhanh.BackendApi.Controllers;

[ApiController]
[Route("api/v1/promotions")]
public sealed class PromotionsController(
    AdminDataRepository repository,
    AdminRequestContextResolver adminRequestContextResolver) : ControllerBase
{
    [HttpGet]
    public ActionResult<ApiResponse<IReadOnlyList<Promotion>>> GetPromotions([FromQuery] string? poiId, [FromQuery] string? status)
    {
        var actor = adminRequestContextResolver.TryGetCurrentAdmin();
        IEnumerable<Promotion> query = repository.GetPromotions(actor);

        if (!string.IsNullOrWhiteSpace(poiId))
        {
            query = query.Where(item => item.PoiId == poiId);
        }

        if (!string.IsNullOrWhiteSpace(status))
        {
            query = query.Where(item => item.Status.Equals(status, StringComparison.OrdinalIgnoreCase));
        }

        return Ok(ApiResponse<IReadOnlyList<Promotion>>.Ok(query.OrderByDescending(item => item.StartAt).ToList()));
    }

    [HttpPost]
    public ActionResult<ApiResponse<Promotion>> CreatePromotion([FromBody] PromotionUpsertRequest request)
    {
        var actor = adminRequestContextResolver.RequireAuthenticatedAdmin();
        if (string.IsNullOrWhiteSpace(request.PoiId) || string.IsNullOrWhiteSpace(request.Title))
        {
            return BadRequest(ApiResponse<Promotion>.Fail("PoiId và tiêu đề ưu đãi là bắt buộc."));
        }

        if (!repository.GetPois(actor).Any(item => item.Id == request.PoiId))
        {
            if (actor.IsPlaceOwner)
            {
                throw new ApiForbiddenException("Chu quan chi duoc tao uu dai cho POI cua minh.");
            }

            return NotFound(ApiResponse<Promotion>.Fail("Không tìm thấy POI để tạo ưu đãi."));
        }

        var saved = repository.SavePromotion(null, request, actor);
        return Ok(ApiResponse<Promotion>.Ok(saved, "Tạo ưu đãi thành công."));
    }

    [HttpPut("{id}")]
    public ActionResult<ApiResponse<Promotion>> UpdatePromotion(string id, [FromBody] PromotionUpsertRequest request)
    {
        var actor = adminRequestContextResolver.RequireAuthenticatedAdmin();
        var existing = repository.GetPromotions(actor).FirstOrDefault(item => item.Id == id);
        if (existing is null || !repository.GetPois(actor).Any(item => item.Id == request.PoiId))
        {
            if (actor.IsPlaceOwner)
            {
                throw new ApiForbiddenException("Chu quan chi duoc sua uu dai thuoc POI cua minh.");
            }

            return NotFound(ApiResponse<Promotion>.Fail("Không tìm thấy ưu đãi."));
        }

        var saved = repository.SavePromotion(id, request, actor);
        return Ok(ApiResponse<Promotion>.Ok(saved, "Cập nhật ưu đãi thành công."));
    }

    [HttpDelete("{id}")]
    public ActionResult<ApiResponse<string>> DeletePromotion(string id)
    {
        var actor = adminRequestContextResolver.RequireAuthenticatedAdmin();
        var existing = repository.GetPromotions(actor).FirstOrDefault(item => item.Id == id);
        if (existing is null)
        {
            if (actor.IsPlaceOwner)
            {
                throw new ApiForbiddenException("Chu quan chi duoc xoa uu dai thuoc POI cua minh.");
            }

            return NotFound(ApiResponse<string>.Fail("Không tìm thấy ưu đãi."));
        }

        var deleted = repository.DeletePromotion(id, actor);
        return deleted
            ? Ok(ApiResponse<string>.Ok(id, "Xóa ưu đãi thành công."))
            : NotFound(ApiResponse<string>.Fail("Không tìm thấy ưu đãi."));
    }
}
