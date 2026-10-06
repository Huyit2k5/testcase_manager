using Acme.TestCaseManagement.Enums;
using Volo.Abp;
using Volo.Abp.Domain.Entities.Auditing;
using Volo.Abp.MultiTenancy;

namespace Acme.TestCaseManagement.TestCases;

/// <summary>
/// Master (design-time) test case. Holds no execution state: results live in the run aggregates
/// and reference an immutable <see cref="TestCaseVersion"/>.
/// </summary>
public class TestCase : FullAuditedAggregateRoot<Guid>, IMultiTenant
{
    public virtual Guid? TenantId { get; protected set; }

    public virtual Guid SuiteId { get; protected set; }

    /// <summary>Unique within a tenant, e.g. <c>TC-AUTH-001</c>.</summary>
    public virtual string Code { get; protected set; }

    public virtual string Title { get; protected set; }

    public virtual string? Description { get; protected set; }

    public virtual string? Preconditions { get; protected set; }

    public virtual string? Postconditions { get; protected set; }

    public virtual PriorityLevel Priority { get; protected set; }

    public virtual SeverityLevel Severity { get; protected set; }

    public virtual TestCaseStatus Status { get; protected set; }

    public virtual ExecutionType ExecutionType { get; protected set; }

    public virtual TestKind Kind { get; protected set; }

    public virtual TestLayer Layer { get; protected set; }

    public virtual string? AutomationId { get; protected set; }

    public virtual bool IsFlaky { get; protected set; }

    /// <summary>Number of the latest published <see cref="TestCaseVersion"/>; 0 until the first publish.</summary>
    public virtual int CurrentVersion { get; protected set; }

    public virtual ICollection<TestStep> Steps { get; protected set; }

    protected TestCase()
    {
        Code = default!;
        Title = default!;
        Steps = new List<TestStep>();
    }

    public TestCase(Guid id, Guid? tenantId, Guid suiteId, string code, string title)
        : base(id)
    {
        TenantId = tenantId;
        SuiteId = suiteId;
        Status = TestCaseStatus.Draft;
        Priority = PriorityLevel.Medium;
        Severity = SeverityLevel.Medium;
        ExecutionType = ExecutionType.Manual;
        Kind = TestKind.Functional;
        Layer = TestLayer.Acceptance;
        Steps = new List<TestStep>();
        Code = NormalizeCode(code);
        Title = NormalizeTitle(title);
    }

    public virtual void SetCode(string code)
    {
        Code = NormalizeCode(code);
    }

    public virtual void SetTitle(string title)
    {
        Title = NormalizeTitle(title);
    }

    private static string NormalizeCode(string code)
    {
        return Check.NotNullOrWhiteSpace(code, nameof(code), TestCaseConsts.MaxCodeLength).Trim();
    }

    private static string NormalizeTitle(string title)
    {
        return Check.NotNullOrWhiteSpace(title, nameof(title), TestCaseConsts.MaxTitleLength).Trim();
    }

    public virtual void SetSuite(Guid suiteId)
    {
        SuiteId = suiteId;
    }

    public virtual void SetDetails(
        string? description,
        string? preconditions,
        string? postconditions,
        PriorityLevel priority,
        SeverityLevel severity,
        ExecutionType executionType,
        TestKind kind,
        TestLayer layer,
        string? automationId)
    {
        Description = Check.Length(description, nameof(description), TestCaseConsts.MaxTextLength);
        Preconditions = Check.Length(preconditions, nameof(preconditions), TestCaseConsts.MaxTextLength);
        Postconditions = Check.Length(postconditions, nameof(postconditions), TestCaseConsts.MaxTextLength);
        AutomationId = Check.Length(automationId, nameof(automationId), TestCaseConsts.MaxAutomationIdLength);
        Priority = priority;
        Severity = severity;
        ExecutionType = executionType;
        Kind = kind;
        Layer = layer;
    }

    public virtual void SetFlaky(bool isFlaky)
    {
        IsFlaky = isFlaky;
    }

    /// <summary>
    /// Makes the step list equal to <paramref name="inputs"/>, in that order. Inputs with a known Id update the
    /// existing step, inputs without one are added, and steps that are not listed are removed.
    /// </summary>
    public virtual void SetSteps(IReadOnlyList<TestStepInput> inputs)
    {
        Check.NotNull(inputs, nameof(inputs));

        var existing = Steps.ToDictionary(s => s.Id);
        var seen = new HashSet<Guid>();
        var result = new List<TestStep>(inputs.Count);

        for (var i = 0; i < inputs.Count; i++)
        {
            var input = inputs[i];
            var order = i + 1;

            if (input.Id.HasValue && existing.TryGetValue(input.Id.Value, out var step) && seen.Add(step.Id))
            {
                step.Update(input.Action, input.ExpectedResult, input.TestData);
                step.SetOrder(order);
            }
            else
            {
                step = new TestStep(Guid.CreateVersion7(), Id, order, input.Action, input.ExpectedResult, input.TestData);
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
    }

    /// <summary>Reorders the steps. <paramref name="orderedStepIds"/> must list every current step exactly once.</summary>
    public virtual void ReorderSteps(IReadOnlyList<Guid> orderedStepIds)
    {
        Check.NotNull(orderedStepIds, nameof(orderedStepIds));

        var current = Steps.ToDictionary(s => s.Id);
        if (orderedStepIds.Count != current.Count ||
            orderedStepIds.Distinct().Count() != orderedStepIds.Count ||
            orderedStepIds.Any(id => !current.ContainsKey(id)))
        {
            throw new BusinessException(TestCaseManagementErrorCodes.InvalidStepOrder);
        }

        for (var i = 0; i < orderedStepIds.Count; i++)
        {
            current[orderedStepIds[i]].SetOrder(i + 1);
        }
    }

    /// <summary>Status changes go through <see cref="TestCaseManager.ChangeStatusAsync"/>, which validates the transition.</summary>
    internal void SetStatus(TestCaseStatus status)
    {
        Status = status;
    }

    internal int IncrementVersion()
    {
        CurrentVersion += 1;
        return CurrentVersion;
    }
}
