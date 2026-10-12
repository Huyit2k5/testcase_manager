using Acme.TestCaseManagement.Enums;
using Volo.Abp;
using Volo.Abp.Domain.Entities.Auditing;
using Volo.Abp.MultiTenancy;

namespace Acme.TestCaseManagement.TestCases;

/// <summary>
/// Immutable snapshot of a test case. Test run items bind to a version, never to the live test case,
/// so later library edits cannot change historical results. There are intentionally no mutators.
/// </summary>
public class TestCaseVersion : CreationAuditedEntity<Guid>, IMultiTenant
{
    public virtual Guid? TenantId { get; protected set; }

    public virtual Guid TestCaseId { get; protected set; }

    public virtual int VersionNumber { get; protected set; }

    public virtual string Title { get; protected set; }

    public virtual string? Preconditions { get; protected set; }

    /// <summary>JSON array of <see cref="TestStepSnapshot"/>, ordered by step order.</summary>
    public virtual string StepsJson { get; protected set; }

    public virtual string? Postconditions { get; protected set; }

    public virtual string? ChangeSummary { get; protected set; }

    // The classification of the test case when it was approved. These are null on a version that was published before the
    // snapshot held them; readers then fall back to the test case as it is now.

    public virtual PriorityLevel? Priority { get; protected set; }

    public virtual SeverityLevel? Severity { get; protected set; }

    public virtual TestKind? Kind { get; protected set; }

    public virtual TestLayer? Layer { get; protected set; }

    public virtual ExecutionType? ExecutionType { get; protected set; }

    protected TestCaseVersion()
    {
        Title = default!;
        StepsJson = default!;
    }

    public TestCaseVersion(
        Guid id,
        Guid? tenantId,
        Guid testCaseId,
        int versionNumber,
        string title,
        string? preconditions,
        string stepsJson,
        string? postconditions,
        string? changeSummary,
        PriorityLevel? priority = null,
        SeverityLevel? severity = null,
        TestKind? kind = null,
        TestLayer? layer = null,
        ExecutionType? executionType = null)
        : base(id)
    {
        TenantId = tenantId;
        TestCaseId = testCaseId;
        VersionNumber = versionNumber;
        Title = title;
        Preconditions = preconditions;
        StepsJson = Check.NotNull(stepsJson, nameof(stepsJson));
        Postconditions = postconditions;
        ChangeSummary = Check.Length(changeSummary, nameof(changeSummary), TestCaseConsts.MaxChangeSummaryLength);
        Priority = priority;
        Severity = severity;
        Kind = kind;
        Layer = layer;
        ExecutionType = executionType;
    }
}
