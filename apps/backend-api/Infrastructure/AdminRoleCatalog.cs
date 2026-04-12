namespace VinhKhanh.BackendApi.Infrastructure;

public static class AdminRoleCatalog
{
    public const string SuperAdmin = "SUPER_ADMIN";
    public const string PlaceOwner = "PLACE_OWNER";
    public const string SuperAdminModelName = "SuperAdmin";
    public const string PlaceOwnerModelName = "PlaceOwner";
    public const string AdminActorType = "ADMIN";
    public const string EndUserActorType = "END_USER";
    public const string AdminWebSource = "ADMIN_WEB";
    public const string MobileAppSource = "MOBILE_APP";

    public static IReadOnlyList<string> AdminRoles => [SuperAdmin, PlaceOwner];

    public static bool IsSuperAdmin(string? role) =>
        RoleEquals(role, SuperAdmin);

    public static bool IsPlaceOwner(string? role) =>
        RoleEquals(role, PlaceOwner);

    public static bool IsAdminRole(string? role) =>
        IsSuperAdmin(role) || IsPlaceOwner(role);

    public static bool RoleEquals(string? left, string? right) =>
        string.Equals(NormalizeRole(left), NormalizeRole(right), StringComparison.Ordinal);

    public static string? NormalizeRole(string? role)
    {
        var compactRole = CompactRoleValue(role);
        return compactRole switch
        {
            "SUPERADMIN" => SuperAdmin,
            "PLACEOWNER" => PlaceOwner,
            _ => null
        };
    }

    public static string NormalizeKnownRoleOrOriginal(string? role)
        => NormalizeRole(role) ?? role?.Trim() ?? string.Empty;

    public static string NormalizeRequiredRole(string? role)
        => NormalizeRole(role) ??
           throw new ApiBadRequestException(
               $"Role admin khong hop le. He thong chi ho tro {SuperAdminModelName} va {PlaceOwnerModelName}.");

    public static string ToModelRoleName(string? role)
        => NormalizeRole(role) switch
        {
            SuperAdmin => SuperAdminModelName,
            PlaceOwner => PlaceOwnerModelName,
            _ => role?.Trim() ?? string.Empty
        };

    private static string CompactRoleValue(string? role)
    {
        if (string.IsNullOrWhiteSpace(role))
        {
            return string.Empty;
        }

        var builder = new System.Text.StringBuilder(role.Length);
        foreach (var character in role.Trim())
        {
            if (!char.IsWhiteSpace(character) && character is not '_' and not '-')
            {
                builder.Append(char.ToUpperInvariant(character));
            }
        }

        return builder.ToString();
    }
}
