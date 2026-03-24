using Microsoft.AspNetCore.Mvc;
using VinhKhanh.BackendApi.Contracts;
using VinhKhanh.BackendApi.Infrastructure;
using VinhKhanh.BackendApi.Models;

namespace VinhKhanh.BackendApi.Controllers;

[ApiController]
[Route("api/v1/promotions")]
public sealed class PromotionsController(AdminDataRepository repository) : ControllerBase
{
    [HttpGet]
    public ActionResult<ApiResponse<IReadOnlyList<Promotion>>> GetPromotions([FromQuery] string? poiId, [FromQuery] string? status)
    {
        IEnumerable<Promotion> query = repository.GetPromotions();

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
        if (string.IsNullOrWhiteSpace(request.PoiId) || string.IsNullOrWhiteSpace(request.Title))
        {
            return BadRequest(ApiResponse<Promotion>.Fail("PoiId va tieu de uu dai la bat buoc."));
        }

        var saved = repository.SavePromotion(null, request);
        return Ok(ApiResponse<Promotion>.Ok(saved, "Tao uu dai thanh cong."));
    }

    [HttpPut("{id}")]
    public ActionResult<ApiResponse<Promotion>> UpdatePromotion(string id, [FromBody] PromotionUpsertRequest request)
    {
        var existing = repository.GetPromotions().Any(item => item.Id == id);
        if (!existing)
        {
            return NotFound(ApiResponse<Promotion>.Fail("Khong tim thay uu dai."));
        }

        var saved = repository.SavePromotion(id, request);
        return Ok(ApiResponse<Promotion>.Ok(saved, "Cap nhat uu dai thanh cong."));
    }

    [HttpDelete("{id}")]
    public ActionResult<ApiResponse<string>> DeletePromotion(string id)
    {
        var deleted = repository.DeletePromotion(id);
        return deleted
            ? Ok(ApiResponse<string>.Ok(id, "Xoa uu dai thanh cong."))
            : NotFound(ApiResponse<string>.Fail("Khong tim thay uu dai."));
    }
}
