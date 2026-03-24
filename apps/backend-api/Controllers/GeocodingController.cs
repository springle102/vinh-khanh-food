using Microsoft.AspNetCore.Mvc;
using VinhKhanh.BackendApi.Contracts;
using VinhKhanh.BackendApi.Infrastructure;

namespace VinhKhanh.BackendApi.Controllers;

[ApiController]
[Route("api/v1/geocoding")]
public sealed class GeocodingController(GeocodingProxyService geocoding) : ControllerBase
{
    [HttpGet("reverse")]
    public async Task<ActionResult<ApiResponse<GeocodingLocationResponse>>> Reverse([FromQuery] double lat, [FromQuery] double lng, CancellationToken cancellationToken)
    {
        var location = await geocoding.ReverseAsync(lat, lng, cancellationToken);
        if (location is null || string.IsNullOrWhiteSpace(location.Address))
        {
            return NotFound(ApiResponse<GeocodingLocationResponse>.Fail("ADDRESS_NOT_FOUND"));
        }

        return Ok(ApiResponse<GeocodingLocationResponse>.Ok(location));
    }

    [HttpGet("search")]
    public async Task<ActionResult<ApiResponse<GeocodingLocationResponse>>> Search([FromQuery(Name = "q")] string query, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return BadRequest(ApiResponse<GeocodingLocationResponse>.Fail("ADDRESS_QUERY_REQUIRED"));
        }

        var location = await geocoding.ForwardAsync(query, cancellationToken);
        if (location is null)
        {
            return NotFound(ApiResponse<GeocodingLocationResponse>.Fail("ADDRESS_NOT_FOUND"));
        }

        return Ok(ApiResponse<GeocodingLocationResponse>.Ok(location));
    }
}
