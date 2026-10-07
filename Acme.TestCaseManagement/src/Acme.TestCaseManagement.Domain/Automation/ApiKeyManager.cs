using System.Security.Cryptography;
using System.Text;
using Volo.Abp;
using Volo.Abp.Data;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Domain.Services;
using Volo.Abp.MultiTenancy;

namespace Acme.TestCaseManagement.Automation;

/// <summary>
/// Makes and checks API keys. A key looks like <c>tcm_3fa9c2d1_&lt;43 characters&gt;</c>: a marker, 8 random hexadecimal
/// characters that identify the key, and 256 random bits as the secret. The secret is random, not chosen by a person, so a
/// plain SHA-256 is enough to store it (a slow password hash would only make every request slower).
/// </summary>
public class ApiKeyManager : DomainService
{
    public const string Marker = "tcm_";

    private readonly IRepository<ApiKey, Guid> _repository;
    private readonly IDataFilter _dataFilter;

    public ApiKeyManager(IRepository<ApiKey, Guid> repository, IDataFilter dataFilter)
    {
        _repository = repository;
        _dataFilter = dataFilter;
    }

    /// <summary>The current time as UTC, which is how every time of an API key is stored and compared.</summary>
    public virtual DateTime UtcNow => Clock.Now.ToUniversalTime();

    /// <summary>A time as UTC. A time without a kind is taken to be UTC already, as a client that sends one means it.</summary>
    public static DateTime ToUtc(DateTime value)
    {
        return value.Kind == DateTimeKind.Unspecified ? DateTime.SpecifyKind(value, DateTimeKind.Utc) : value.ToUniversalTime();
    }

    /// <summary>Builds a new key and its secret. The caller inserts the key and shows the secret once.</summary>
    public virtual (ApiKey Key, string Secret) Create(string name, DateTime? expiresAt)
    {
        if (expiresAt.HasValue)
        {
            expiresAt = ToUtc(expiresAt.Value);
            if (expiresAt.Value <= UtcNow)
            {
                throw new BusinessException(TestCaseManagementErrorCodes.InvalidApiKeyExpiry);
            }
        }

        var identifier = Convert.ToHexString(RandomNumberGenerator.GetBytes(4)).ToLowerInvariant();
        var secret = $"{Marker}{identifier}_{Base64Url(RandomNumberGenerator.GetBytes(32))}";

        var key = new ApiKey(GuidGenerator.Create(), CurrentTenant.Id, name, Marker + identifier, Hash(secret), expiresAt);
        return (key, secret);
    }

    /// <summary>Lowercase hexadecimal SHA-256 of a key.</summary>
    public static string Hash(string secret)
    {
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(secret))).ToLowerInvariant();
    }

    /// <summary>
    /// The key that <paramref name="secret"/> belongs to, if it is a working one (neither revoked nor expired); otherwise
    /// null. Looks across tenants, because the caller does not know its tenant yet: the key tells it. The comparison takes
    /// the same time whatever the secret, so that response times reveal nothing about how much of a guess was right.
    /// </summary>
    public virtual async Task<ApiKey?> FindActiveAsync(string? secret)
    {
        if (!TryGetPrefix(secret, out var prefix))
        {
            return null;
        }

        List<ApiKey> candidates;
        using (_dataFilter.Disable<IMultiTenant>())
        {
            candidates = await _repository.GetListAsync(x => x.KeyPrefix == prefix);
        }

        var hash = Encoding.ASCII.GetBytes(Hash(secret!));
        ApiKey? match = null;
        foreach (var candidate in candidates)
        {
            // Every candidate is compared, so the loop does not stop early on a match.
            if (CryptographicOperations.FixedTimeEquals(hash, Encoding.ASCII.GetBytes(candidate.KeyHash)))
            {
                match = candidate;
            }
        }

        return match != null && match.IsActive(UtcNow) ? match : null;
    }

    /// <summary>Whether the text has the shape of a key, and its identifying prefix.</summary>
    public static bool TryGetPrefix(string? secret, out string prefix)
    {
        prefix = string.Empty;
        if (string.IsNullOrWhiteSpace(secret) || secret.Length < Marker.Length + 8 + 1 + 20 || !secret.StartsWith(Marker, StringComparison.Ordinal))
        {
            return false;
        }

        if (secret[Marker.Length + 8] != '_')
        {
            return false;
        }

        prefix = secret[..(Marker.Length + 8)];
        return true;
    }

    private static string Base64Url(byte[] bytes)
    {
        return Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }
}
