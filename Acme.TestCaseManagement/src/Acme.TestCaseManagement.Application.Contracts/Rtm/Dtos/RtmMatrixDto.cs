using Acme.TestCaseManagement.Enums;

namespace Acme.TestCaseManagement.Rtm.Dtos;

public class RtmMatrixDto
{
    /// <summary>Figures over every requirement that matches the milestone and text filters (not limited by paging or status).</summary>
    public RtmSummaryDto Summary { get; set; } = new();

    /// <summary>The requested page of requirement rows (after the status filter), ordered by code.</summary>
    public List<RequirementCoverageDto> Requirements { get; set; } = new();

    /// <summary>Number of rows matching the status filter, for paging.</summary>
    public int TotalCount { get; set; }
}

/// <summary>Roll-up of the matrix. See the plan (ADR 4.4) for the definitions.</summary>
public class RtmSummaryDto
{
    public int TotalRequirements { get; set; }

    /// <summary>Requirements with at least one linked, non-deprecated test case.</summary>
    public int CoveredRequirements { get; set; }

    public int UncoveredRequirements { get; set; }

    /// <summary>Covered / total requirements, 0-100 (0 when there are no requirements).</summary>
    public double CoveragePercentage { get; set; }

    public int PassedRequirements { get; set; }

    /// <summary>Passed / total requirements, 0-100. Uncovered requirements count in the total.</summary>
    public double PassedPercentage { get; set; }

    public int FailedRequirements { get; set; }

    public int BlockedRequirements { get; set; }

    public int NotRunRequirements { get; set; }
}

public class RequirementCoverageDto
{
    public Guid RequirementId { get; set; }

    public string Code { get; set; } = string.Empty;

    public string Title { get; set; } = string.Empty;

    public PriorityLevel Priority { get; set; }

    public Guid? MilestoneId { get; set; }

    public RequirementCoverageStatus Status { get; set; }

    /// <summary>All linked test cases, including deprecated ones (which are not counted).</summary>
    public List<RequirementTestCaseDto> TestCases { get; set; } = new();

    /// <summary>
    /// Unresolved defects linked to executions of the requirement's test cases (within the scope filters).
    /// A defect stops blocking when it is marked resolved.
    /// </summary>
    public List<RequirementDefectDto> BlockingDefects { get; set; } = new();
}

public class RequirementTestCaseDto
{
    public Guid TestCaseId { get; set; }

    public string Code { get; set; } = string.Empty;

    public string Title { get; set; } = string.Empty;

    public TestCaseStatus Status { get; set; }

    /// <summary>False for deprecated test cases, which are ignored by the coverage rules.</summary>
    public bool CountsTowardCoverage { get; set; }

    /// <summary>Executed result in scope; Untested when the test case has no executed result.</summary>
    public TestResultStatus Result { get; set; }

    /// <summary>When the most recent counted result was recorded; lets readers judge how stale a pass is.</summary>
    public DateTime? LastExecutedTime { get; set; }
}

public class RequirementDefectDto
{
    public Guid TestCaseId { get; set; }

    public string TestCaseCode { get; set; } = string.Empty;

    public Guid TestExecutionId { get; set; }

    public string ExternalSystem { get; set; } = string.Empty;

    public string IssueKey { get; set; } = string.Empty;

    public string? IssueUrl { get; set; }

    public SeverityLevel Severity { get; set; }
}
