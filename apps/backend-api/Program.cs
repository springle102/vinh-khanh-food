using Microsoft.AspNetCore.Mvc;
using Microsoft.OpenApi.Models;
using VinhKhanh.BackendApi.Contracts;
using VinhKhanh.BackendApi.Infrastructure;
using VinhKhanh.BackendApi.Middlewares;

var builder = WebApplication.CreateBuilder(args);

builder.Logging.ClearProviders();
builder.Logging.AddConsole();
builder.Logging.AddDebug();

builder.Services.AddSingleton<AdminDataRepository>();
builder.Services.AddSingleton<StorageService>();
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<PoiNarrationService>();
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
builder.Services.AddHttpClient<GoogleTranslateTtsProxyService>(client =>
{
    client.Timeout = TimeSpan.FromSeconds(20);
    client.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/124.0 Safari/537.36");
    client.DefaultRequestHeaders.Accept.ParseAdd("audio/mpeg, audio/*;q=0.9, */*;q=0.8");
    client.DefaultRequestHeaders.AcceptLanguage.ParseAdd("vi-VN,vi;q=0.9,en-US;q=0.8,en;q=0.7");
    client.DefaultRequestHeaders.Referrer = new Uri("https://translate.google.com/");
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
app.UseMiddleware<ApiExceptionMiddleware>();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();
app.UseStaticFiles();
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
