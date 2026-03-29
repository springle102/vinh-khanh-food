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

            var statusCode = exception is InvalidOperationException
                ? StatusCodes.Status400BadRequest
                : StatusCodes.Status500InternalServerError;
            var message = string.IsNullOrWhiteSpace(exception.Message)
                ? ApiResponseHttpWriter.GetDefaultMessage(statusCode)
                : exception.Message;

            await ApiResponseHttpWriter.WriteFailureAsync(context, statusCode, message);
        }
    }
}
