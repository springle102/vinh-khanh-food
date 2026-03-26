namespace VinhKhanh.BackendApi.Authentication;

public sealed class JwtOptions
{
    public const string SectionName = "Jwt";

    public string Issuer { get; set; } = "VinhKhanhFoodGuide";

    public string Audience { get; set; } = "VinhKhanhFoodGuide.MobileAndAdmin";

    public string SigningKey { get; set; } = "VinhKhanhFoodGuide-demo-signing-key-2026";

    public int AccessTokenMinutes { get; set; } = 120;

    public int RefreshTokenDays { get; set; } = 14;

    public byte[] GetSigningKeyBytes() => System.Text.Encoding.UTF8.GetBytes(SigningKey);
}
