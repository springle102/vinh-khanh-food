using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Http;

namespace VinhKhanh.BackendApi.Infrastructure;

public static class PublicDownloadAnalytics
{
    public const string QrScanSource = "public_download_apk";
    private const int QrScanDedupeWindowSeconds = 30;

    public static string BuildQrScanMetadata(HttpRequest request, string requestPath)
        => $"path={NormalizeRequestPath(requestPath)};method={request.Method}";

    public static string BuildQrScanIdempotencyKey(HttpRequest request, string requestPath)
    {
        var bucket = DateTimeOffset.UtcNow.ToUnixTimeSeconds() / QrScanDedupeWindowSeconds;
        var fingerprint = string.Join(
            "|",
            NormalizeRequestPath(requestPath),
            ResolveClientAddress(request),
            request.Headers.UserAgent.ToString().Trim(),
            request.Headers.AcceptLanguage.ToString().Trim());
        var fingerprintHash = Convert.ToHexString(
                SHA256.HashData(Encoding.UTF8.GetBytes(fingerprint)))
            .ToLowerInvariant()[..24];

        return $"qr-public-download:{bucket}:{fingerprintHash}";
    }

    private static string NormalizeRequestPath(string? requestPath)
    {
        var path = string.IsNullOrWhiteSpace(requestPath)
            ? "/"
            : requestPath.Trim();
        return path.StartsWith("/", StringComparison.Ordinal)
            ? path.ToLowerInvariant()
            : $"/{path.ToLowerInvariant()}";
    }

    private static string ResolveClientAddress(HttpRequest request)
    {
        var forwardedFor = request.Headers["X-Forwarded-For"].ToString();
        if (!string.IsNullOrWhiteSpace(forwardedFor))
        {
            var firstForwardedAddress = forwardedFor
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .FirstOrDefault();
            if (!string.IsNullOrWhiteSpace(firstForwardedAddress))
            {
                return firstForwardedAddress;
            }
        }

        return request.HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
    }
}
