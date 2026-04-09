using Microsoft.AspNetCore.Http;

namespace VinhKhanh.BackendApi.Infrastructure;

public class ApiRequestException : Exception
{
    public ApiRequestException(int statusCode, string message)
        : base(message)
    {
        StatusCode = statusCode;
    }

    public int StatusCode { get; }
}

public sealed class ApiUnauthorizedException(string message)
    : ApiRequestException(StatusCodes.Status401Unauthorized, message);

public sealed class ApiForbiddenException(string message)
    : ApiRequestException(StatusCodes.Status403Forbidden, message);

public sealed class ApiNotFoundException(string message)
    : ApiRequestException(StatusCodes.Status404NotFound, message);
