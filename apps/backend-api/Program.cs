using System.Net;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Mvc;
using Microsoft.OpenApi.Models;
using VinhKhanh.BackendApi.Contracts;
using VinhKhanh.BackendApi.Infrastructure;
using VinhKhanh.BackendApi.Middlewares;

var builder = WebApplication.CreateBuilder(args);
var backendRoot = builder.Environment.ContentRootPath;
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

builder.Services.AddCors(options =>
{
    options.AddPolicy("AdminWeb", policy =>
    {
        var origins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];

        policy
            .WithOrigins(origins)
            .AllowAnyHeader()
            .AllowAnyMethod();
    });
});

var app = builder.Build();

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

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

if (!app.Environment.IsDevelopment())
{
    app.UseHttpsRedirection();
}
app.UseStaticFiles(new StaticFileOptions
{
    OnPrepareResponse = context =>
    {
        var path = context.Context.Request.Path.Value ?? string.Empty;
        if (path.StartsWith("/storage/audio/", StringComparison.OrdinalIgnoreCase))
        {
            context.Context.Response.Headers.CacheControl = "public, max-age=604800, immutable";
            context.Context.Response.Headers.Remove("Pragma");
            context.Context.Response.Headers.Remove("Expires");
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
