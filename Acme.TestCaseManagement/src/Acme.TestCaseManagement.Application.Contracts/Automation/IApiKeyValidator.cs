namespace Acme.TestCaseManagement.Automation;

/// <summary>Who an API key is, once it has been accepted.</summary>
public sealed record ApiKeyIdentity(Guid Id, Guid? TenantId, string Name);

/// <summary>
/// Checks the secret of an API key. Used by the authentication handler of the HTTP layer; not an application service,
/// so it has no route of its own.
/// </summary>
public interface IApiKeyValidator
{
    /// <summary>The identity of the key, or null when the secret is unknown, revoked or expired.</summary>
    Task<ApiKeyIdentity?> ValidateAsync(string secret);
}
