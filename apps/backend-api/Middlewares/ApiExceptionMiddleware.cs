using VinhKhanh.BackendApi.Contracts;

namespace VinhKhanh.BackendApi.Middlewares;

public sealed class ApiExceptionMiddleware(RequestDelegate next, ILogger<ApiExceptionMiddleware> logger)
{
    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await next(context);
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Unhandled exception while processing {Path}", context.Request.Path);

            context.Response.StatusCode = exception is InvalidOperationException
                ? StatusCodes.Status400BadRequest
                : StatusCodes.Status500InternalServerError;
            context.Response.ContentType = "application/json; charset=utf-8";

            await context.Response.WriteAsJsonAsync(ApiResponse<string>.Fail(exception.Message));
        }
    }
}
