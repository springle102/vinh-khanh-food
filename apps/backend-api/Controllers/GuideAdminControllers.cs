using System.Security.Claims;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VinhKhanh.BackendApi.Application.Interfaces;
using VinhKhanh.BackendApi.Contracts;
using VinhKhanh.BackendApi.DTOs;

namespace VinhKhanh.BackendApi.Controllers;

[ApiController]
[Route("api/guide/v1/admin/analytics")]
[Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme, Roles = "SUPER_ADMIN,PLACE_OWNER")]
public sealed class GuideAnalyticsController(IAnalyticsService analyticsService) : ControllerBase
{
    [HttpGet("overview")]
    public async Task<ActionResult<ApiResponse<AnalyticsOverviewDto>>> GetOverview(
        [FromQuery] string language = "vi",
        CancellationToken cancellationToken = default)
    {
        var analytics = await analyticsService.GetOverviewAsync(language, cancellationToken);
        return Ok(ApiResponse<AnalyticsOverviewDto>.Ok(analytics));
    }
}

[ApiController]
[Route("api/guide/v1/admin/pois")]
[Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme, Roles = "SUPER_ADMIN,PLACE_OWNER")]
public sealed class GuideAdminPoisController(IPoiService poiService) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<ApiResponse<PagedResult<PoiSummaryDto>>>> GetPois(
        [FromQuery] string language = "vi",
        [FromQuery] string? search = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50,
        CancellationToken cancellationToken = default)
    {
        var result = await poiService.GetPoisAsync(
            new PoiSearchCriteria(language, search, null, null, null, null, page, pageSize),
            cancellationToken);

        if (IsPlaceOwner() && !string.IsNullOrWhiteSpace(GetManagedPoiId()))
        {
            var filtered = result.Items.Where(item => item.Id == GetManagedPoiId()).ToList();
            return Ok(ApiResponse<PagedResult<PoiSummaryDto>>.Ok(new PagedResult<PoiSummaryDto>(filtered, 1, filtered.Count, filtered.Count, 1)));
        }

        return Ok(ApiResponse<PagedResult<PoiSummaryDto>>.Ok(result));
    }

    [HttpPost]
    public async Task<ActionResult<ApiResponse<PoiDetailDto>>> CreatePoi(
        [FromBody] AdminPoiUpsertRequestDto request,
        CancellationToken cancellationToken)
    {
        var normalized = NormalizeRequestForPlaceOwner(request);
        var result = await poiService.SavePoiAsync(null, normalized, cancellationToken);
        return CreatedAtAction(nameof(GetPoiById), new { id = result.Id, language = normalized.DefaultLanguageCode }, ApiResponse<PoiDetailDto>.Ok(result, "Tao POI thanh cong."));
    }

    [HttpGet("{id}")]
    public async Task<ActionResult<ApiResponse<PoiDetailDto>>> GetPoiById(
        string id,
        [FromQuery] string language = "vi",
        CancellationToken cancellationToken = default)
    {
        EnsureManagePermission(id);
        var result = await poiService.GetPoiByIdAsync(id, language, cancellationToken);
        return result is null
            ? NotFound(ApiResponse<PoiDetailDto>.Fail("Khong tim thay POI."))
            : Ok(ApiResponse<PoiDetailDto>.Ok(result));
    }

    [HttpPut("{id}")]
    public async Task<ActionResult<ApiResponse<PoiDetailDto>>> UpdatePoi(
        string id,
        [FromBody] AdminPoiUpsertRequestDto request,
        CancellationToken cancellationToken)
    {
        EnsureManagePermission(id);
        var normalized = NormalizeRequestForPlaceOwner(request);
        var result = await poiService.SavePoiAsync(id, normalized, cancellationToken);
        return Ok(ApiResponse<PoiDetailDto>.Ok(result, "Cap nhat POI thanh cong."));
    }

    [HttpDelete("{id}")]
    public async Task<ActionResult<ApiResponse<string>>> DeletePoi(string id, CancellationToken cancellationToken)
    {
        EnsureManagePermission(id);
        var deleted = await poiService.DeletePoiAsync(id, cancellationToken);
        return deleted
            ? Ok(ApiResponse<string>.Ok(id, "Xoa POI thanh cong."))
            : NotFound(ApiResponse<string>.Fail("Khong tim thay POI."));
    }

    [HttpPut("{poiId}/translations/{languageCode}")]
    public async Task<ActionResult<ApiResponse<PoiNarrationDto>>> SaveTranslation(
        string poiId,
        string languageCode,
        [FromBody] AdminPoiTranslationUpsertRequestDto request,
        CancellationToken cancellationToken)
    {
        EnsureManagePermission(poiId);
        var result = await poiService.SaveTranslationAsync(poiId, request with { LanguageCode = languageCode }, cancellationToken);
        return Ok(ApiResponse<PoiNarrationDto>.Ok(result, "Cap nhat ban dich thanh cong."));
    }

    [HttpDelete("{poiId}/translations/{languageCode}")]
    public async Task<ActionResult<ApiResponse<string>>> DeleteTranslation(
        string poiId,
        string languageCode,
        CancellationToken cancellationToken)
    {
        EnsureManagePermission(poiId);
        var deleted = await poiService.DeleteTranslationAsync(poiId, languageCode, cancellationToken);
        return deleted
            ? Ok(ApiResponse<string>.Ok(languageCode, "Xoa ban dich thanh cong."))
            : NotFound(ApiResponse<string>.Fail("Khong tim thay ban dich."));
    }

    [HttpPut("{poiId}/audio-guides/{languageCode}")]
    public async Task<ActionResult<ApiResponse<PoiNarrationDto>>> SaveAudioGuide(
        string poiId,
        string languageCode,
        [FromBody] AdminPoiAudioUpsertRequestDto request,
        CancellationToken cancellationToken)
    {
        EnsureManagePermission(poiId);
        var result = await poiService.SaveAudioGuideAsync(poiId, request with { LanguageCode = languageCode }, cancellationToken);
        return Ok(ApiResponse<PoiNarrationDto>.Ok(result, "Cap nhat audio guide thanh cong."));
    }

    [HttpDelete("{poiId}/audio-guides/{languageCode}")]
    public async Task<ActionResult<ApiResponse<string>>> DeleteAudioGuide(
        string poiId,
        string languageCode,
        CancellationToken cancellationToken)
    {
        EnsureManagePermission(poiId);
        var deleted = await poiService.DeleteAudioGuideAsync(poiId, languageCode, cancellationToken);
        return deleted
            ? Ok(ApiResponse<string>.Ok(languageCode, "Xoa audio guide thanh cong."))
            : NotFound(ApiResponse<string>.Fail("Khong tim thay audio guide."));
    }

    [HttpPost("{poiId}/food-items")]
    public async Task<ActionResult<ApiResponse<FoodItemDto>>> CreateFoodItem(
        string poiId,
        [FromBody] AdminFoodItemUpsertRequestDto request,
        CancellationToken cancellationToken)
    {
        EnsureManagePermission(poiId);
        var result = await poiService.SaveFoodItemAsync(poiId, null, request, cancellationToken);
        return Ok(ApiResponse<FoodItemDto>.Ok(result, "Tao mon noi bat thanh cong."));
    }

    [HttpPut("{poiId}/food-items/{foodItemId}")]
    public async Task<ActionResult<ApiResponse<FoodItemDto>>> UpdateFoodItem(
        string poiId,
        string foodItemId,
        [FromBody] AdminFoodItemUpsertRequestDto request,
        CancellationToken cancellationToken)
    {
        EnsureManagePermission(poiId);
        var result = await poiService.SaveFoodItemAsync(poiId, foodItemId, request, cancellationToken);
        return Ok(ApiResponse<FoodItemDto>.Ok(result, "Cap nhat mon noi bat thanh cong."));
    }

    [HttpDelete("{poiId}/food-items/{foodItemId}")]
    public async Task<ActionResult<ApiResponse<string>>> DeleteFoodItem(
        string poiId,
        string foodItemId,
        CancellationToken cancellationToken)
    {
        EnsureManagePermission(poiId);
        var deleted = await poiService.DeleteFoodItemAsync(poiId, foodItemId, cancellationToken);
        return deleted
            ? Ok(ApiResponse<string>.Ok(foodItemId, "Xoa mon noi bat thanh cong."))
            : NotFound(ApiResponse<string>.Fail("Khong tim thay mon noi bat."));
    }

    [HttpPost("{poiId}/media-assets")]
    public async Task<ActionResult<ApiResponse<MediaAssetDto>>> CreateMediaAsset(
        string poiId,
        [FromBody] AdminMediaAssetUpsertRequestDto request,
        CancellationToken cancellationToken)
    {
        EnsureManagePermission(poiId);
        var result = await poiService.SaveMediaAssetAsync(poiId, null, request, cancellationToken);
        return Ok(ApiResponse<MediaAssetDto>.Ok(result, "Them media thanh cong."));
    }

    [HttpPut("{poiId}/media-assets/{mediaAssetId}")]
    public async Task<ActionResult<ApiResponse<MediaAssetDto>>> UpdateMediaAsset(
        string poiId,
        string mediaAssetId,
        [FromBody] AdminMediaAssetUpsertRequestDto request,
        CancellationToken cancellationToken)
    {
        EnsureManagePermission(poiId);
        var result = await poiService.SaveMediaAssetAsync(poiId, mediaAssetId, request, cancellationToken);
        return Ok(ApiResponse<MediaAssetDto>.Ok(result, "Cap nhat media thanh cong."));
    }

    [HttpDelete("{poiId}/media-assets/{mediaAssetId}")]
    public async Task<ActionResult<ApiResponse<string>>> DeleteMediaAsset(
        string poiId,
        string mediaAssetId,
        CancellationToken cancellationToken)
    {
        EnsureManagePermission(poiId);
        var deleted = await poiService.DeleteMediaAssetAsync(poiId, mediaAssetId, cancellationToken);
        return deleted
            ? Ok(ApiResponse<string>.Ok(mediaAssetId, "Xoa media thanh cong."))
            : NotFound(ApiResponse<string>.Fail("Khong tim thay media asset."));
    }

    private AdminPoiUpsertRequestDto NormalizeRequestForPlaceOwner(AdminPoiUpsertRequestDto request)
    {
        if (!IsPlaceOwner())
        {
            return request;
        }

        return request with
        {
            OwnerUserId = User.FindFirstValue(ClaimTypes.NameIdentifier),
            UpdatedBy = User.Identity?.Name ?? request.UpdatedBy
        };
    }

    private void EnsureManagePermission(string poiId)
    {
        if (!IsPlaceOwner())
        {
            return;
        }

        var managedPoiId = GetManagedPoiId();
        if (!string.Equals(managedPoiId, poiId, StringComparison.Ordinal))
        {
            throw new UnauthorizedAccessException("Tai khoan PLACE_OWNER chi duoc quan ly POI da duoc gan.");
        }
    }

    private bool IsPlaceOwner() => User.IsInRole("PLACE_OWNER");

    private string? GetManagedPoiId() => User.FindFirst("managed_poi_id")?.Value;
}
