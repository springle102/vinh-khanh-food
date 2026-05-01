using System.Net;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.Extensions.Options;
using Microsoft.OpenApi.Models;
using VinhKhanh.BackendApi.Contracts;
using VinhKhanh.BackendApi.Infrastructure;
using VinhKhanh.BackendApi.Middlewares;

var builder = WebApplication.CreateBuilder(new WebApplicationOptions
{
    Args = args,
    ContentRootPath = Directory.GetCurrentDirectory(),
    WebRootPath = "wwwroot"
});
var backendRoot = builder.Environment.ContentRootPath;
var webRoot = builder.Environment.WebRootPath ?? Path.Combine(backendRoot, "wwwroot");
Directory.CreateDirectory(webRoot);
var repositoryRoot = Path.GetFullPath(Path.Combine(backendRoot, "..", ".."));

DotEnvBootstrapper.LoadIntoEnvironment(
    Path.Combine(repositoryRoot, ".env"),
    Path.Combine(repositoryRoot, ".env.local"),
    Path.Combine(backendRoot, ".env"),
    Path.Combine(backendRoot, ".env.local"));

builder.Logging.ClearProviders();
builder.Logging.AddConsole();
builder.Logging.AddDebug();

builder.Services.AddSingleton<AdminDataRepository>();
builder.Services.AddScoped<AdminRequestContextResolver>();
builder.Services.AddSingleton<IBlobStorageService, BlobStorageService>();
builder.Services.AddSingleton<StorageService>();
builder.Services.AddSingleton<GeneratedAudioStorageService>();
builder.Services.AddScoped<BlobBackfillService>();
builder.Services.AddSingleton<ResponseUrlNormalizer>();
builder.Services.AddScoped<BootstrapLocalizationService>();
builder.Services.AddHttpContextAccessor();
builder.Services.AddMemoryCache();
builder.Services.AddScoped<PoiNarrationService>();
builder.Services.AddScoped<PoiNarrationAudioService>();
builder.Services.AddScoped<PoiPregeneratedAudioService>();
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders =
        ForwardedHeaders.XForwardedFor |
        ForwardedHeaders.XForwardedHost |
        ForwardedHeaders.XForwardedProto;
    options.KnownIPNetworks.Clear();
    options.KnownProxies.Clear();
    options.KnownProxies.Add(IPAddress.Loopback);
    options.KnownProxies.Add(IPAddress.IPv6Loopback);
});
builder.Services.AddOptions<TextToSpeechOptions>()
    .Configure<IConfiguration>((options, configuration) => TextToSpeechOptions.ApplyConfiguration(options, configuration));
builder.Services.AddOptions<MobileDistributionOptions>()
    .Configure<IConfiguration>((options, configuration) => MobileDistributionOptions.ApplyConfiguration(options, configuration));
builder.Services.AddOptions<BlobStorageOptions>()
    .Configure<IConfiguration>((options, configuration) => BlobStorageOptions.ApplyConfiguration(options, configuration));
builder.Services.AddHttpClient<GeocodingProxyService>(client =>
{
    client.Timeout = TimeSpan.FromSeconds(8);
    client.DefaultRequestHeaders.UserAgent.ParseAdd("VinhKhanhAdmin/1.0");
    client.DefaultRequestHeaders.Accept.ParseAdd("application/json");
});
builder.Services.AddHttpClient<TranslationProxyService>(client =>
{
    client.Timeout = TimeSpan.FromSeconds(12);
    client.DefaultRequestHeaders.UserAgent.ParseAdd("VinhKhanhAdmin/1.0");
    client.DefaultRequestHeaders.Accept.ParseAdd("application/json");
});
builder.Services.AddScoped<ITextTranslationClient>(provider => provider.GetRequiredService<TranslationProxyService>());
builder.Services.AddScoped<RuntimeTranslationService>();
builder.Services.AddHttpClient<ITextToSpeechService, ElevenLabsTextToSpeechService>(client =>
{
    client.BaseAddress = new Uri("https://api.elevenlabs.io/");
    client.Timeout = TimeSpan.FromSeconds(30);
    client.DefaultRequestHeaders.UserAgent.ParseAdd("VinhKhanhAdmin/1.0");
    client.DefaultRequestHeaders.Accept.ParseAdd("audio/mpeg, audio/*;q=0.9, application/octet-stream;q=0.8");
});
builder.Services.AddHttpClient();
builder.Services
    .AddControllers()
    .ConfigureApiBehaviorOptions(options =>
    {
        options.InvalidModelStateResponseFactory = context =>
        {
            var response = ApiResponse<string>.Fail(ApiResponseHttpWriter.BuildValidationMessage(context));
            return new BadRequestObjectResult(response);
        };
    });
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "VinhKhanh Admin API",
        Version = "v1",
        Description = "Backend API cho web admin quản lý nội dung, người dùng, tệp và dữ liệu SQL Server."
    });
});

var configuredCorsOrigins = NormalizeConfiguredValues(
    builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? []);

builder.Services.AddCors(options =>
{
    options.AddPolicy("AdminWeb", policy =>
    {
        policy
            .WithOrigins(configuredCorsOrigins)
            .AllowAnyHeader()
            .AllowAnyMethod();
    });
});

var app = builder.Build();
var mobileDistributionOptions = app.Services.GetRequiredService<IOptions<MobileDistributionOptions>>().Value;
var blobStorageOptions = app.Services.GetRequiredService<IOptions<BlobStorageOptions>>().Value;
var trackedQrApkPath = mobileDistributionOptions.PublicDownloadApkPath;

app.UseForwardedHeaders();

app.Use(async (context, next) =>
{
    if (!context.Request.Path.StartsWithSegments("/api"))
    {
        await next();
        return;
    }

    var stopwatch = System.Diagnostics.Stopwatch.StartNew();
    try
    {
        await next();
    }
    finally
    {
        stopwatch.Stop();
        app.Logger.LogInformation(
            "API {Method} {Path} responded {StatusCode} in {ElapsedMs} ms",
            context.Request.Method,
            context.Request.Path,
            context.Response.StatusCode,
            stopwatch.Elapsed.TotalMilliseconds.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture));
    }
});
app.Use(async (context, next) =>
{
    try
    {
        await next();
    }
    catch (ApiRequestException exception)
    {
        app.Logger.LogWarning(
            exception,
            "Request rejected while processing {Method} {Path} with status {StatusCode}",
            context.Request.Method,
            context.Request.Path,
            exception.StatusCode);

        await ApiResponseHttpWriter.WriteFailureAsync(
            context,
            exception.StatusCode,
            string.IsNullOrWhiteSpace(exception.Message)
                ? ApiResponseHttpWriter.GetDefaultMessage(exception.StatusCode)
                : exception.Message);
    }
});
app.UseMiddleware<ApiExceptionMiddleware>();

app.UseSwagger(c =>
{
    c.RouteTemplate = "swagger/{documentName}/swagger.json";
});

app.UseSwaggerUI(c =>
{
    c.SwaggerEndpoint("/swagger/v1/swagger.json", "VinhKhanh Admin API v1");
    c.RoutePrefix = "swagger";
});

if (!app.Environment.IsDevelopment())
{
    app.UseHttpsRedirection();
}

app.Use(async (context, next) =>
{
    var requestPath = context.Request.Path.Value ?? string.Empty;
    if (requestPath.StartsWith("/downloads", StringComparison.OrdinalIgnoreCase))
    {
        app.Logger.LogInformation(
            "[QrScanDownloadMiddleware] Request entered. path={Path}; method={Method}; configuredApkPath={ConfiguredApkPath}; userAgent={UserAgent}; remoteIpAddress={RemoteIpAddress}",
            requestPath,
            context.Request.Method,
            trackedQrApkPath,
            context.Request.Headers.UserAgent.ToString(),
            context.Connection.RemoteIpAddress?.ToString() ?? "unknown");
    }

    if (!ShouldHandleTrackedApkDownload(context.Request, mobileDistributionOptions))
    {
        if (requestPath.StartsWith("/downloads", StringComparison.OrdinalIgnoreCase))
        {
            app.Logger.LogInformation(
                "[QrScanDownloadMiddleware] Request skipped. path={Path}; method={Method}; qrScanWritten=false; streamFile=false; reason=path-or-method-not-tracked",
                requestPath,
                context.Request.Method);
            context.Response.StatusCode = StatusCodes.Status404NotFound;
            return;
        }

        await next();
        return;
    }

    var blobStorageService = context.RequestServices.GetRequiredService<IBlobStorageService>();
    var metadata = PublicDownloadAnalytics.BuildQrScanMetadata(context.Request, requestPath);
    var idempotencyKey = PublicDownloadAnalytics.BuildQrScanIdempotencyKey(context.Request, requestPath);
    var qrScanSucceeded = false;
    var qrScanCreated = false;
    var qrScanEventId = string.Empty;

    if (HttpMethods.IsGet(context.Request.Method))
    {
        try
        {
            var adminDataRepository = context.RequestServices.GetRequiredService<AdminDataRepository>();
            var result = adminDataRepository.TrackQrScanWithResult(
                PublicDownloadAnalytics.QrScanSource,
                metadata,
                idempotencyKey);
            qrScanSucceeded = true;
            qrScanCreated = result.WasCreated;
            qrScanEventId = result.Event.Id;
            app.Logger.LogInformation(
                "[QrScanDownloadMiddleware] qr_scan write completed. path={Path}; method={Method}; qrScanCreated={QrScanCreated}; qrScanEventId={QrScanEventId}; idempotencyKey={IdempotencyKey}",
                requestPath,
                context.Request.Method,
                qrScanCreated,
                qrScanEventId,
                idempotencyKey);
        }
        catch (Exception exception)
        {
            app.Logger.LogError(
                exception,
                "[QrScanDownloadMiddleware] qr_scan write failed. path={Path}; method={Method}",
                requestPath,
                context.Request.Method);
        }
    }

    var apkBlobPath = blobStorageService.NormalizeBlobPath(
        blobStorageOptions.ApkFolder,
        mobileDistributionOptions.GetApkFileName());
    if (!blobStorageService.IsConfigured)
    {
        app.Logger.LogWarning(
            "[QrScanDownloadMiddleware] Blob storage is not configured for APK redirect. path={Path}; method={Method}; qrScanSucceeded={QrScanSucceeded}; qrScanCreated={QrScanCreated}; blobPath={BlobPath}",
            requestPath,
            context.Request.Method,
            qrScanSucceeded,
            qrScanCreated,
            apkBlobPath);
        await WriteBlobDownloadUnavailableAsync(context, mobileDistributionOptions.AppDisplayName);
        return;
    }

    if (!await blobStorageService.ExistsAsync(apkBlobPath, context.RequestAborted))
    {
        app.Logger.LogWarning(
            "[QrScanDownloadMiddleware] Blob APK not found after qr_scan attempt. path={Path}; method={Method}; qrScanSucceeded={QrScanSucceeded}; qrScanCreated={QrScanCreated}; streamFile=false; blobPath={BlobPath}",
            requestPath,
            context.Request.Method,
            qrScanSucceeded,
            qrScanCreated,
            apkBlobPath);
        await WriteBlobDownloadUnavailableAsync(context, mobileDistributionOptions.AppDisplayName);
        return;
    }

    var apkUrl = blobStorageService.GetPublicUrl(apkBlobPath);
    WriteApkDownloadHeaders(context.Response, mobileDistributionOptions.GetApkFileName());
    context.Response.Headers["X-VK-QR-Tracking"] = "middleware";
    context.Response.Headers["X-VK-QR-Scan-Succeeded"] = qrScanSucceeded ? "true" : "false";
    context.Response.Headers["X-VK-QR-Scan-Created"] = qrScanCreated ? "true" : "false";
    app.Logger.LogInformation(
        "[QrScanDownloadMiddleware] APK redirect ready. path={Path}; method={Method}; qrScanSucceeded={QrScanSucceeded}; qrScanCreated={QrScanCreated}; qrScanEventId={QrScanEventId}; streamFile=false; blobPath={BlobPath}; targetUrl={TargetUrl}",
        requestPath,
        context.Request.Method,
        qrScanSucceeded,
        qrScanCreated,
        string.IsNullOrWhiteSpace(qrScanEventId) ? "none" : qrScanEventId,
        apkBlobPath,
        apkUrl);

    context.Response.Redirect(apkUrl, permanent: false);
});

var staticFileContentTypeProvider = new FileExtensionContentTypeProvider();
staticFileContentTypeProvider.Mappings[".apk"] = "application/vnd.android.package-archive";
app.UseStaticFiles(new StaticFileOptions
{
    ContentTypeProvider = staticFileContentTypeProvider,
    OnPrepareResponse = context =>
    {
        var path = context.Context.Request.Path.Value ?? string.Empty;
        if (path.StartsWith("/storage/audio/", StringComparison.OrdinalIgnoreCase))
        {
            app.Logger.LogWarning(
                "[BlobMigration] Serving legacy local audio fallback through App Service. path={Path}",
                path);
            context.Context.Response.Headers.CacheControl = "public, max-age=604800, immutable";
            context.Context.Response.Headers.Remove("Pragma");
            context.Context.Response.Headers.Remove("Expires");
            return;
        }

        if (path.StartsWith("/storage/", StringComparison.OrdinalIgnoreCase))
        {
            app.Logger.LogWarning(
                "[BlobMigration] Serving legacy local media fallback through App Service. path={Path}",
                path);
        }

        if (string.Equals(Path.GetExtension(path), ".apk", StringComparison.OrdinalIgnoreCase))
        {
            var fileName = Path.GetFileName(path);
            WriteApkDownloadHeaders(context.Context.Response, fileName);
        }
    }
});
app.UseCors("AdminWeb");
app.Use(async (context, next) =>
{
    if (context.Request.Path.StartsWithSegments("/api"))
    {
        context.Response.Headers.CacheControl = "no-store, no-cache, must-revalidate, proxy-revalidate";
        context.Response.Headers.Pragma = "no-cache";
        context.Response.Headers.Expires = "0";
    }

    await next();
});
app.UseStatusCodePages(async statusCodeContext =>
{
    var context = statusCodeContext.HttpContext;
    if (!context.Request.Path.StartsWithSegments("/api"))
    {
        return;
    }

    if (context.Response.StatusCode < StatusCodes.Status400BadRequest || context.Response.HasStarted)
    {
        return;
    }

    var contentType = context.Response.ContentType;
    if (!string.IsNullOrWhiteSpace(contentType))
    {
        return;
    }

    await ApiResponseHttpWriter.WriteFailureAsync(
        context,
        context.Response.StatusCode,
        ApiResponseHttpWriter.GetDefaultMessage(context.Response.StatusCode));
});
app.MapControllers();

app.MapGet("/", () => Results.Ok(new
{
    service = "VinhKhanh Admin API",
    version = "v1",
    framework = ".NET 10 / ASP.NET Core",
    swagger = "/swagger"
}));

app.Run();

static bool ShouldHandleTrackedApkDownload(HttpRequest request, MobileDistributionOptions options)
{
    if (!HttpMethods.IsGet(request.Method) && !HttpMethods.IsHead(request.Method))
    {
        return false;
    }

    var requestPath = request.Path.Value ?? string.Empty;
    return options.IsDownloadApkPath(requestPath);
}

static void WriteApkDownloadHeaders(HttpResponse response, string? fileName)
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

static async Task WriteBlobDownloadUnavailableAsync(HttpContext context, string? appDisplayName)
{
    context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
    context.Response.ContentType = "text/html; charset=utf-8";
    context.Response.Headers.CacheControl = "no-store, no-cache, must-revalidate, proxy-revalidate";
    context.Response.Headers.Pragma = "no-cache";
    context.Response.Headers.Expires = "0";

    var safeAppName = WebUtility.HtmlEncode(
        string.IsNullOrWhiteSpace(appDisplayName) ? "ung dung" : appDisplayName.Trim());
    await context.Response.WriteAsync($$"""
<!DOCTYPE html>
<html lang="vi">
<head>
  <meta charset="utf-8">
  <meta name="viewport" content="width=device-width, initial-scale=1">
  <title>Chua san sang tai {{safeAppName}}</title>
  <style>
    body { margin: 0; min-height: 100vh; display: grid; place-items: center; padding: 1rem; font-family: Segoe UI, Arial, sans-serif; color: #1f2937; background: #fffaf2; }
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
""");
}

static string[] NormalizeConfiguredValues(IEnumerable<string> values)
{
    return values
        .Select(value => value?.Trim() ?? string.Empty)
        .Where(value => !string.IsNullOrWhiteSpace(value))
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .ToArray();
}
