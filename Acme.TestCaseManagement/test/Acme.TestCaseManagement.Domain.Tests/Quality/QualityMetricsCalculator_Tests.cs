using Acme.TestCaseManagement.Enums;
using Shouldly;
using Xunit;

namespace Acme.TestCaseManagement.Quality;

/// <summary>Executable form of the metric definitions in plan.md, section 4.5.</summary>
public class QualityMetricsCalculator_Tests
{
    private const TestResultStatus P = TestResultStatus.Passed;
    private const TestResultStatus F = TestResultStatus.Failed;
    private const TestResultStatus B = TestResultStatus.Blocked;
    private const TestResultStatus S = TestResultStatus.Skipped;
    private const TestResultStatus U = TestResultStatus.Untested;

    private static QualityItemInfo Item(
        TestResultStatus status,
        PriorityLevel priority = PriorityLevel.Medium,
        TestResultStatus? firstAttempt = null) =>
        new(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), priority, status, firstAttempt);

    private static List<QualityItemInfo> Items(params (TestResultStatus Status, int Count)[] groups) =>
        groups.SelectMany(g => Enumerable.Range(0, g.Count).Select(_ => Item(g.Status))).ToList();

    private static QualityDefectInfo Defect(string system, string key, SeverityLevel severity, string? url = null) =>
        new(Guid.NewGuid(), system, key, url, severity);

    [Fact]
    public void An_Empty_Scope_Has_No_Pass_Rate_And_Nothing_To_Execute_For_P1()
    {
        var metrics = QualityMetricsCalculator.Calculate(0, Array.Empty<QualityItemInfo>(), Array.Empty<QualityDefectInfo>());

        metrics.TotalItems.ShouldBe(0);
        metrics.PassRate.ShouldBeNull();
        metrics.FirstTimePassRate.ShouldBeNull();
        metrics.CompletionPercentage.ShouldBe(0);
        metrics.P1Total.ShouldBe(0);
        metrics.P1ExecutionRate.ShouldBe(100);
        metrics.OpenDefects.Total.ShouldBe(0);
    }

    [Fact]
    public void Pass_Rate_Is_Passed_Over_Applicable_Items_So_Untested_Blocked_And_Failed_Count_Against_It()
    {
        // 15 items: 8 passed, 1 failed, 1 blocked, 2 untested, 3 skipped (not applicable) -> 12 applicable.
        var items = Items((P, 8), (F, 1), (B, 1), (U, 2), (S, 3));

        var metrics = QualityMetricsCalculator.Calculate(2, items, Array.Empty<QualityDefectInfo>());

        metrics.RunCount.ShouldBe(2);
        metrics.TotalItems.ShouldBe(15);
        (metrics.Passed, metrics.Failed, metrics.Blocked, metrics.Skipped, metrics.Untested).ShouldBe((8, 1, 1, 3, 2));
        metrics.PassRate.ShouldBe(66.66m); // 8 / 12 = 66.666..., shown rounded down
        metrics.CompletionPercentage.ShouldBe(86.66m); // 13 of 15 items have a status other than Untested
    }

    [Fact]
    public void Unexecuted_Tests_Cannot_Raise_The_Pass_Rate()
    {
        var metrics = QualityMetricsCalculator.Calculate(
            1, Items((P, 5), (U, 95)), Array.Empty<QualityDefectInfo>());

        metrics.PassRate.ShouldBe(5m);
    }

    [Fact]
    public void Skipped_Items_Are_Left_Out_And_A_Scope_Of_Only_Skipped_Items_Has_No_Pass_Rate()
    {
        QualityMetricsCalculator.Calculate(1, Items((P, 1), (S, 99)), Array.Empty<QualityDefectInfo>())
            .PassRate.ShouldBe(100m);
        QualityMetricsCalculator.Calculate(1, Items((S, 4)), Array.Empty<QualityDefectInfo>())
            .PassRate.ShouldBeNull();
    }

    [Fact]
    public void Rates_Are_Rounded_Down_So_A_Failing_Value_Is_Never_Shown_As_The_Threshold()
    {
        var metrics = QualityMetricsCalculator.Calculate(
            1, Items((P, 94996), (F, 5004)), Array.Empty<QualityDefectInfo>());

        metrics.PassRate.ShouldBe(94.99m); // 94.996 exactly: not 95.00
    }

    [Fact]
    public void First_Time_Pass_Rate_Uses_The_First_Attempt_Of_Items_That_Were_Attempted()
    {
        var items = new List<QualityItemInfo>
        {
            Item(P, firstAttempt: P),   // passed first time
            Item(P, firstAttempt: F),   // failed first, fixed later: current Passed, first attempt Failed
            Item(F, firstAttempt: F),
            Item(U),                    // never attempted: not counted
        };

        var metrics = QualityMetricsCalculator.Calculate(1, items, Array.Empty<QualityDefectInfo>());

        metrics.FirstTimePassRate.ShouldBe(33.33m);
        metrics.PassRate.ShouldBe(50m); // 2 passed of 4 applicable
    }

    [Fact]
    public void P1_Items_Are_Executed_Only_When_They_Have_A_Passed_Or_Failed_Verdict()
    {
        var items = new List<QualityItemInfo>
        {
            Item(P, PriorityLevel.Urgent),
            Item(F, PriorityLevel.Urgent),
            Item(B, PriorityLevel.Urgent),  // blocked: not executed
            Item(S, PriorityLevel.Urgent),  // skipped: not executed
            Item(U, PriorityLevel.Urgent),
            Item(U, PriorityLevel.High),    // not P1
            Item(U, PriorityLevel.Low),
        };

        var metrics = QualityMetricsCalculator.Calculate(1, items, Array.Empty<QualityDefectInfo>());

        metrics.P1Total.ShouldBe(5);
        metrics.P1Executed.ShouldBe(2);
        metrics.P1ExecutionRate.ShouldBe(40m);
    }

    [Fact]
    public void P1_Rate_Is_100_When_Every_P1_Item_Has_A_Verdict()
    {
        var items = new List<QualityItemInfo> { Item(P, PriorityLevel.Urgent), Item(F, PriorityLevel.Urgent), Item(U) };

        QualityMetricsCalculator.Calculate(1, items, Array.Empty<QualityDefectInfo>())
            .P1ExecutionRate.ShouldBe(100m);
    }

    [Fact]
    public void An_Issue_Linked_Many_Times_Counts_Once_At_Its_Highest_Severity_Ignoring_Case()
    {
        var defects = new[]
        {
            Defect("Jira", "BUG-1", SeverityLevel.Low),
            Defect("jira", "bug-1", SeverityLevel.Critical, "https://acme.test/BUG-1"),
            Defect("JIRA", "Bug-1", SeverityLevel.Medium),
            Defect("GitHub", "BUG-1", SeverityLevel.High), // same key, other tracker: a different issue
            Defect("Jira", "BUG-2", SeverityLevel.Medium),
            Defect("Jira", "BUG-3", SeverityLevel.Low),
        };

        var metrics = QualityMetricsCalculator.Calculate(1, Array.Empty<QualityItemInfo>(), defects);

        (metrics.OpenDefects.Critical, metrics.OpenDefects.High, metrics.OpenDefects.Medium, metrics.OpenDefects.Low, metrics.OpenDefects.Total)
            .ShouldBe((1, 1, 1, 1, 4));
        metrics.OpenDefectIssues.Select(d => (d.IssueKey, d.Severity)).ToList().ShouldBe(new List<(string, SeverityLevel)>
        {
            ("bug-1", SeverityLevel.Critical),
            ("BUG-1", SeverityLevel.High),
            ("BUG-2", SeverityLevel.Medium),
            ("BUG-3", SeverityLevel.Low),
        }, ignoreOrder: false);
        metrics.OpenDefectIssues[0].IssueUrl.ShouldBe("https://acme.test/BUG-1");
    }
}
