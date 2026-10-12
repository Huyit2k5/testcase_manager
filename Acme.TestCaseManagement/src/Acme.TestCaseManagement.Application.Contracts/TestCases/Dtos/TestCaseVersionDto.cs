using Acme.TestCaseManagement.Enums;
using Volo.Abp.Application.Dtos;

namespace Acme.TestCaseManagement.TestCases.Dtos;

/// <summary>Read-only view of an immutable <c>TestCaseVersion</c> snapshot.</summary>
public class TestCaseVersionDto : CreationAuditedEntityDto<Guid>
{
    public Guid TestCaseId { get; set; }

    public int VersionNumber { get; set; }

    public string Title { get; set; } = string.Empty;

    public string? Preconditions { get; set; }

    public string? Postconditions { get; set; }

    public string? ChangeSummary { get; set; }

    /// <summary>The classification of the test case when the version was published; null on a version published before it was kept.</summary>
    public PriorityLevel? Priority { get; set; }

    public SeverityLevel? Severity { get; set; }

    public TestKind? Kind { get; set; }

    public TestLayer? Layer { get; set; }

    public ExecutionType? ExecutionType { get; set; }

    public List<TestStepDto> Steps { get; set; } = new();
}
