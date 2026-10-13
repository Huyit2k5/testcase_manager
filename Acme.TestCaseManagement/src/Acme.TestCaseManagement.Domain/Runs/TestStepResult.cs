using Acme.TestCaseManagement.Enums;
using Volo.Abp;
using Volo.Abp.Domain.Entities;
using Volo.Abp.MultiTenancy;

namespace Acme.TestCaseManagement.Runs;

/// <summary>
/// The result of one step in one execution attempt. Append-only like the attempt it belongs to: it has no mutators, and a re-test
/// writes the results of its own attempt. The step is the one of the frozen version the run item executes
/// (<see cref="StepId"/> is the id the step has in <see cref="TestCases.TestCaseVersion.StepsJson"/>).
/// </summary>
public class TestStepResult : Entity<Guid>, IMultiTenant
{
    public virtual Guid? TenantId { get; protected set; }

    public virtual Guid TestExecutionId { get; protected set; }

    /// <summary>The id of the step inside the version snapshot.</summary>
    public virtual Guid StepId { get; protected set; }

    /// <summary>The position of the step in the version, kept so that the results read in order.</summary>
    public virtual int StepOrder { get; protected set; }

    public virtual TestResultStatus Status { get; protected set; }

    /// <summary>What was seen at this step, mostly for a step that did not pass.</summary>
    public virtual string? ActualResult { get; protected set; }

    protected TestStepResult()
    {
    }

    public TestStepResult(Guid id, Guid? tenantId, Guid testExecutionId, Guid stepId, int stepOrder, TestResultStatus status, string? actualResult)
        : base(id)
    {
        if (status == TestResultStatus.Untested)
        {
            throw new BusinessException(TestCaseManagementErrorCodes.InvalidExecutionStatus);
        }

        TenantId = tenantId;
        TestExecutionId = testExecutionId;
        StepId = stepId;
        StepOrder = Check.Range(stepOrder, nameof(stepOrder), 1, int.MaxValue);
        Status = status;
        ActualResult = Check.Length(actualResult, nameof(actualResult), TestExecutionConsts.MaxActualResultLength);
    }
}

/// <summary>A result of one step as it is given when an attempt is recorded.</summary>
public record StepResultInput(Guid StepId, TestResultStatus Status, string? ActualResult);
