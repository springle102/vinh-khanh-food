using VinhKhanh.BackendApi.Models;

namespace VinhKhanh.BackendApi.Infrastructure;

public static class AppAccessCatalog
{
    public const string GuestUser = "GuestUser";
    public const string RegisteredUser = "RegisteredUser";
    public const string PremiumUser = "PremiumUser";

    public static bool IsGuestUser(string? customerUserId)
        => string.IsNullOrWhiteSpace(customerUserId);

    public static bool HasPremiumSubscription(CustomerUser? customer)
        => customer?.IsPremium == true;

    public static string ResolveAppAccessType(CustomerUser? customer)
        => customer is null
            ? GuestUser
            : HasPremiumSubscription(customer)
                ? PremiumUser
                : RegisteredUser;
}
