using Acme.TestCaseManagement.Enums;
using Volo.Abp.Application.Dtos;

namespace Acme.TestCaseManagement.Runs.Dtos;

public class TestRunDto : AuditedEntityDto<Guid>
{
    public Guid? TestPlanId { get; set; }

    public string Title { get; set; } = string.Empty;

    public string Environment { get; set; } = string.Empty;

    public Guid? AssignedToUserId { get; set; }

    public RunStatus Status { get; set; }

    /// <summary>Ordered by position in the run. Empty in list results; use GetAsync for the full run.</summary>
    public List<TestRunItemDto> Items { get; set; } = new();

    /// <summary>Roll-up figures. Only filled by GetAsync and the commands that return a full run.</summary>
    public TestRunSummaryDto Summary { get; set; } = new();
}

public class TestRunItemDto
{
    public Guid Id { get; set; }

    public Guid TestRunId { get; set; }

    public int Sequence { get; set; }

    /// <summary>The frozen snapshot this item executes. It never changes.</summary>
    public Guid TestCaseVersionId { get; set; }

    public int VersionNumber { get; set; }

    public Guid TestCaseId { get; set; }

    /// <summary>Current code of the library test case (null if it was deleted).</summary>
    public string? TestCaseCode { get; set; }

    /// <summary>Title as frozen in the version snapshot.</summary>
    public string TestCaseTitle { get; set; } = string.Empty;

    public Guid? AssignedUserId { get; set; }

    public TestResultStatus CurrentStatus { get; set; }

    public int AttemptCount { get; set; }
}

public class TestRunSummaryDto
{
    public int TotalItems { get; set; }

    public int ExecutedItems { get; set; }

    public int Passed { get; set; }

    public int Failed { get; set; }

    public int Blocked { get; set; }

    public int Skipped { get; set; }

    public int Untested { get; set; }

    /// <summary>Items with at least one attempt, as a percentage (0-100).</summary>
    public double CompletionPercentage { get; set; }

    /// <summary>
    /// Share of executed items whose first attempt passed (0-100), derived from the attempt log.
    /// Null when nothing has been executed yet.
    /// </summary>
    public double? FirstTimePassRate { get; set; }
}
