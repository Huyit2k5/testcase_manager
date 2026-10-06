using Acme.TestCaseManagement.Enums;

namespace Acme.TestCaseManagement.TestCases.Dtos;

/// <summary>A defect linked to some execution of a test case, with the run it was found in.</summary>
public class TestCaseDefectDto
{
    public Guid DefectLinkId { get; set; }

    public string ExternalSystem { get; set; } = string.Empty;

    public string IssueKey { get; set; } = string.Empty;

    public string? IssueUrl { get; set; }

    public SeverityLevel Severity { get; set; }

    public bool IsResolved { get; set; }

    /// <summary>When the link was recorded.</summary>
    public DateTime LinkedTime { get; set; }

    public Guid? LinkedByUserId { get; set; }

    public Guid TestExecutionId { get; set; }

    public int AttemptNumber { get; set; }

    /// <summary>Test case version that was executed.</summary>
    public int VersionNumber { get; set; }

    public DateTime ExecutionTime { get; set; }

    public Guid TestRunId { get; set; }

    public string TestRunTitle { get; set; } = string.Empty;

    public string Environment { get; set; } = string.Empty;
}
