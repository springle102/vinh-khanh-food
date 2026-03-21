using VinhKhanh.BackendApi.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSingleton<AdminDataRepository>();
builder.Services.AddSingleton<StorageService>();
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
