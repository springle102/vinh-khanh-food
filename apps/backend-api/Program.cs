using Microsoft.AspNetCore.Diagnostics;
using VinhKhanh.BackendApi.Contracts;
using VinhKhanh.BackendApi.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

builder.Logging.ClearProviders();
builder.Logging.AddConsole();
builder.Logging.AddDebug();

builder.Services.AddSingleton<AdminDataRepository>();
builder.Services.AddSingleton<StorageService>();
builder.Services.AddHttpClient<GeocodingProxyService>(client =>
{
    client.Timeout = TimeSpan.FromSeconds(8);
    client.DefaultRequestHeaders.UserAgent.ParseAdd("VinhKhanhAdmin/1.0");
    client.DefaultRequestHeaders.Accept.ParseAdd("application/json");
});
builder.Services.AddControllers();

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

app.UseExceptionHandler(errorApp =>
{
    errorApp.Run(async context =>
    {
        var exception = context.Features.Get<IExceptionHandlerFeature>()?.Error;
        var message = exception?.Message ?? "Đã xảy ra lỗi không xác định.";

        context.Response.StatusCode = exception is InvalidOperationException
            ? StatusCodes.Status503ServiceUnavailable
            : StatusCodes.Status500InternalServerError;
        context.Response.ContentType = "application/json; charset=utf-8";

        await context.Response.WriteAsJsonAsync(ApiResponse<string>.Fail(message));
    });
});

app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseCors("AdminWeb");
app.MapControllers();

app.MapGet("/", () => Results.Ok(new
{
    service = "Vinh Khanh Backend API",
    version = "v1",
    framework = ".NET 10 / ASP.NET Core"
}));

app.Run();
