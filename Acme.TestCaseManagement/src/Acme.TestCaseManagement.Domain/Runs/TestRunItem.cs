using Acme.TestCaseManagement.Enums;
using Volo.Abp.Domain.Entities;
using Volo.Abp.MultiTenancy;

namespace Acme.TestCaseManagement.Runs;

/// <summary>
/// A test case version scheduled in a run. <see cref="TestCaseVersionId"/> is set once and never changes, so the
/// item keeps pointing at the exact snapshot it was created with, whatever happens to the library test case.
/// </summary>
public class TestRunItem : Entity<Guid>, IMultiTenant
{
    public virtual Guid? TenantId { get; protected set; }

    public virtual Guid TestRunId { get; protected set; }

    /// <summary>Frozen snapshot reference (constitution principle III).</summary>
    public virtual Guid TestCaseVersionId { get; protected set; }

    /// <summary>One-based position within the run, used to keep a stable display order.</summary>
    public virtual int Sequence { get; protected set; }

    public virtual Guid? AssignedUserId { get; protected set; }

    /// <summary>Result of the latest attempt. The attempt log (<see cref="TestExecution"/>) is the source of truth.</summary>
    public virtual TestResultStatus CurrentStatus { get; protected set; }

    protected TestRunItem()
    {
    }

    internal TestRunItem(Guid id, Guid? tenantId, Guid testRunId, Guid testCaseVersionId, int sequence, Guid? assignedUserId)
        : base(id)
    {
        TenantId = tenantId;
        TestRunId = testRunId;
        TestCaseVersionId = testCaseVersionId;
        Sequence = sequence;
        AssignedUserId = assignedUserId;
        CurrentStatus = TestResultStatus.Untested;
    }

    public virtual void AssignTo(Guid? userId)
    {
        AssignedUserId = userId;
    }

    internal void SetCurrentStatus(TestResultStatus status)
    {
        CurrentStatus = status;
    }
}
