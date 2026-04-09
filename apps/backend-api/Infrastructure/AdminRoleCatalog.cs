namespace VinhKhanh.BackendApi.Infrastructure;

public static class AdminRoleCatalog
{
    public const string SuperAdmin = "SUPER_ADMIN";
    public const string PlaceOwner = "PLACE_OWNER";
    public const string AdminActorType = "ADMIN";
    public const string EndUserActorType = "END_USER";
    public const string AdminWebSource = "ADMIN_WEB";
    public const string MobileAppSource = "MOBILE_APP";

    public static bool IsSuperAdmin(string? role) =>
        string.Equals(role, SuperAdmin, StringComparison.OrdinalIgnoreCase);

    public static bool IsPlaceOwner(string? role) =>
        string.Equals(role, PlaceOwner, StringComparison.OrdinalIgnoreCase);

    public static bool IsAdminRole(string? role) =>
        IsSuperAdmin(role) || IsPlaceOwner(role);
}
