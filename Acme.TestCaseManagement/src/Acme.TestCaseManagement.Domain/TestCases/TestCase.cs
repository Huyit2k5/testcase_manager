using Acme.TestCaseManagement.Enums;
using Acme.TestCaseManagement.SharedSteps;
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

    public virtual ICollection<TestCaseTag> Tags { get; protected set; }

    protected TestCase()
    {
        Code = default!;
        Title = default!;
        Steps = new List<TestStep>();
        Tags = new List<TestCaseTag>();
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
        Tags = new List<TestCaseTag>();
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
                // A step of a shared group that is edited in the test case is the test case's own step from then on.
                if (step.SharedStepGroupId.HasValue && !step.ContentEquals(input.Action, input.ExpectedResult, input.TestData))
                {
                    step.Unlink();
                }

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

    /// <summary>
    /// Makes the tags equal to <paramref name="names"/>. Tags are cleaned, doubles (ignoring case) are dropped, and a tag that is
    /// kept stays the same row, so that nothing is deleted and inserted for a tag that did not change. Not part of any version.
    /// </summary>
    public virtual void SetTags(IEnumerable<string?>? names)
    {
        var wanted = TagNames.Prepare(names);
        var wantedKeys = wanted.Select(TagNames.Normalize).ToHashSet();

        foreach (var removed in Tags.Where(t => !wantedKeys.Contains(t.NormalizedName)).ToList())
        {
            Tags.Remove(removed);
        }

        var kept = Tags.Select(t => t.NormalizedName).ToHashSet();
        foreach (var name in wanted.Where(n => !kept.Contains(TagNames.Normalize(n))))
        {
            Tags.Add(new TestCaseTag(Guid.CreateVersion7(), Id, name));
        }
    }

    /// <summary>
    /// Puts a copy of the steps of <paramref name="group"/> into the test case, linked to the group at its current revision.
    /// <paramref name="position"/> is the one-based place of the first copied step; null or past the end means at the end.
    /// </summary>
    public virtual void InsertSharedSteps(SharedStepGroup group, int? position)
    {
        Check.NotNull(group, nameof(group));

        // A group is used once by a test case. A second copy could not be told from the first when the group is refreshed (which
        // replaces the steps of the group at one place), and would lose a block of steps there. To use it again, detach the first copy.
        if (Steps.Any(s => s.SharedStepGroupId == group.Id))
        {
            throw new BusinessException(TestCaseManagementErrorCodes.SharedStepGroupAlreadyUsed)
                .WithData("Code", Code)
                .WithData("Name", group.Name);
        }

        var ordered = Steps.OrderBy(s => s.StepOrder).ToList();
        var index = Math.Clamp((position ?? ordered.Count + 1) - 1, 0, ordered.Count);

        ordered.InsertRange(index, CopyOf(group));
        Renumber(ordered);
    }

    /// <summary>
    /// Replaces the steps that came from <paramref name="group"/> by its current steps, at the place of the first of them, and
    /// links them to the current revision. A test case that does not use the group is refused.
    /// </summary>
    public virtual void RefreshSharedSteps(SharedStepGroup group)
    {
        Check.NotNull(group, nameof(group));

        var ordered = Steps.OrderBy(s => s.StepOrder).ToList();
        var first = ordered.FindIndex(s => s.SharedStepGroupId == group.Id);
        if (first < 0)
        {
            throw new BusinessException(TestCaseManagementErrorCodes.SharedStepsNotLinked)
                .WithData("Code", Code)
                .WithData("Name", group.Name);
        }

        // The place is that of the first linked step, counted among the steps that stay.
        var index = ordered.Take(first).Count(s => s.SharedStepGroupId != group.Id);
        ordered.RemoveAll(s => s.SharedStepGroupId == group.Id);
        foreach (var removed in Steps.Where(s => s.SharedStepGroupId == group.Id).ToList())
        {
            Steps.Remove(removed);
        }

        ordered.InsertRange(index, CopyOf(group));
        Renumber(ordered);
    }

    /// <summary>Makes the steps that came from the group the test case's own. Their content does not change.</summary>
    public virtual void DetachSharedSteps(Guid groupId)
    {
        var linked = Steps.Where(s => s.SharedStepGroupId == groupId).ToList();
        if (linked.Count == 0)
        {
            throw new BusinessException(TestCaseManagementErrorCodes.SharedStepsNotLinked)
                .WithData("Code", Code)
                .WithData("Name", groupId);
        }

        foreach (var step in linked)
        {
            step.Unlink();
        }
    }

    /// <summary>The ids of the groups that some step of this test case was copied from.</summary>
    public virtual IReadOnlyCollection<Guid> SharedStepGroupIds() =>
        Steps.Where(s => s.SharedStepGroupId.HasValue).Select(s => s.SharedStepGroupId!.Value).Distinct().ToList();

    private List<TestStep> CopyOf(SharedStepGroup group)
    {
        return group.Steps
            .OrderBy(s => s.StepOrder)
            .Select(s =>
            {
                var copy = new TestStep(Guid.CreateVersion7(), Id, s.StepOrder, s.Action, s.ExpectedResult, s.TestData);
                copy.LinkTo(group.Id, group.Revision);
                return copy;
            })
            .ToList();
    }

    /// <summary>Gives the steps their positions in the order of the list, and makes the collection hold exactly these steps.</summary>
    private void Renumber(List<TestStep> ordered)
    {
        for (var i = 0; i < ordered.Count; i++)
        {
            ordered[i].SetOrder(i + 1);
        }

        foreach (var added in ordered.Where(s => !Steps.Contains(s)))
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
