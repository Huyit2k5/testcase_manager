using Volo.Abp.Domain.Entities;
using Volo.Abp.Domain.Entities.Auditing;
using Volo.Abp.MultiTenancy;

namespace Acme.TestCaseManagement.Requirements;

/// <summary>
/// N-N link between a requirement and a test case, identified by the pair of ids. It is soft-deleted when
/// unlinked and restored when linked again, so the audit trail of traceability changes is kept.
/// </summary>
public class RequirementTestCase : FullAuditedEntity, IMultiTenant
{
    public virtual Guid? TenantId { get; protected set; }

    public virtual Guid RequirementId { get; protected set; }

    public virtual Guid TestCaseId { get; protected set; }

    /// <summary>
    /// True from the moment the requirement changed (its title, description or acceptance criteria) until someone confirms that the
    /// test case still matches it. It does not change the coverage or the result: it tells that the test case is to be looked at.
    /// </summary>
    public virtual bool IsSuspect { get; protected set; }

    /// <summary>When the link became suspect: the first change that nobody has looked at yet.</summary>
    public virtual DateTime? SuspectSince { get; protected set; }

    /// <summary>The fields of the requirement that changed since, as names ("Title, AcceptanceCriteria").</summary>
    public virtual string? SuspectReason { get; protected set; }

    protected RequirementTestCase()
    {
    }

    internal RequirementTestCase(Guid? tenantId, Guid requirementId, Guid testCaseId)
    {
        TenantId = tenantId;
        RequirementId = requirementId;
        TestCaseId = testCaseId;
    }

    public override object?[] GetKeys()
    {
        return new object?[] { RequirementId, TestCaseId };
    }

    /// <summary>
    /// Marks the link as to be checked. A link that is already suspect keeps the time of the first change and gathers the fields of
    /// every change since.
    /// </summary>
    internal void MarkSuspect(DateTime now, IReadOnlyCollection<string> changedFields)
    {
        var fields = (SuspectReason ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Concat(changedFields)
            .Distinct(StringComparer.Ordinal)
            .ToList();

        SuspectSince = IsSuspect ? SuspectSince : now;
        IsSuspect = true;
        SuspectReason = string.Join(", ", fields);
    }

    internal void ClearSuspect()
    {
        IsSuspect = false;
        SuspectSince = null;
        SuspectReason = null;
    }

    internal void Restore()
    {
        IsDeleted = false;
        DeleterId = null;
        DeletionTime = null;
        // Linking again is a decision to link it now, so whatever was open before is gone.
        ClearSuspect();
    }
}
