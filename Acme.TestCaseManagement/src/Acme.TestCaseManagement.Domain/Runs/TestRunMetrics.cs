using Acme.TestCaseManagement.Enums;

namespace Acme.TestCaseManagement.Runs;

/// <summary>Derived figures of a run, computed from the items and the attempt log (never stored).</summary>
public record TestRunMetrics(
    int TotalItems,
    int ExecutedItems,
    int Passed,
    int Failed,
    int Blocked,
    int Skipped,
    int Untested,
    double CompletionPercentage,
    double? FirstTimePassRate)
{
    /// <param name="items">All items of the run.</param>
    /// <param name="executions">Attempts of those items. Only the first attempt of each item matters for FTPR.</param>
    public static TestRunMetrics Calculate(IReadOnlyCollection<TestRunItem> items, IEnumerable<TestExecution> executions)
    {
        var total = items.Count;
        var executed = items.Count(i => i.CurrentStatus != TestResultStatus.Untested);

        var firstAttempts = executions
            .GroupBy(e => e.TestRunItemId)
            .Select(g => g.OrderBy(e => e.AttemptNumber).First())
            .ToList();

        double? firstTimePassRate = firstAttempts.Count == 0
            ? null
            : Math.Round(firstAttempts.Count(e => e.Status == TestResultStatus.Passed) * 100.0 / firstAttempts.Count, 2);

        return new TestRunMetrics(
            total,
            executed,
            items.Count(i => i.CurrentStatus == TestResultStatus.Passed),
            items.Count(i => i.CurrentStatus == TestResultStatus.Failed),
            items.Count(i => i.CurrentStatus == TestResultStatus.Blocked),
            items.Count(i => i.CurrentStatus == TestResultStatus.Skipped),
            items.Count(i => i.CurrentStatus == TestResultStatus.Untested),
            total == 0 ? 0 : Math.Round(executed * 100.0 / total, 2),
            firstTimePassRate);
    }
}
