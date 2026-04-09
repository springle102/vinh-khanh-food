namespace VinhKhanh.BackendApi.Infrastructure;

public sealed record AdminRequestContext(
    string UserId,
    string Name,
    string Email,
    string Role,
    string Status,
    string? ManagedPoiId)
{
    public bool IsSuperAdmin => AdminRoleCatalog.IsSuperAdmin(Role);

    public bool IsPlaceOwner => AdminRoleCatalog.IsPlaceOwner(Role);
}
