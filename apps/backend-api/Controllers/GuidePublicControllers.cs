using Microsoft.AspNetCore.Mvc;
using VinhKhanh.BackendApi.Application.Interfaces;
using VinhKhanh.BackendApi.Contracts;
using VinhKhanh.BackendApi.DTOs;

namespace VinhKhanh.BackendApi.Controllers;

[ApiController]
[Route("api/guide/v1/auth/admin")]
public sealed class GuideAuthController(IAuthService authService) : ControllerBase
{
    [HttpPost("login")]
    public async Task<ActionResult<ApiResponse<TokenResponseDto>>> Login(
        [FromBody] AdminLoginRequestDto request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Email) || string.IsNullOrWhiteSpace(request.Password))
        {
            return BadRequest(ApiResponse<TokenResponseDto>.Fail("Email va mat khau la bat buoc."));
        }

        var response = await authService.LoginAsync(request, cancellationToken);
        return response is null
            ? Unauthorized(ApiResponse<TokenResponseDto>.Fail("Thong tin dang nhap khong hop le."))
            : Ok(ApiResponse<TokenResponseDto>.Ok(response, "Dang nhap thanh cong."));
    }

    [HttpPost("refresh")]
    public async Task<ActionResult<ApiResponse<TokenResponseDto>>> Refresh(
        [FromBody] AdminRefreshTokenRequestDto request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.RefreshToken))
        {
            return BadRequest(ApiResponse<TokenResponseDto>.Fail("Refresh token la bat buoc."));
        }

        var response = await authService.RefreshAsync(request.RefreshToken, cancellationToken);
        return response is null
            ? Unauthorized(ApiResponse<TokenResponseDto>.Fail("Refresh token khong hop le hoac da het han."))
            : Ok(ApiResponse<TokenResponseDto>.Ok(response, "Lam moi token thanh cong."));
    }

    [HttpPost("logout")]
    public async Task<ActionResult<ApiResponse<string>>> Logout(
        [FromBody] AdminRefreshTokenRequestDto request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.RefreshToken))
        {
            return BadRequest(ApiResponse<string>.Fail("Refresh token la bat buoc."));
        }

        await authService.LogoutAsync(request.RefreshToken, cancellationToken);
        return Ok(ApiResponse<string>.Ok("logged_out", "Dang xuat thanh cong."));
    }
}

[ApiController]
[Route("api/guide/v1/settings")]
public sealed class GuideSettingsController(ISettingsService settingsService) : ControllerBase
{
    [HttpGet("mobile")]
    public async Task<ActionResult<ApiResponse<MobileSettingsDto>>> GetMobileSettings(CancellationToken cancellationToken)
    {
        var settings = await settingsService.GetMobileSettingsAsync(cancellationToken);
        return Ok(ApiResponse<MobileSettingsDto>.Ok(settings));
    }
}

[ApiController]
[Route("api/guide/v1/pois")]
public sealed class GuidePoisController(
    IPoiService poiService,
    IAnalyticsService analyticsService) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<ApiResponse<PagedResult<PoiSummaryDto>>>> GetPois(
        [FromQuery] string language = "vi",
        [FromQuery] string? search = null,
        [FromQuery] string? categoryId = null,
        [FromQuery] string? area = null,
        [FromQuery] string? dish = null,
        [FromQuery] bool? featured = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken cancellationToken = default)
    {
        var criteria = new PoiSearchCriteria(
            language,
            search,
            categoryId,
            area,
            dish,
            featured,
            page <= 0 ? 1 : page,
            pageSize <= 0 ? 20 : Math.Min(pageSize, 100));

        var result = await poiService.GetPoisAsync(criteria, cancellationToken);
        return Ok(ApiResponse<PagedResult<PoiSummaryDto>>.Ok(result));
    }

    [HttpGet("nearby")]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<PoiSummaryDto>>>> GetNearbyPois(
        [FromQuery] double lat,
        [FromQuery] double lng,
        [FromQuery] double radiusMeters = 80,
        [FromQuery] string language = "vi",
        CancellationToken cancellationToken = default)
    {
        var result = await poiService.GetNearbyPoisAsync(language, lat, lng, radiusMeters, cancellationToken);
        return Ok(ApiResponse<IReadOnlyList<PoiSummaryDto>>.Ok(result));
    }

    [HttpGet("routes")]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<TourRouteDto>>>> GetRoutes(
        [FromQuery] string language = "vi",
        CancellationToken cancellationToken = default)
    {
        var routes = await poiService.GetFeaturedRoutesAsync(language, cancellationToken);
        return Ok(ApiResponse<IReadOnlyList<TourRouteDto>>.Ok(routes));
    }

    [HttpGet("{id}")]
    public async Task<ActionResult<ApiResponse<PoiDetailDto>>> GetById(
        string id,
        [FromQuery] string language = "vi",
        CancellationToken cancellationToken = default)
    {
        var result = await poiService.GetPoiByIdAsync(id, language, cancellationToken);
        return result is null
            ? NotFound(ApiResponse<PoiDetailDto>.Fail("Khong tim thay POI."))
            : Ok(ApiResponse<PoiDetailDto>.Ok(result));
    }

    [HttpGet("slug/{slug}")]
    public async Task<ActionResult<ApiResponse<PoiDetailDto>>> GetBySlug(
        string slug,
        [FromQuery] string language = "vi",
        CancellationToken cancellationToken = default)
    {
        var result = await poiService.GetPoiBySlugAsync(slug, language, cancellationToken);
        return result is null
            ? NotFound(ApiResponse<PoiDetailDto>.Fail("Khong tim thay POI theo slug."))
            : Ok(ApiResponse<PoiDetailDto>.Ok(result));
    }

    [HttpGet("qr/{qrCode}")]
    public async Task<ActionResult<ApiResponse<PoiDetailDto>>> GetByQrCode(
        string qrCode,
        [FromQuery] string language = "vi",
        CancellationToken cancellationToken = default)
    {
        var result = await poiService.GetPoiByQrCodeAsync(qrCode, language, cancellationToken);
        return result is null
            ? NotFound(ApiResponse<PoiDetailDto>.Fail("Khong tim thay POI theo QR code."))
            : Ok(ApiResponse<PoiDetailDto>.Ok(result));
    }

    [HttpPost("{id}/events/view")]
    public async Task<ActionResult<ApiResponse<string>>> TrackView(
        string id,
        [FromBody] TrackPoiViewRequestDto request,
        CancellationToken cancellationToken)
    {
        await analyticsService.TrackViewAsync(id, request, cancellationToken);
        return Ok(ApiResponse<string>.Ok("tracked", "Da ghi nhan luot xem."));
    }

    [HttpPost("{id}/events/audio")]
    public async Task<ActionResult<ApiResponse<string>>> TrackAudio(
        string id,
        [FromBody] TrackPoiAudioRequestDto request,
        CancellationToken cancellationToken)
    {
        await analyticsService.TrackAudioPlayAsync(id, request, cancellationToken);
        return Ok(ApiResponse<string>.Ok("tracked", "Da ghi nhan luot nghe audio."));
    }
}
