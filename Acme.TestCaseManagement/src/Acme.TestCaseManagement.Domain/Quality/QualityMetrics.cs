using Acme.TestCaseManagement.Enums;

namespace Acme.TestCaseManagement.Quality;

/// <summary>One run item in the evaluated scope.</summary>
/// <param name="RunItemId">Id of the run item.</param>
/// <param name="RunId">Id of the run the item belongs to.</param>
/// <param name="TestCaseId">Id of the test case the item's version snapshot belongs to.</param>
/// <param name="Priority">Priority of the test case; Urgent counts as P1.</param>
/// <param name="Status">Current status, i.e. the result of the latest attempt.</param>
/// <param name="FirstAttemptStatus">Result of attempt #1; null when the item was never executed.</param>
public record QualityItemInfo(
    Guid RunItemId,
    Guid RunId,
    Guid TestCaseId,
    PriorityLevel Priority,
    TestResultStatus Status,
    TestResultStatus? FirstAttemptStatus);

/// <summary>An unresolved defect link found in the evaluated scope.</summary>
public record QualityDefectInfo(Guid DefectLinkId, string ExternalSystem, string IssueKey, string? IssueUrl, SeverityLevel Severity);

/// <summary>A distinct open issue (the residual risk listed in a completion report).</summary>
public record OpenDefectSummary(string ExternalSystem, string IssueKey, string? IssueUrl, SeverityLevel Severity);

public record OpenDefectCounts(int Critical, int High, int Medium, int Low, int Total);

/// <summary>
/// Figures a quality gate is evaluated on, and the "summary statistics" a sign-off report freezes.
/// All rates are percentages rounded down to two decimals. See the plan (ADR 4.5) for the definitions.
/// </summary>
public record QualityMetrics(
    int RunCount,
    int TotalItems,
    int Passed,
    int Failed,
    int Blocked,
    int Skipped,
    int Untested,
    decimal CompletionPercentage,
    decimal? PassRate,
    decimal? FirstTimePassRate,
    int P1Total,
    int P1Executed,
    decimal P1ExecutionRate,
    OpenDefectCounts OpenDefects,
    IReadOnlyList<OpenDefectSummary> OpenDefectIssues);

public static class QualityMetricsCalculator
{
    /// <summary>Test cases of this priority are the "P1" tests the gate requires to be executed.</summary>
    public const PriorityLevel P1Priority = PriorityLevel.Urgent;

    public static QualityMetrics Calculate(
        int runCount,
        IReadOnlyCollection<QualityItemInfo> items,
        IReadOnlyCollection<QualityDefectInfo> openDefects)
    {
        int Count(TestResultStatus status) => items.Count(i => i.Status == status);

        var total = items.Count;
        var passed = Count(TestResultStatus.Passed);
        var skipped = Count(TestResultStatus.Skipped);
        var untested = Count(TestResultStatus.Untested);
        var applicable = total - skipped;

        var firstAttempts = items.Where(i => i.FirstAttemptStatus.HasValue).ToList();

        var p1 = items.Where(i => i.Priority == P1Priority).ToList();
        var p1Executed = p1.Count(i => i.Status is TestResultStatus.Passed or TestResultStatus.Failed);

        var issues = DistinctIssues(openDefects);

        return new QualityMetrics(
            RunCount: runCount,
            TotalItems: total,
            Passed: passed,
            Failed: Count(TestResultStatus.Failed),
            Blocked: Count(TestResultStatus.Blocked),
            Skipped: skipped,
            Untested: untested,
            CompletionPercentage: total == 0 ? 0 : RoundDown((total - untested) * 100m / total),
            PassRate: applicable == 0 ? null : RoundDown(passed * 100m / applicable),
            FirstTimePassRate: firstAttempts.Count == 0
                ? null
                : RoundDown(firstAttempts.Count(i => i.FirstAttemptStatus == TestResultStatus.Passed) * 100m / firstAttempts.Count),
            P1Total: p1.Count,
            P1Executed: p1Executed,
            P1ExecutionRate: p1.Count == 0 ? 100m : RoundDown(p1Executed * 100m / p1.Count),
            OpenDefects: new OpenDefectCounts(
                issues.Count(i => i.Severity == SeverityLevel.Critical),
                issues.Count(i => i.Severity == SeverityLevel.High),
                issues.Count(i => i.Severity == SeverityLevel.Medium),
                issues.Count(i => i.Severity == SeverityLevel.Low),
                issues.Count),
            OpenDefectIssues: issues);
    }

    /// <summary>Rounds down to two decimals, so a value below a threshold is never displayed as equal to it.</summary>
    public static decimal RoundDown(decimal value)
    {
        return Math.Floor(value * 100m) / 100m;
    }

    /// <summary>
    /// One entry per distinct issue (tracker + key, ignoring case), at the highest severity of its open links,
    /// most severe first. The same ticket linked from many failing tests is one defect.
    /// </summary>
    private static List<OpenDefectSummary> DistinctIssues(IEnumerable<QualityDefectInfo> openDefects)
    {
        return openDefects
            .GroupBy(d => (System: d.ExternalSystem.Trim().ToLowerInvariant(), Key: d.IssueKey.Trim().ToLowerInvariant()))
            .Select(g =>
            {
                var worst = g.OrderByDescending(d => d.Severity).First();
                return new OpenDefectSummary(worst.ExternalSystem, worst.IssueKey, worst.IssueUrl, worst.Severity);
            })
            .OrderByDescending(d => d.Severity)
            .ThenBy(d => d.ExternalSystem, StringComparer.OrdinalIgnoreCase)
            .ThenBy(d => d.IssueKey, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }
}
