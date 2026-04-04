using Microsoft.AspNetCore.Mvc;
using VinhKhanh.BackendApi.Contracts;
using VinhKhanh.BackendApi.Infrastructure;
using VinhKhanh.BackendApi.Models;

namespace VinhKhanh.BackendApi.Controllers;

[ApiController]
[Route("api/v1/food-items")]
public sealed class FoodItemsController(AdminDataRepository repository) : ControllerBase
{
    [HttpGet]
    public ActionResult<ApiResponse<IReadOnlyList<FoodItem>>> GetFoodItems([FromQuery] string? poiId)
    {
        IEnumerable<FoodItem> query = repository.GetFoodItems();

        if (!string.IsNullOrWhiteSpace(poiId))
        {
            query = query.Where(item => item.PoiId == poiId);
        }

        return Ok(ApiResponse<IReadOnlyList<FoodItem>>.Ok(query.ToList()));
    }

    [HttpPost]
    public ActionResult<ApiResponse<FoodItem>> CreateFoodItem([FromBody] FoodItemUpsertRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.PoiId) || string.IsNullOrWhiteSpace(request.Name))
        {
            return BadRequest(ApiResponse<FoodItem>.Fail("PoiId và tên món ăn là bắt buộc."));
        }

        var saved = repository.SaveFoodItem(null, request);
        return Ok(ApiResponse<FoodItem>.Ok(saved, "Tạo món ăn thành công."));
    }

    [HttpPut("{id}")]
    public ActionResult<ApiResponse<FoodItem>> UpdateFoodItem(string id, [FromBody] FoodItemUpsertRequest request)
    {
        var existing = repository.GetFoodItems().Any(item => item.Id == id);
        if (!existing)
        {
            return NotFound(ApiResponse<FoodItem>.Fail("Không tìm thấy món ăn."));
        }

        var saved = repository.SaveFoodItem(id, request);
        return Ok(ApiResponse<FoodItem>.Ok(saved, "Cập nhật món ăn thành công."));
    }

    [HttpDelete("{id}")]
    public ActionResult<ApiResponse<string>> DeleteFoodItem(string id)
    {
        var deleted = repository.DeleteFoodItem(id);
        return deleted
            ? Ok(ApiResponse<string>.Ok(id, "Xóa món ăn thành công."))
            : NotFound(ApiResponse<string>.Fail("Không tìm thấy món ăn."));
    }
}
