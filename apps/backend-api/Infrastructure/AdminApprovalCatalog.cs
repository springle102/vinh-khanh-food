namespace VinhKhanh.BackendApi.Infrastructure;

public static class AdminApprovalCatalog
{
    public const string Pending = "pending";
    public const string Approved = "approved";
    public const string Rejected = "rejected";

    public static bool IsPending(string? value) =>
        string.Equals(NormalizeStatus(value), Pending, StringComparison.Ordinal);

    public static bool IsApproved(string? value) =>
        string.Equals(NormalizeStatus(value), Approved, StringComparison.Ordinal);

    public static bool IsRejected(string? value) =>
        string.Equals(NormalizeStatus(value), Rejected, StringComparison.Ordinal);

    public static string NormalizeKnownOrDefault(string? value, string defaultValue = Approved) =>
        NormalizeStatus(value) ?? defaultValue;

    public static string NormalizeRequired(string? value) =>
        NormalizeStatus(value) ??
        throw new ApiBadRequestException("Trang thai duyet khong hop le.");

    public static string? NormalizeStatus(string? value)
        => value?.Trim().ToLowerInvariant() switch
        {
            Pending => Pending,
            Approved => Approved,
            Rejected => Rejected,
            _ => null
        };
}
