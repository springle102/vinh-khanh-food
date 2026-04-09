using Microsoft.AspNetCore.Http;

namespace VinhKhanh.BackendApi.Infrastructure;

public sealed class AdminRequestContextResolver(
    IHttpContextAccessor httpContextAccessor,
    AdminDataRepository repository)
{
    private const string ContextItemKey = "vk.admin-request-context";

    public AdminRequestContext? TryGetCurrentAdmin()
    {
        var httpContext = httpContextAccessor.HttpContext;
        if (httpContext is null)
        {
            return null;
        }

        if (httpContext.Items.TryGetValue(ContextItemKey, out var cached))
        {
            return cached as AdminRequestContext;
        }

        var accessToken = ReadBearerToken(httpContext.Request);
        if (string.IsNullOrWhiteSpace(accessToken))
        {
            httpContext.Items[ContextItemKey] = null!;
            return null;
        }

        var admin = repository.GetAdminRequestContext(accessToken);
        httpContext.Items[ContextItemKey] = admin!;
        return admin;
    }

    public AdminRequestContext RequireAuthenticatedAdmin() =>
        TryGetCurrentAdmin() ??
        throw new ApiUnauthorizedException("Phiên đăng nhập admin không hợp lệ hoặc đã hết hạn.");

    public AdminRequestContext RequireSuperAdmin()
    {
        var admin = RequireAuthenticatedAdmin();
        if (!admin.IsSuperAdmin)
        {
            throw new ApiForbiddenException("Tài khoản hiện tại không có quyền quản trị toàn hệ thống.");
        }

        return admin;
    }

    private static string? ReadBearerToken(HttpRequest request)
    {
        var authorizationHeader = request.Headers.Authorization.ToString();
        if (!authorizationHeader.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return authorizationHeader["Bearer ".Length..].Trim();
    }
}
