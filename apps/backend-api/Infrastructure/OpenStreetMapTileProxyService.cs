using System.Net;

namespace VinhKhanh.BackendApi.Infrastructure;

public sealed class OpenStreetMapTileProxyService(
    HttpClient httpClient,
    ILogger<OpenStreetMapTileProxyService> logger)
{
    private const string TileHost = "https://tile.openstreetmap.org";

    public async Task<ProxiedTileResponse?> GetTileAsync(int zoom, int x, int y, CancellationToken cancellationToken)
    {
        var requestUri = $"{TileHost}/{zoom}/{x}/{y}.png";

        using var response = await httpClient.GetAsync(
            requestUri,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);

        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        response.EnsureSuccessStatusCode();

        await using var sourceStream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var buffer = new MemoryStream();
        await sourceStream.CopyToAsync(buffer, cancellationToken);

        logger.LogDebug(
            "Proxied OpenStreetMap tile. zoom={Zoom}, x={X}, y={Y}, bytes={Length}",
            zoom,
            x,
            y,
            buffer.Length);

        return new ProxiedTileResponse(
            buffer.ToArray(),
            response.Content.Headers.ContentType?.ToString() ?? "image/png",
            response.Content.Headers.LastModified,
            response.Headers.ETag?.Tag);
    }

    public sealed record ProxiedTileResponse(
        byte[] Bytes,
        string ContentType,
        DateTimeOffset? LastModified,
        string? ETag);
}
