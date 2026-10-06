namespace Acme.TestCaseManagement.Authentication;

/// <summary>Settings of the access tokens that <see cref="AuthController"/> issues and the API accepts (section Auth:Jwt).</summary>
public class JwtOptions
{
    public const string Section = "Auth:Jwt";

    /// <summary>Shared secret for HS256, at least 32 characters. Never commit a real one.</summary>
    public string SigningKey { get; set; } = string.Empty;

    public string Issuer { get; set; } = "tcm-host";

    public string Audience { get; set; } = "tcm-api";

    public int LifetimeMinutes { get; set; } = 480;
}
