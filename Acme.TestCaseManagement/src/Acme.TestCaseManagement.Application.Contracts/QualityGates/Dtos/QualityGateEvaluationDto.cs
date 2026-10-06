using Acme.TestCaseManagement.Enums;

namespace Acme.TestCaseManagement.QualityGates.Dtos;

/// <summary>Result of evaluating a quality gate for a test plan or milestone, with the breakdown per criterion.</summary>
public class QualityGateEvaluationDto
{
    /// <summary>True only when every criterion passed.</summary>
    public bool Passed { get; set; }

    public QualityGateSettingsDto Gate { get; set; } = new();

    public QualityGateScopeDto Scope { get; set; } = new();

    public QualityMetricsDto Metrics { get; set; } = new();

    /// <summary>All criteria in a stable order, passed or not.</summary>
    public List<GateCriterionResultDto> Criteria { get; set; } = new();

    public DateTime EvaluatedTime { get; set; }
}

public class QualityGateSettingsDto
{
    /// <summary>Null for the built-in baseline.</summary>
    public Guid? Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public decimal MinPassRate { get; set; }

    public int RequiredApprovals { get; set; }

    public bool IsBuiltIn { get; set; }
}

public class QualityGateScopeDto
{
    public Guid? TestPlanId { get; set; }

    public Guid? MilestoneId { get; set; }

    /// <summary>The plan, or every plan of the milestone.</summary>
    public List<ScopePlanDto> Plans { get; set; } = new();
}

public class ScopePlanDto
{
    public Guid Id { get; set; }

    public string Name { get; set; } = string.Empty;
}

public class GateCriterionResultDto
{
    /// <summary>PassRate, P1Executed, OpenCriticalDefects or OpenHighDefects.</summary>
    public string Code { get; set; } = string.Empty;

    /// <summary>Localized name of the criterion.</summary>
    public string Label { get; set; } = string.Empty;

    /// <summary>"&gt;=" when the actual value must reach the threshold, "&lt;=" when it must not exceed it.</summary>
    public string Operator { get; set; } = string.Empty;

    public decimal Threshold { get; set; }

    /// <summary>Null when it cannot be computed, e.g. a pass rate with no applicable tests.</summary>
    public decimal? Actual { get; set; }

    public bool Passed { get; set; }
}

/// <summary>The figures behind the evaluation. See the plan (ADR 4.5) for the exact definitions.</summary>
public class QualityMetricsDto
{
    public int RunCount { get; set; }

    public int TotalItems { get; set; }

    public int Passed { get; set; }

    public int Failed { get; set; }

    public int Blocked { get; set; }

    /// <summary>Skipped items are not applicable and are left out of the pass rate.</summary>
    public int Skipped { get; set; }

    public int Untested { get; set; }

    /// <summary>Items with a status other than Untested, as a percentage of all items (rounded down).</summary>
    public decimal CompletionPercentage { get; set; }

    /// <summary>Passed / (items - skipped), as a percentage rounded down. Null when no item is applicable.</summary>
    public decimal? PassRate { get; set; }

    /// <summary>Informational: attempted items whose first attempt passed.</summary>
    public decimal? FirstTimePassRate { get; set; }

    public int P1Total { get; set; }

    /// <summary>P1 items whose current status is Passed or Failed.</summary>
    public int P1Executed { get; set; }

    /// <summary>100 when there is no P1 item.</summary>
    public decimal P1ExecutionRate { get; set; }

    public OpenDefectCountsDto OpenDefects { get; set; } = new();

    /// <summary>Distinct open issues, most severe first; the residual risks of the release.</summary>
    public List<OpenDefectDto> OpenDefectIssues { get; set; } = new();
}

public class OpenDefectCountsDto
{
    public int Critical { get; set; }

    public int High { get; set; }

    public int Medium { get; set; }

    public int Low { get; set; }

    public int Total { get; set; }
}

public class OpenDefectDto
{
    public string ExternalSystem { get; set; } = string.Empty;

    public string IssueKey { get; set; } = string.Empty;

    public string? IssueUrl { get; set; }

    public SeverityLevel Severity { get; set; }
}
