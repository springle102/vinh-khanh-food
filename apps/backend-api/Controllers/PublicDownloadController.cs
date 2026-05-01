using System.Net;
using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using VinhKhanh.BackendApi.Contracts;
using VinhKhanh.BackendApi.Infrastructure;

namespace VinhKhanh.BackendApi.Controllers;

[ApiExplorerSettings(IgnoreApi = true)]
public sealed class PublicDownloadController(
    IOptions<MobileDistributionOptions> mobileDistributionOptions,
    IOptions<BlobStorageOptions> blobStorageOptions,
    IBlobStorageService blobStorageService,
    AdminDataRepository repository,
    ILogger<PublicDownloadController> logger) : ControllerBase
{
    private readonly MobileDistributionOptions _options = mobileDistributionOptions.Value;
    private readonly BlobStorageOptions _blobOptions = blobStorageOptions.Value;
    private readonly IBlobStorageService _blobStorageService = blobStorageService;
    private readonly AdminDataRepository _repository = repository;
    private readonly ILogger<PublicDownloadController> _logger = logger;

    [HttpGet("/app")]
    public IActionResult Index()
    {
        var appName = string.IsNullOrWhiteSpace(_options.AppDisplayName)
            ? "Ứng dụng"
            : _options.AppDisplayName.Trim();
        var trackedApkUrl = _options.GetPublicDownloadAppUrl(Request);

        Response.Headers.CacheControl = "no-store, no-cache, must-revalidate, proxy-revalidate";
        Response.Headers.Pragma = "no-cache";
        Response.Headers.Expires = "0";
        Response.Headers["X-Content-Type-Options"] = "nosniff";

        return Content(BuildHtml(appName, trackedApkUrl, trackedApkUrl), "text/html; charset=utf-8", Encoding.UTF8);
    }

    [HttpGet("/download")]
    public Task<IActionResult> Download()
        => HandlePublicAppDownloadAsync(streamFile: true);

    [HttpHead("/download")]
    public Task<IActionResult> DownloadHead()
        => HandlePublicAppDownloadAsync(streamFile: false);

    [HttpGet(MobileDistributionOptions.PublicDownloadAppApiPath)]
    [HttpGet(MobileDistributionOptions.PublicDownloadAppApiAliasPath)]
    public Task<IActionResult> DownloadApp()
        => HandlePublicAppDownloadAsync(streamFile: true);

    [HttpHead(MobileDistributionOptions.PublicDownloadAppApiPath)]
    [HttpHead(MobileDistributionOptions.PublicDownloadAppApiAliasPath)]
    public Task<IActionResult> DownloadAppHead()
        => HandlePublicAppDownloadAsync(streamFile: false);

    [HttpGet("/api/public/diagnostics/qr-scan-count")]
    public ActionResult<ApiResponse<QrScanDiagnosticsResponse>> GetQrScanCount()
    {
        Response.Headers.CacheControl = "no-store, no-cache, must-revalidate, proxy-revalidate";
        Response.Headers.Pragma = "no-cache";
        Response.Headers.Expires = "0";

        var diagnostics = _repository.GetQrScanDiagnostics();
        _logger.LogInformation(
            "[QrScanDiagnostics] qrScanCount={QrScanCount}; publicDownloadQrScanCount={PublicDownloadQrScanCount}; apkDownloadAccessCount={ApkDownloadAccessCount}; dashboardQrTotal={DashboardQrTotal}; databaseServer={DatabaseServer}; databaseName={DatabaseName}",
            diagnostics.QrScanCount,
            diagnostics.PublicDownloadQrScanCount,
            diagnostics.ApkDownloadAccessCount,
            diagnostics.DashboardQrTotal,
            diagnostics.DatabaseServer,
            diagnostics.DatabaseName);

        return Ok(ApiResponse<QrScanDiagnosticsResponse>.Ok(diagnostics));
    }

    private async Task<IActionResult> HandlePublicAppDownloadAsync(bool streamFile)
    {
        var requestPath = Request.Path.Value ?? MobileDistributionOptions.PublicDownloadAppApiPath;
        var userAgent = Request.Headers.UserAgent.ToString();
        var remoteIpAddress = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";

        _logger.LogInformation(
            "[QrScanDownloadApi] Request entered. path={Path}; method={Method}; userAgent={UserAgent}; remoteIpAddress={RemoteIpAddress}",
            requestPath,
            Request.Method,
            userAgent,
            remoteIpAddress);

        var metadata = PublicDownloadAnalytics.BuildQrScanMetadata(Request, requestPath);
        var idempotencyKey = PublicDownloadAnalytics.BuildQrScanIdempotencyKey(Request, requestPath);
        var qrScanSucceeded = false;
        var qrScanCreated = false;
        var qrScanEventId = string.Empty;

        if (HttpMethods.IsGet(Request.Method))
        {
            try
            {
                var result = _repository.TrackQrScanWithResult(
                    PublicDownloadAnalytics.QrScanSource,
                    metadata,
                    idempotencyKey);
                qrScanSucceeded = true;
                qrScanCreated = result.WasCreated;
                qrScanEventId = result.Event.Id;
            }
            catch (Exception exception)
            {
                _logger.LogError(
                    exception,
                    "[QrScanDownloadApi] Failed to write qr_scan. path={Path}; method={Method}",
                    requestPath,
                    Request.Method);
            }
        }

        var apkBlobPath = _blobStorageService.NormalizeBlobPath(_blobOptions.ApkFolder, _options.GetApkFileName());
        if (!_blobStorageService.IsConfigured)
        {
            _logger.LogWarning(
                "[QrScanDownloadApi] Blob storage is not configured for APK redirect. path={Path}; method={Method}; qrScanSucceeded={QrScanSucceeded}; qrScanCreated={QrScanCreated}; streamFile=false; blobPath={BlobPath}",
                requestPath,
                Request.Method,
                qrScanSucceeded,
                qrScanCreated,
                apkBlobPath);
            return BlobDownloadUnavailable();
        }

        if (!await _blobStorageService.ExistsAsync(apkBlobPath, HttpContext.RequestAborted))
        {
            _logger.LogWarning(
                "[QrScanDownloadApi] Blob APK not found after qr_scan attempt. path={Path}; method={Method}; qrScanSucceeded={QrScanSucceeded}; qrScanCreated={QrScanCreated}; streamFile=false; blobPath={BlobPath}",
                requestPath,
                Request.Method,
                qrScanSucceeded,
                qrScanCreated,
                apkBlobPath);
            return BlobDownloadUnavailable();
        }

        var apkUrl = _blobStorageService.GetPublicUrl(apkBlobPath);
        WriteApkDownloadHeaders(Response, _options.GetApkFileName());
        Response.Headers["X-VK-QR-Tracking"] = "api";
        Response.Headers["X-VK-QR-Scan-Succeeded"] = qrScanSucceeded ? "true" : "false";
        Response.Headers["X-VK-QR-Scan-Created"] = qrScanCreated ? "true" : "false";

        _logger.LogInformation(
            "[QrScanDownloadApi] Redirect ready. path={Path}; method={Method}; qrScanSucceeded={QrScanSucceeded}; qrScanCreated={QrScanCreated}; qrScanEventId={QrScanEventId}; streamFile=false; blobPath={BlobPath}; targetUrl={TargetUrl}; idempotencyKey={IdempotencyKey}",
            requestPath,
            Request.Method,
            qrScanSucceeded,
            qrScanCreated,
            string.IsNullOrWhiteSpace(qrScanEventId) ? "none" : qrScanEventId,
            apkBlobPath,
            apkUrl,
            idempotencyKey);

        return Redirect(apkUrl);
    }

    private static string BuildHtml(string appName, string trackedApkUrl, string displayApkUrl)
    {
        var safeAppName = EscapeHtml(appName);
        var safeTrackedApkUrl = EscapeHtml(trackedApkUrl);
        var safeDisplayApkUrl = EscapeHtml(displayApkUrl);

        return $$"""
<!DOCTYPE html>
<html lang="vi">
<head>
  <meta charset="utf-8">
  <meta name="viewport" content="width=device-width, initial-scale=1">
  <meta name="referrer" content="same-origin">
  <title>Tải ứng dụng {{safeAppName}}</title>
  <style>
    :root {
      color-scheme: light;
      --bg: #fffaf2;
      --card: #ffffff;
      --ink: #1f2937;
      --muted: #6b7280;
      --line: rgba(217, 119, 6, 0.16);
      --shadow: rgba(120, 53, 15, 0.12);
      --accent: #d97706;
      --accent-dark: #b45309;
    }

    * { box-sizing: border-box; }

    body {
      margin: 0;
      min-height: 100vh;
      display: grid;
      place-items: center;
      padding: 1rem;
      font-family: "Segoe UI", Arial, sans-serif;
      color: var(--ink);
      background:
        radial-gradient(circle at top, rgba(251, 191, 36, 0.22), transparent 28rem),
        linear-gradient(180deg, var(--bg), #ffffff);
    }

    main {
      width: min(460px, 100%);
      padding: 1.5rem;
      border-radius: 28px;
      background: var(--card);
      border: 1px solid var(--line);
      box-shadow: 0 24px 60px var(--shadow);
      text-align: center;
    }

    h1 {
      margin: 0 0 0.75rem;
      font-size: clamp(1.8rem, 4vw, 2.4rem);
      line-height: 1.2;
    }

    p {
      margin: 0;
      color: var(--muted);
      line-height: 1.6;
    }

    .actions {
      display: grid;
      gap: 0.9rem;
      margin-top: 1.5rem;
    }

    .download-button {
      display: inline-flex;
      align-items: center;
      justify-content: center;
      width: 100%;
      padding: 0.95rem 1rem;
      border-radius: 18px;
      background: linear-gradient(180deg, #f59e0b, var(--accent));
      color: #fff;
      font-size: 1rem;
      font-weight: 600;
      text-decoration: none;
      box-shadow: 0 18px 36px rgba(217, 119, 6, 0.25);
    }

    .download-button:hover {
      background: linear-gradient(180deg, #f59e0b, var(--accent-dark));
    }

    .link {
      color: var(--accent-dark);
      word-break: break-all;
    }
  </style>
</head>
<body>
  <main>
    <h1>Tải ứng dụng {{safeAppName}}</h1>
    <p>Trang này cung cấp gói APK trực tiếp cho Android. Nếu trình duyệt không tự tải, hãy dùng nút bên dưới.</p>
    <div class="actions">
      <a id="download-apk-button" class="download-button" href="{{safeTrackedApkUrl}}" rel="noopener">Tải APK</a>
      <a id="download-apk-link" class="link" href="{{safeTrackedApkUrl}}" rel="noopener">{{safeDisplayApkUrl}}</a>
    </div>
  </main>
  <script>
    (function () {
      var downloadStarted = false;
      var downloadButton = document.getElementById("download-apk-button");
      var downloadLink = document.getElementById("download-apk-link");
      [downloadButton, downloadLink].forEach(function (downloadElement) {
        if (!downloadElement) {
          return;
        }

        downloadElement.addEventListener("click", function () {
          downloadStarted = true;
        });
      });

      if (!/Android/i.test(navigator.userAgent)) {
        return;
      }

      window.setTimeout(function () {
        if (downloadStarted) {
          return;
        }

        downloadStarted = true;
        window.location.href = "{{safeTrackedApkUrl}}";
      }, 1200);
    }());
  </script>
</body>
</html>
""";
    }

    private static string EscapeHtml(string value)
        => WebUtility.HtmlEncode(value);

    private static void WriteApkDownloadHeaders(HttpResponse response, string? fileName)
    {
        response.Headers.CacheControl = "no-store, no-cache, must-revalidate, proxy-revalidate";
        response.Headers.Pragma = "no-cache";
        response.Headers.Expires = "0";
        response.Headers["X-Content-Type-Options"] = "nosniff";

        if (!string.IsNullOrWhiteSpace(fileName))
        {
            response.Headers["Content-Disposition"] = $"attachment; filename=\"{fileName}\"";
        }
    }

    private ContentResult BlobDownloadUnavailable()
    {
        Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
        return Content(
            BuildUnavailableHtml(_options.AppDisplayName),
            "text/html; charset=utf-8",
            Encoding.UTF8);
    }

    private static string BuildUnavailableHtml(string? appDisplayName)
    {
        var safeAppName = EscapeHtml(string.IsNullOrWhiteSpace(appDisplayName) ? "ung dung" : appDisplayName.Trim());
        return $$"""
<!DOCTYPE html>
<html lang="vi">
<head>
  <meta charset="utf-8">
  <meta name="viewport" content="width=device-width, initial-scale=1">
  <title>Chua san sang tai {{safeAppName}}</title>
  <style>
    body { margin: 0; min-height: 100vh; display: grid; place-items: center; padding: 1rem; font-family: "Segoe UI", Arial, sans-serif; color: #1f2937; background: #fffaf2; }
    main { width: min(460px, 100%); padding: 1.5rem; border: 1px solid rgba(217,119,6,.18); border-radius: 18px; background: #fff; box-shadow: 0 20px 48px rgba(120,53,15,.12); }
    h1 { margin: 0 0 .75rem; font-size: 1.6rem; }
    p { margin: 0; line-height: 1.6; color: #6b7280; }
  </style>
</head>
<body>
  <main>
    <h1>File tai ve chua san sang</h1>
    <p>He thong da ghi nhan luot quet ma QR, nhung file cai dat {{safeAppName}} hien chua co tren kho Blob Storage. Vui long thu lai sau.</p>
  </main>
</body>
</html>
""";
    }
}
