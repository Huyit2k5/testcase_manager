using Acme.TestCaseManagement.TestCases;
using Volo.Abp;
using Volo.Abp.Domain.Entities.Auditing;
using Volo.Abp.MultiTenancy;

namespace Acme.TestCaseManagement.SharedSteps;

/// <summary>
/// A group of steps written once and put into many test cases ("Log in as a customer"). A test case that uses it holds a copy of
/// the steps and the <see cref="Revision"/> it copied; the copy is what is versioned and what runs execute, so changing the group
/// never changes a test case by itself. The revision goes up whenever the steps of the group change, which is how a test case
/// can tell that it is behind.
/// </summary>
public class SharedStepGroup : FullAuditedAggregateRoot<Guid>, IMultiTenant
{
    public virtual Guid? TenantId { get; protected set; }

    public virtual string Name { get; protected set; }

    public virtual string? Description { get; protected set; }

    /// <summary>1 when the group is created; goes up by one each time its steps change (a new name or description does not).</summary>
    public virtual int Revision { get; protected set; }

    public virtual ICollection<SharedStep> Steps { get; protected set; }

    protected SharedStepGroup()
    {
        Name = default!;
        Steps = new List<SharedStep>();
    }

    public SharedStepGroup(Guid id, Guid? tenantId, string name, string? description)
        : base(id)
    {
        TenantId = tenantId;
        Name = NormalizeName(name);
        Description = Check.Length(description, nameof(description), SharedStepGroupConsts.MaxDescriptionLength);
        Revision = 1;
        Steps = new List<SharedStep>();
    }

    public virtual void SetName(string name)
    {
        Name = NormalizeName(name);
    }

    public virtual void SetDescription(string? description)
    {
        Description = Check.Length(description, nameof(description), SharedStepGroupConsts.MaxDescriptionLength);
    }

    public static string NormalizeName(string name) =>
        Check.NotNullOrWhiteSpace(name, nameof(name), SharedStepGroupConsts.MaxNameLength).Trim();

    /// <summary>
    /// Makes the steps equal to <paramref name="inputs"/>, in that order (an Id keeps a step, no Id adds one, a step not listed goes).
    /// Returns whether anything changed; the revision goes up only then, and not for the first steps of a new group.
    /// </summary>
    public virtual bool SetSteps(IReadOnlyList<TestStepInput> inputs)
    {
        Check.NotNull(inputs, nameof(inputs));

        if (inputs.Count == 0)
        {
            throw new BusinessException(TestCaseManagementErrorCodes.SharedStepGroupHasNoSteps);
        }

        if (inputs.Count > SharedStepGroupConsts.MaxSteps)
        {
            throw new BusinessException(TestCaseManagementErrorCodes.SharedStepGroupTooLarge).WithData("Max", SharedStepGroupConsts.MaxSteps);
        }

        var current = Steps.OrderBy(s => s.StepOrder).ToList();
        var same = current.Count == inputs.Count
                   && current.Zip(inputs).All(pair => pair.First.ContentEquals(pair.Second.Action, pair.Second.ExpectedResult, pair.Second.TestData));
        if (same)
        {
            return false;
        }

        var existing = current.ToDictionary(s => s.Id);
        var seen = new HashSet<Guid>();
        var result = new List<SharedStep>(inputs.Count);

        for (var i = 0; i < inputs.Count; i++)
        {
            var input = inputs[i];
            if (input.Id.HasValue && existing.TryGetValue(input.Id.Value, out var step) && seen.Add(step.Id))
            {
                step.Update(input.Action, input.ExpectedResult, input.TestData);
                step.SetOrder(i + 1);
            }
            else
            {
                step = new SharedStep(Guid.CreateVersion7(), Id, i + 1, input.Action, input.ExpectedResult, input.TestData);
            }

            result.Add(step);
        }

        foreach (var removed in Steps.Where(s => !seen.Contains(s.Id)).ToList())
        {
            Steps.Remove(removed);
        }

        foreach (var added in result.Where(s => !Steps.Contains(s)))
        {
            Steps.Add(added);
        }

        if (current.Count > 0)
        {
            Revision += 1;
        }

        return true;
    }
}
