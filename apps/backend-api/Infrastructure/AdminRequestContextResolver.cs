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
        throw new ApiUnauthorizedException("Phien dang nhap admin khong hop le hoac da het han.");

    public AdminRequestContext RequireAdminRole(params string[] allowedRoles)
    {
        var admin = RequireAuthenticatedAdmin();
        if (allowedRoles is null || allowedRoles.Length == 0)
        {
            return admin;
        }

        if (allowedRoles.Any(role => AdminRoleCatalog.RoleEquals(admin.Role, role)))
        {
            return admin;
        }

        if (allowedRoles.Length == 1 && AdminRoleCatalog.IsSuperAdmin(allowedRoles[0]))
        {
            throw new ApiForbiddenException("Tai khoan hien tai khong co quyen quan tri toan he thong.");
        }

        throw new ApiForbiddenException("Tai khoan hien tai khong co quyen su dung API quan tri nay.");
    }

    public AdminRequestContext RequireSuperAdmin() =>
        RequireAdminRole(AdminRoleCatalog.SuperAdmin);

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
