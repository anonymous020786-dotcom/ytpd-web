namespace YtpdWeb.Api.Auth;

public class JwtOptions
{
    public const string SectionName = "Jwt";

    public string Secret { get; set; } = "";
    public string Issuer { get; set; } = "ytpd-web";
    public int ExpiryHours { get; set; } = 24 * 7;
}

public class AuthCredentialsOptions
{
    public const string SectionName = "Auth";

    public string Username { get; set; } = "";

    // PBKDF2 hash produced by PasswordHasher, not a plaintext password.
    public string PasswordHash { get; set; } = "";
}
