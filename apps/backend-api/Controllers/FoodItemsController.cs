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
    public ActionResult<ApiResponse<IReadOnlyList<FoodItem>>> GetFoodItems([FromQuery] string? placeId)
    {
        IEnumerable<FoodItem> query = repository.GetFoodItems();

        if (!string.IsNullOrWhiteSpace(placeId))
        {
            query = query.Where(item => item.PlaceId == placeId);
        }

        return Ok(ApiResponse<IReadOnlyList<FoodItem>>.Ok(query.ToList()));
    }

    [HttpPost]
    public ActionResult<ApiResponse<FoodItem>> CreateFoodItem([FromBody] FoodItemUpsertRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.PlaceId) || string.IsNullOrWhiteSpace(request.Name))
        {
            return BadRequest(ApiResponse<FoodItem>.Fail("PlaceId va ten mon an la bat buoc."));
        }

        var saved = repository.SaveFoodItem(null, request);
        return Ok(ApiResponse<FoodItem>.Ok(saved, "Tao mon an thanh cong."));
    }

    [HttpPut("{id}")]
    public ActionResult<ApiResponse<FoodItem>> UpdateFoodItem(string id, [FromBody] FoodItemUpsertRequest request)
    {
        var existing = repository.GetFoodItems().Any(item => item.Id == id);
        if (!existing)
        {
            return NotFound(ApiResponse<FoodItem>.Fail("Khong tim thay mon an."));
        }

        var saved = repository.SaveFoodItem(id, request);
        return Ok(ApiResponse<FoodItem>.Ok(saved, "Cap nhat mon an thanh cong."));
    }

    [HttpDelete("{id}")]
    public ActionResult<ApiResponse<string>> DeleteFoodItem(string id)
    {
        var deleted = repository.DeleteFoodItem(id);
        return deleted
            ? Ok(ApiResponse<string>.Ok(id, "Xoa mon an thanh cong."))
            : NotFound(ApiResponse<string>.Fail("Khong tim thay mon an."));
    }
}
