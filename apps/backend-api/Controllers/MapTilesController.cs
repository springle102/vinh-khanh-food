using Microsoft.AspNetCore.Mvc;
using VinhKhanh.BackendApi.Infrastructure;

namespace VinhKhanh.BackendApi.Controllers;

[ApiController]
[Route("map-tiles/osm")]
public sealed class MapTilesController(OpenStreetMapTileProxyService tileProxyService) : ControllerBase
{
    [HttpGet("{zoom:int}/{x:int}/{y:int}.png")]
    [ResponseCache(Duration = 86400, Location = ResponseCacheLocation.Any)]
    public async Task<IActionResult> GetTile(int zoom, int x, int y, CancellationToken cancellationToken)
    {
        if (zoom is < 0 or > 19 || x < 0 || y < 0)
        {
            return NotFound();
        }

        var tile = await tileProxyService.GetTileAsync(zoom, x, y, cancellationToken);
        if (tile is null)
        {
            return NotFound();
        }

        Response.Headers.CacheControl = "public,max-age=86400";
        if (!string.IsNullOrWhiteSpace(tile.ETag))
        {
            Response.Headers.ETag = tile.ETag;
        }

        if (tile.LastModified is DateTimeOffset lastModified)
        {
            Response.Headers.LastModified = lastModified.ToString("R");
        }

        return File(tile.Bytes, tile.ContentType);
    }
}
