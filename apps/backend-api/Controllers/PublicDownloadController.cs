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
    IWebHostEnvironment environment,
    AdminDataRepository repository,
    ILogger<PublicDownloadController> logger) : ControllerBase
{
    private readonly MobileDistributionOptions _options = mobileDistributionOptions.Value;
    private readonly IWebHostEnvironment _environment = environment;
    private readonly AdminDataRepository _repository = repository;
    private readonly ILogger<PublicDownloadController> _logger = logger;

    [HttpGet("/app")]
    [HttpGet("/download")]
    public IActionResult Index()
    {
        var appName = string.IsNullOrWhiteSpace(_options.AppDisplayName)
            ? "Ứng dụng"
            : _options.AppDisplayName.Trim();
        var trackedApkUrl = _options.GetDownloadApkUrl(Request);

        Response.Headers.CacheControl = "no-store, no-cache, must-revalidate, proxy-revalidate";
        Response.Headers.Pragma = "no-cache";
        Response.Headers.Expires = "0";
        Response.Headers["X-Content-Type-Options"] = "nosniff";

        return Content(BuildHtml(appName, trackedApkUrl, trackedApkUrl), "text/html; charset=utf-8", Encoding.UTF8);
    }

    [HttpGet(MobileDistributionOptions.PublicDownloadAppApiPath)]
    [HttpGet(MobileDistributionOptions.PublicDownloadAppApiAliasPath)]
    public IActionResult DownloadApp()
        => HandlePublicAppDownload(streamFile: true);

    [HttpHead(MobileDistributionOptions.PublicDownloadAppApiPath)]
    [HttpHead(MobileDistributionOptions.PublicDownloadAppApiAliasPath)]
    public IActionResult DownloadAppHead()
        => HandlePublicAppDownload(streamFile: false);

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

    private IActionResult HandlePublicAppDownload(bool streamFile)
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

        var apkFilePath = ResolveApkFilePath();
        if (!System.IO.File.Exists(apkFilePath))
        {
            _logger.LogWarning(
                "[QrScanDownloadApi] APK file not found after qr_scan attempt. path={Path}; method={Method}; qrScanSucceeded={QrScanSucceeded}; qrScanCreated={QrScanCreated}; streamFile=false; apkFilePath={ApkFilePath}",
                requestPath,
                Request.Method,
                qrScanSucceeded,
                qrScanCreated,
                apkFilePath);
            return NotFound("APK file not found.");
        }

        var apkFileInfo = new FileInfo(apkFilePath);
        WriteApkDownloadHeaders(Response, _options.GetApkFileName());
        Response.Headers["X-VK-QR-Tracking"] = "api";
        Response.Headers["X-VK-QR-Scan-Succeeded"] = qrScanSucceeded ? "true" : "false";
        Response.Headers["X-VK-QR-Scan-Created"] = qrScanCreated ? "true" : "false";

        _logger.LogInformation(
            "[QrScanDownloadApi] Response ready. path={Path}; method={Method}; qrScanSucceeded={QrScanSucceeded}; qrScanCreated={QrScanCreated}; qrScanEventId={QrScanEventId}; streamFile={StreamFile}; fileSizeBytes={FileSizeBytes}; idempotencyKey={IdempotencyKey}",
            requestPath,
            Request.Method,
            qrScanSucceeded,
            qrScanCreated,
            string.IsNullOrWhiteSpace(qrScanEventId) ? "none" : qrScanEventId,
            streamFile,
            apkFileInfo.Length,
            idempotencyKey);

        if (!streamFile)
        {
            Response.ContentType = "application/vnd.android.package-archive";
            Response.ContentLength = apkFileInfo.Length;
            return new EmptyResult();
        }

        return PhysicalFile(
            apkFilePath,
            "application/vnd.android.package-archive",
            _options.GetApkFileName(),
            enableRangeProcessing: true);
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

    private string ResolveApkFilePath()
    {
        var webRoot = _environment.WebRootPath ?? Path.Combine(_environment.ContentRootPath, "wwwroot");
        var normalizedWebRoot = Path.GetFullPath(webRoot);
        string? firstCandidate = null;
        foreach (var relativeFilePath in _options.GetApkRelativeFilePathCandidates())
        {
            var candidatePath = Path.GetFullPath(Path.Combine(normalizedWebRoot, relativeFilePath));
            EnsurePathInsideWebRoot(normalizedWebRoot, candidatePath);
            firstCandidate ??= candidatePath;

            if (System.IO.File.Exists(candidatePath))
            {
                return candidatePath;
            }
        }

        if (firstCandidate is not null)
        {
            return firstCandidate;
        }

        var apkFilePath = Path.GetFullPath(Path.Combine(normalizedWebRoot, _options.GetApkRelativeFilePath()));
        EnsurePathInsideWebRoot(normalizedWebRoot, apkFilePath);
        return apkFilePath;
    }

    private static void EnsurePathInsideWebRoot(string normalizedWebRoot, string apkFilePath)
    {
        var webRootWithSeparator = normalizedWebRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) +
                                   Path.DirectorySeparatorChar;

        if (!apkFilePath.StartsWith(webRootWithSeparator, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Configured APK path must be inside wwwroot.");
        }
    }

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
}
