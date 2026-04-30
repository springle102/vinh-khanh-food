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
builder.Services.AddSingleton<StorageService>();
builder.Services.AddSingleton<GeneratedAudioStorageService>();
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
        }

        await next();
        return;
    }

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

    var apkFilePath = ResolveTrackedApkFilePath(webRoot, mobileDistributionOptions);
    if (!System.IO.File.Exists(apkFilePath))
    {
        app.Logger.LogWarning(
            "[QrScanDownloadMiddleware] APK file not found after qr_scan attempt. path={Path}; method={Method}; qrScanSucceeded={QrScanSucceeded}; qrScanCreated={QrScanCreated}; streamFile=false; apkFilePath={ApkFilePath}",
            requestPath,
            context.Request.Method,
            qrScanSucceeded,
            qrScanCreated,
            apkFilePath);
        context.Response.StatusCode = StatusCodes.Status404NotFound;
        context.Response.ContentType = "text/plain; charset=utf-8";
        await context.Response.WriteAsync("APK file not found.");
        return;
    }

    var apkFileInfo = new FileInfo(apkFilePath);
    WriteApkDownloadHeaders(context.Response, mobileDistributionOptions.GetApkFileName());
    context.Response.Headers["X-VK-QR-Tracking"] = "middleware";
    context.Response.Headers["X-VK-QR-Scan-Succeeded"] = qrScanSucceeded ? "true" : "false";
    context.Response.Headers["X-VK-QR-Scan-Created"] = qrScanCreated ? "true" : "false";
    app.Logger.LogInformation(
        "[QrScanDownloadMiddleware] APK response ready. path={Path}; method={Method}; qrScanSucceeded={QrScanSucceeded}; qrScanCreated={QrScanCreated}; qrScanEventId={QrScanEventId}; streamFile={StreamFile}; fileSizeBytes={FileSizeBytes}",
        requestPath,
        context.Request.Method,
        qrScanSucceeded,
        qrScanCreated,
        string.IsNullOrWhiteSpace(qrScanEventId) ? "none" : qrScanEventId,
        HttpMethods.IsGet(context.Request.Method),
        apkFileInfo.Length);

    if (HttpMethods.IsHead(context.Request.Method))
    {
        context.Response.StatusCode = StatusCodes.Status200OK;
        context.Response.ContentType = "application/vnd.android.package-archive";
        context.Response.ContentLength = apkFileInfo.Length;
        return;
    }

    await Results.File(
            apkFilePath,
            contentType: "application/vnd.android.package-archive",
            fileDownloadName: mobileDistributionOptions.GetApkFileName(),
            enableRangeProcessing: true)
        .ExecuteAsync(context);
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
            context.Context.Response.Headers.CacheControl = "public, max-age=604800, immutable";
            context.Context.Response.Headers.Remove("Pragma");
            context.Context.Response.Headers.Remove("Expires");
            return;
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

static string ResolveTrackedApkFilePath(string webRoot, MobileDistributionOptions options)
{
    var normalizedWebRoot = Path.GetFullPath(webRoot);
    string? firstCandidate = null;
    foreach (var relativeFilePath in options.GetApkRelativeFilePathCandidates())
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

    var apkFilePath = Path.GetFullPath(Path.Combine(normalizedWebRoot, options.GetApkRelativeFilePath()));
    EnsurePathInsideWebRoot(normalizedWebRoot, apkFilePath);
    return apkFilePath;
}

static void EnsurePathInsideWebRoot(string normalizedWebRoot, string apkFilePath)
{
    var webRootWithSeparator = normalizedWebRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) +
                               Path.DirectorySeparatorChar;

    if (!apkFilePath.StartsWith(webRootWithSeparator, StringComparison.OrdinalIgnoreCase))
    {
        throw new InvalidOperationException("Configured APK path must be inside wwwroot.");
    }
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

static string[] NormalizeConfiguredValues(IEnumerable<string> values)
{
    return values
        .Select(value => value?.Trim() ?? string.Empty)
        .Where(value => !string.IsNullOrWhiteSpace(value))
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .ToArray();
}
