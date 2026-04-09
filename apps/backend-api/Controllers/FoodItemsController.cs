using Microsoft.AspNetCore.Mvc;
using VinhKhanh.BackendApi.Contracts;
using VinhKhanh.BackendApi.Infrastructure;
using VinhKhanh.BackendApi.Models;

namespace VinhKhanh.BackendApi.Controllers;

[ApiController]
[Route("api/v1/food-items")]
public sealed class FoodItemsController(
    AdminDataRepository repository,
    AdminRequestContextResolver adminRequestContextResolver,
    ResponseUrlNormalizer responseUrlNormalizer) : ControllerBase
{
    [HttpGet]
    public ActionResult<ApiResponse<IReadOnlyList<FoodItem>>> GetFoodItems([FromQuery] string? poiId)
    {
        var actor = adminRequestContextResolver.RequireAuthenticatedAdmin();
        IEnumerable<FoodItem> query = repository.GetFoodItems(actor);

        if (!string.IsNullOrWhiteSpace(poiId))
        {
            query = query.Where(item => item.PoiId == poiId);
        }

        return Ok(ApiResponse<IReadOnlyList<FoodItem>>.Ok(
            query
                .Select(responseUrlNormalizer.Normalize)
                .ToList()));
    }

    [HttpPost]
    public ActionResult<ApiResponse<FoodItem>> CreateFoodItem([FromBody] FoodItemUpsertRequest request)
    {
        var actor = adminRequestContextResolver.RequireAuthenticatedAdmin();
        if (string.IsNullOrWhiteSpace(request.PoiId) || string.IsNullOrWhiteSpace(request.Name))
        {
            return BadRequest(ApiResponse<FoodItem>.Fail("PoiId va ten mon an la bat buoc."));
        }

        if (!repository.GetPois(actor).Any(item => item.Id == request.PoiId))
        {
            return NotFound(ApiResponse<FoodItem>.Fail("Khong tim thay POI de cap nhat mon an."));
        }

        var saved = responseUrlNormalizer.Normalize(repository.SaveFoodItem(null, request, actor));
        return Ok(ApiResponse<FoodItem>.Ok(saved, "Tao mon an thanh cong."));
    }

    [HttpPut("{id}")]
    public ActionResult<ApiResponse<FoodItem>> UpdateFoodItem(string id, [FromBody] FoodItemUpsertRequest request)
    {
        var actor = adminRequestContextResolver.RequireAuthenticatedAdmin();
        var existing = repository.GetFoodItems(actor).FirstOrDefault(item => item.Id == id);
        if (existing is null || !repository.GetPois(actor).Any(item => item.Id == request.PoiId))
        {
            return NotFound(ApiResponse<FoodItem>.Fail("Khong tim thay mon an."));
        }

        var saved = responseUrlNormalizer.Normalize(repository.SaveFoodItem(id, request, actor));
        return Ok(ApiResponse<FoodItem>.Ok(saved, "Cap nhat mon an thanh cong."));
    }

    [HttpDelete("{id}")]
    public ActionResult<ApiResponse<string>> DeleteFoodItem(string id)
    {
        var actor = adminRequestContextResolver.RequireAuthenticatedAdmin();
        var existing = repository.GetFoodItems(actor).FirstOrDefault(item => item.Id == id);
        if (existing is null)
        {
            return NotFound(ApiResponse<string>.Fail("Khong tim thay mon an."));
        }

        var deleted = repository.DeleteFoodItem(id, actor);
        return deleted
            ? Ok(ApiResponse<string>.Ok(id, "Xoa mon an thanh cong."))
            : NotFound(ApiResponse<string>.Fail("Khong tim thay mon an."));
    }
}
