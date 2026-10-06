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

    internal void Restore()
    {
        IsDeleted = false;
        DeleterId = null;
        DeletionTime = null;
    }
}
