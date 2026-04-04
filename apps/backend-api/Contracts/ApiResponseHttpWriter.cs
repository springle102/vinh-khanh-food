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
            .Select(error => string.IsNullOrWhiteSpace(error.ErrorMessage) ? "Dữ liệu gửi lên không hợp lệ." : error.ErrorMessage)
            .Distinct()
            .ToArray();

        return messages.Length > 0
            ? string.Join("; ", messages)
            : "Dữ liệu gửi lên không hợp lệ.";
    }

    public static string GetDefaultMessage(int statusCode) => statusCode switch
    {
        StatusCodes.Status400BadRequest => "Dữ liệu gửi lên không hợp lệ.",
        StatusCodes.Status401Unauthorized => "Bạn chưa đăng nhập hoặc phiên đăng nhập đã hết hạn.",
        StatusCodes.Status403Forbidden => "Bạn không có quyền truy cập tài nguyên này.",
        StatusCodes.Status404NotFound => "Không tìm thấy tài nguyên yêu cầu.",
        StatusCodes.Status405MethodNotAllowed => "Phương thức yêu cầu không được hỗ trợ.",
        StatusCodes.Status415UnsupportedMediaType => "Định dạng dữ liệu gửi lên không được hỗ trợ.",
        StatusCodes.Status422UnprocessableEntity => "Dữ liệu gửi lên không hợp lệ.",
        StatusCodes.Status500InternalServerError => "Hệ thống gặp lỗi trong quá trình xử lý.",
        _ => "Yêu cầu đến backend thất bại."
    };
}
