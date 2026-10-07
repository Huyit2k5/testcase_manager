using Volo.Abp;
using Volo.Abp.Domain.Entities.Auditing;
using Volo.Abp.MultiTenancy;

namespace Acme.TestCaseManagement.Automation;

/// <summary>
/// The credential of a CI/CD pipeline. The secret is shown once, when the key is created; only its SHA-256 hash is
/// stored, so a leaked database does not leak working keys. A key is never deleted: it is revoked, which keeps the
/// record of what existed.
/// </summary>
public class ApiKey : CreationAuditedAggregateRoot<Guid>, IMultiTenant
{
    public virtual Guid? TenantId { get; protected set; }

    /// <summary>What the key is for, for example "GitHub Actions - main".</summary>
    public virtual string Name { get; protected set; }

    /// <summary>The start of the key ("tcm_" and 8 characters). Not secret: it identifies the key in lists and lookups.</summary>
    public virtual string KeyPrefix { get; protected set; }

    /// <summary>Lowercase hexadecimal SHA-256 of the whole key.</summary>
    public virtual string KeyHash { get; protected set; }

    public virtual DateTime? ExpiresAt { get; protected set; }

    public virtual DateTime? RevokedAt { get; protected set; }

    public virtual DateTime? LastUsedAt { get; protected set; }

    protected ApiKey()
    {
        Name = default!;
        KeyPrefix = default!;
        KeyHash = default!;
    }

    public ApiKey(Guid id, Guid? tenantId, string name, string keyPrefix, string keyHash, DateTime? expiresAt)
        : base(id)
    {
        TenantId = tenantId;
        Name = Check.NotNullOrWhiteSpace(name, nameof(name), ApiKeyConsts.MaxNameLength).Trim();
        KeyPrefix = Check.NotNullOrWhiteSpace(keyPrefix, nameof(keyPrefix), ApiKeyConsts.KeyPrefixLength);
        KeyHash = Check.NotNullOrWhiteSpace(keyHash, nameof(keyHash), SignOffConsts.HashLength);
        ExpiresAt = expiresAt;
    }

    /// <summary>A key works until it is revoked or expires.</summary>
    public virtual bool IsActive(DateTime now) => RevokedAt == null && (ExpiresAt == null || ExpiresAt > now);

    /// <summary>Revoking twice keeps the first time.</summary>
    public virtual void Revoke(DateTime now)
    {
        RevokedAt ??= now;
    }

    public virtual void MarkUsed(DateTime now)
    {
        LastUsedAt = now;
    }
}
