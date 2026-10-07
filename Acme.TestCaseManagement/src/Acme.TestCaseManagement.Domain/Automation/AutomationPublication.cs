using Volo.Abp;
using Volo.Abp.Domain.Entities.Auditing;
using Volo.Abp.MultiTenancy;

namespace Acme.TestCaseManagement.Automation;

/// <summary>
/// The memory of a publish request that carried an idempotency key. A pipeline that sends the same request again after a
/// timeout gets the stored answer instead of recording every result a second time.
/// </summary>
public class AutomationPublication : CreationAuditedEntity<Guid>, IMultiTenant
{
    public virtual Guid? TenantId { get; protected set; }

    public virtual string IdempotencyKey { get; protected set; }

    /// <summary>SHA-256 of the request, to tell a repeat of the request from another request that reuses the key.</summary>
    public virtual string RequestHash { get; protected set; }

    public virtual Guid? RunId { get; protected set; }

    /// <summary>The answer that was given, as JSON.</summary>
    public virtual string ResponseJson { get; protected set; }

    protected AutomationPublication()
    {
        IdempotencyKey = default!;
        RequestHash = default!;
        ResponseJson = default!;
    }

    public AutomationPublication(Guid id, Guid? tenantId, string idempotencyKey, string requestHash, Guid? runId, string responseJson)
        : base(id)
    {
        TenantId = tenantId;
        IdempotencyKey = Check.NotNullOrWhiteSpace(idempotencyKey, nameof(idempotencyKey), AutomationConsts.MaxIdempotencyKeyLength);
        RequestHash = Check.NotNullOrWhiteSpace(requestHash, nameof(requestHash), SignOffConsts.HashLength);
        RunId = runId;
        ResponseJson = Check.NotNullOrWhiteSpace(responseJson, nameof(responseJson));
    }
}
