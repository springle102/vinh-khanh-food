using Microsoft.AspNetCore.Mvc;

namespace VinhKhanh.BackendApi.Contracts;

public static class ApiResponseHttpWriter
{
    public static Task WriteFailureAsync(HttpContext context, int statusCode, string message)
    {
        context.Response.StatusCode = statusCode;
        context.Response.ContentType = "application/json; charset=utf-8";
        return context.Response.WriteAsJsonAsync(ApiResponse<string>.Fail(message));
    }

    public static string BuildValidationMessage(ActionContext context)
    {
        var messages = context.ModelState
            .Values
            .SelectMany(entry => entry.Errors)
            .Select(error => string.IsNullOrWhiteSpace(error.ErrorMessage)
                ? "The submitted data is invalid."
                : error.ErrorMessage)
            .Distinct()
            .ToArray();

        return messages.Length > 0
            ? string.Join("; ", messages)
            : "The submitted data is invalid.";
    }

    public static string GetDefaultMessage(int statusCode) => statusCode switch
    {
        StatusCodes.Status400BadRequest => "The submitted data is invalid.",
        StatusCodes.Status401Unauthorized => "Please sign in again.",
        StatusCodes.Status403Forbidden => "You do not have access to this resource.",
        StatusCodes.Status404NotFound => "The requested resource was not found.",
        StatusCodes.Status405MethodNotAllowed => "The request method is not supported.",
        StatusCodes.Status415UnsupportedMediaType => "The submitted data format is not supported.",
        StatusCodes.Status422UnprocessableEntity => "The submitted data is invalid.",
        StatusCodes.Status500InternalServerError => "The system hit an error while processing the request.",
        _ => "The backend request failed."
    };
}
