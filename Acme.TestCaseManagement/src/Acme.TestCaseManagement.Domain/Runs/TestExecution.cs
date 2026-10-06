using Acme.TestCaseManagement.Enums;
using Volo.Abp;
using Volo.Abp.Domain.Entities.Auditing;
using Volo.Abp.MultiTenancy;

namespace Acme.TestCaseManagement.Runs;

/// <summary>
/// One execution attempt of a run item. Append-only (constitution principle IV): the type has no mutators,
/// a re-test inserts a new attempt, and nothing in the module updates or deletes an existing one.
/// The executing user is <see cref="CreationAuditedEntity{TKey}.CreatorId"/>.
/// </summary>
public class TestExecution : CreationAuditedEntity<Guid>, IMultiTenant
{
    public virtual Guid? TenantId { get; protected set; }

    public virtual Guid TestRunItemId { get; protected set; }

    /// <summary>1, 2, 3, ... per run item.</summary>
    public virtual int AttemptNumber { get; protected set; }

    public virtual TestResultStatus Status { get; protected set; }

    public virtual string? ActualResult { get; protected set; }

    public virtual int DurationSeconds { get; protected set; }

    protected TestExecution()
    {
    }

    public TestExecution(
        Guid id,
        Guid? tenantId,
        Guid testRunItemId,
        int attemptNumber,
        TestResultStatus status,
        string? actualResult,
        int durationSeconds)
        : base(id)
    {
        if (status == TestResultStatus.Untested)
        {
            throw new BusinessException(TestCaseManagementErrorCodes.InvalidExecutionStatus);
        }

        TenantId = tenantId;
        TestRunItemId = testRunItemId;
        AttemptNumber = Check.Range(attemptNumber, nameof(attemptNumber), 1, int.MaxValue);
        Status = status;
        ActualResult = Check.Length(actualResult, nameof(actualResult), TestExecutionConsts.MaxActualResultLength);
        DurationSeconds = Check.Range(durationSeconds, nameof(durationSeconds), 0, int.MaxValue);
    }
}
