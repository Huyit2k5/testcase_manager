using Acme.TestCaseManagement.Enums;

namespace Acme.TestCaseManagement.Requirements;

/// <summary>The most recent attempt of one run item of a test case, with the environment it ran in.</summary>
public record LatestTestResult(
    Guid TestCaseId,
    string Environment,
    TestResultStatus Status,
    DateTime ExecutedTime,
    Guid TestRunId,
    Guid TestExecutionId,
    int VersionNumber);

/// <summary>
/// Pure implementation of the rules in the plan (section 4.4). It has no dependencies so the rules can be
/// tested exhaustively and reused by the quality gate.
/// </summary>
public static class RequirementCoverageCalculator
{
    /// <summary>Keeps, for each environment (ignoring case), only the most recent result.</summary>
    public static IReadOnlyList<LatestTestResult> CurrentResults(IEnumerable<LatestTestResult> results)
    {
        return results
            .GroupBy(r => r.Environment.Trim(), StringComparer.OrdinalIgnoreCase)
            .Select(g => g.OrderByDescending(r => r.ExecutedTime).First())
            .ToList();
    }

    /// <summary>
    /// Status of a test case from its executed results: the latest result per environment, combined worst first
    /// (Failed, Blocked, Passed, Skipped). Untested when there is no executed result.
    /// </summary>
    public static TestResultStatus CalculateTestCaseStatus(IEnumerable<LatestTestResult> results)
    {
        var current = CurrentResults(results);
        if (current.Count == 0)
        {
            return TestResultStatus.Untested;
        }

        foreach (var status in new[] { TestResultStatus.Failed, TestResultStatus.Blocked, TestResultStatus.Passed })
        {
            if (current.Any(r => r.Status == status))
            {
                return status;
            }
        }

        return current.Any(r => r.Status == TestResultStatus.Skipped)
            ? TestResultStatus.Skipped
            : TestResultStatus.Untested;
    }

    /// <summary>
    /// Status of a requirement from the statuses of its linked, non-deprecated test cases. First matching rule wins:
    /// none = Uncovered; any Failed = Failed; any Untested = NotRun; any Blocked = Blocked;
    /// at least one Passed (rest Skipped) = Passed; all Skipped = NotRun.
    /// </summary>
    public static RequirementCoverageStatus CalculateRequirementStatus(IReadOnlyCollection<TestResultStatus> testCaseStatuses)
    {
        if (testCaseStatuses.Count == 0)
        {
            return RequirementCoverageStatus.Uncovered;
        }

        if (testCaseStatuses.Contains(TestResultStatus.Failed))
        {
            return RequirementCoverageStatus.Failed;
        }

        if (testCaseStatuses.Contains(TestResultStatus.Untested))
        {
            return RequirementCoverageStatus.NotRun;
        }

        if (testCaseStatuses.Contains(TestResultStatus.Blocked))
        {
            return RequirementCoverageStatus.Blocked;
        }

        return testCaseStatuses.Contains(TestResultStatus.Passed)
            ? RequirementCoverageStatus.Passed
            : RequirementCoverageStatus.NotRun;
    }
}
