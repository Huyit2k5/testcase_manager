using Acme.TestCaseManagement.Enums;
using Volo.Abp;
using Volo.Abp.Domain.Entities.Auditing;
using Volo.Abp.MultiTenancy;

namespace Acme.TestCaseManagement.Runs;

/// <summary>A testing session on one environment, optionally part of a <see cref="Plans.TestPlan"/>.</summary>
public class TestRun : FullAuditedAggregateRoot<Guid>, IMultiTenant
{
    public virtual Guid? TenantId { get; protected set; }

    public virtual Guid? TestPlanId { get; protected set; }

    public virtual string Title { get; protected set; }

    /// <summary>For example "Staging", "Production" or "iOS 17".</summary>
    public virtual string Environment { get; protected set; }

    public virtual Guid? AssignedToUserId { get; protected set; }

    public virtual RunStatus Status { get; protected set; }

    public virtual ICollection<TestRunItem> Items { get; protected set; }

    /// <summary>Share of items that have at least one attempt, 0 to 100, rounded to two decimals. Not persisted.</summary>
    public virtual double CompletionPercentage =>
        Items.Count == 0
            ? 0
            : Math.Round(Items.Count(i => i.CurrentStatus != TestResultStatus.Untested) * 100.0 / Items.Count, 2);

    protected TestRun()
    {
        Title = default!;
        Environment = default!;
        Items = new List<TestRunItem>();
    }

    public TestRun(Guid id, Guid? tenantId, string title, string environment, Guid? testPlanId = null, Guid? assignedToUserId = null)
        : base(id)
    {
        TenantId = tenantId;
        TestPlanId = testPlanId;
        AssignedToUserId = assignedToUserId;
        Status = RunStatus.Planned;
        Items = new List<TestRunItem>();
        Title = NormalizeTitle(title);
        Environment = NormalizeEnvironment(environment);
    }

    public virtual void SetTitle(string title)
    {
        Title = NormalizeTitle(title);
    }

    public virtual void SetEnvironment(string environment)
    {
        Environment = NormalizeEnvironment(environment);
    }

    public virtual void SetAssignedTo(Guid? userId)
    {
        AssignedToUserId = userId;
    }

    /// <summary>Schedules a test case version in this run. The version id is bound permanently.</summary>
    public virtual TestRunItem AddItem(Guid testCaseVersionId, Guid? assignedUserId)
    {
        EnsureNotCompleted();

        if (Items.Any(i => i.TestCaseVersionId == testCaseVersionId))
        {
            throw new BusinessException(TestCaseManagementErrorCodes.DuplicateTestRunItem)
                .WithData("VersionId", testCaseVersionId);
        }

        var item = new TestRunItem(
            Guid.CreateVersion7(), TenantId, Id, testCaseVersionId, Items.Count + 1, assignedUserId);
        Items.Add(item);
        return item;
    }

    public virtual TestRunItem? FindItem(Guid itemId)
    {
        return Items.FirstOrDefault(i => i.Id == itemId);
    }

    /// <summary>Closes the run. A completed run accepts no more items or attempts.</summary>
    public virtual void Complete()
    {
        EnsureNotCompleted();
        Status = RunStatus.Completed;
    }

    internal void EnsureNotCompleted()
    {
        if (Status == RunStatus.Completed)
        {
            throw new BusinessException(TestCaseManagementErrorCodes.TestRunAlreadyCompleted)
                .WithData("Title", Title);
        }
    }

    internal void MarkInProgress()
    {
        if (Status == RunStatus.Planned)
        {
            Status = RunStatus.InProgress;
        }
    }

    private static string NormalizeTitle(string title)
    {
        return Check.NotNullOrWhiteSpace(title, nameof(title), TestRunConsts.MaxTitleLength).Trim();
    }

    private static string NormalizeEnvironment(string environment)
    {
        return Check.NotNullOrWhiteSpace(environment, nameof(environment), TestRunConsts.MaxEnvironmentLength).Trim();
    }
}
