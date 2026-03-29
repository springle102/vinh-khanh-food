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
            .Select(error => string.IsNullOrWhiteSpace(error.ErrorMessage) ? "Du lieu gui len khong hop le." : error.ErrorMessage)
            .Distinct()
            .ToArray();

        return messages.Length > 0
            ? string.Join("; ", messages)
            : "Du lieu gui len khong hop le.";
    }

    public static string GetDefaultMessage(int statusCode) => statusCode switch
    {
        StatusCodes.Status400BadRequest => "Du lieu gui len khong hop le.",
        StatusCodes.Status401Unauthorized => "Ban chua dang nhap hoac phien dang nhap da het han.",
        StatusCodes.Status403Forbidden => "Ban khong co quyen truy cap tai nguyen nay.",
        StatusCodes.Status404NotFound => "Khong tim thay tai nguyen yeu cau.",
        StatusCodes.Status405MethodNotAllowed => "Phuong thuc yeu cau khong duoc ho tro.",
        StatusCodes.Status415UnsupportedMediaType => "Dinh dang du lieu gui len khong duoc ho tro.",
        StatusCodes.Status422UnprocessableEntity => "Du lieu gui len khong hop le.",
        StatusCodes.Status500InternalServerError => "He thong gap loi trong qua trinh xu ly.",
        _ => "Yeu cau den backend that bai."
    };
}
