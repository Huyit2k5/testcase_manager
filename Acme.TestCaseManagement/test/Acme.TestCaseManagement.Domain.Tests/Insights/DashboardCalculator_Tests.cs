using Acme.TestCaseManagement.Enums;
using Shouldly;
using Xunit;

namespace Acme.TestCaseManagement.Insights;

/// <summary>Executable form of the dashboard definitions in plan.md, section 4.10.</summary>
public class DashboardCalculator_Tests
{
    private static readonly DateTime Today = new(2026, 3, 20, 15, 30, 0);

    private static DateTime Day(int daysAgo, int hour = 10) => Today.Date.AddDays(-daysAgo).AddHours(hour);

    private static InsightItem Item(TestResultStatus status, int? executedDaysAgo = null, TestResultStatus? first = null, Guid? testCaseId = null) =>
        new(Guid.NewGuid(), testCaseId ?? Guid.NewGuid(), status, first ?? (executedDaysAgo.HasValue ? status : null),
            executedDaysAgo.HasValue ? Day(executedDaysAgo.Value) : null);

    private static InsightAttempt Attempt(TestResultStatus status, int daysAgo) =>
        new(Guid.NewGuid(), Guid.NewGuid(), status, 1, Day(daysAgo));

    // ---- progress

    [Fact]
    public void The_Pass_Rate_Leaves_Skipped_Items_Out_Like_The_Quality_Gate_Does()
    {
        var items = new List<InsightItem>
        {
            Item(TestResultStatus.Passed, 1, TestResultStatus.Passed), Item(TestResultStatus.Passed, 1, TestResultStatus.Failed),
            Item(TestResultStatus.Failed, 1, TestResultStatus.Failed), Item(TestResultStatus.Skipped, 1),
            Item(TestResultStatus.Untested), Item(TestResultStatus.Blocked, 1),
        };

        var progress = DashboardCalculator.Progress(items);

        progress.TotalItems.ShouldBe(6);
        progress.Passed.ShouldBe(2);
        progress.PassRate.ShouldBe(40m);                 // 2 of the 5 items that are not skipped
        progress.FirstTimePassRate.ShouldBe(20m);        // 1 of the 5 first attempts
        progress.CompletionPercentage.ShouldBe(83.33m);  // 5 of 6 are not untested, rounded down
    }

    [Fact]
    public void An_Empty_Scope_Has_No_Rates()
    {
        var progress = DashboardCalculator.Progress(new List<InsightItem>());

        progress.PassRate.ShouldBeNull();
        progress.FirstTimePassRate.ShouldBeNull();
        progress.CompletionPercentage.ShouldBe(0m);
    }

    // ---- velocity

    [Fact]
    public void Velocity_Has_A_Point_For_Every_Day_Ending_Today()
    {
        var attempts = new[]
        {
            Attempt(TestResultStatus.Passed, 0), Attempt(TestResultStatus.Failed, 0), Attempt(TestResultStatus.Passed, 2),
            Attempt(TestResultStatus.Passed, 30), // outside the window
        };
        var items = new[] { Item(TestResultStatus.Passed, 0), Item(TestResultStatus.Failed, 0), Item(TestResultStatus.Passed, 2) };

        var velocity = DashboardCalculator.Velocity(attempts, items, Today, 7);

        velocity.Points.Count.ShouldBe(7);
        velocity.Points[0].Date.ShouldBe(Today.Date.AddDays(-6));
        velocity.Points[^1].Date.ShouldBe(Today.Date);
        velocity.Points[^1].Attempts.ShouldBe(2);
        velocity.Points[^1].Passed.ShouldBe(1);
        velocity.Points[^1].Failed.ShouldBe(1);
        velocity.Points[^1].ItemsCompleted.ShouldBe(2);
        velocity.Points[^3].Attempts.ShouldBe(1);
        velocity.TotalAttempts.ShouldBe(3);
        velocity.AveragePerDay.ShouldBe(0.43m);
        velocity.TrendPercent.ShouldBeNull();   // seven days have nothing before them
    }

    [Fact]
    public void The_Trend_Compares_The_Last_Seven_Days_With_The_Seven_Before()
    {
        var before = Enumerable.Range(7, 7).SelectMany(d => new[] { Attempt(TestResultStatus.Passed, d), Attempt(TestResultStatus.Passed, d) });
        var recent = Enumerable.Range(0, 7).SelectMany(d => Enumerable.Range(0, 3).Select(_ => Attempt(TestResultStatus.Passed, d)));

        var velocity = DashboardCalculator.Velocity(before.Concat(recent).ToList(), new List<InsightItem>(), Today, 14);

        velocity.Last7DaysAverage.ShouldBe(3m);
        velocity.TrendPercent.ShouldBe(50m);   // 3 a day against 2 a day

        var nothingBefore = DashboardCalculator.Velocity(recent.ToList(), new List<InsightItem>(), Today, 14);
        nothingBefore.TrendPercent.ShouldBeNull();
    }

    // ---- burn-down

    [Fact]
    public void The_Burn_Down_Counts_Items_Without_An_Attempt_Day_By_Day()
    {
        // 10 items; 2 were executed 4 days ago, 3 two days ago, 1 today; 4 are untouched.
        var items = new List<InsightItem>
        {
            Item(TestResultStatus.Passed, 4), Item(TestResultStatus.Failed, 4),
            Item(TestResultStatus.Passed, 2), Item(TestResultStatus.Passed, 2), Item(TestResultStatus.Blocked, 2),
            Item(TestResultStatus.Passed, 0),
        };
        items.AddRange(Enumerable.Range(0, 4).Select(_ => Item(TestResultStatus.Untested)));

        var report = DashboardCalculator.BurnDown(items, Today.Date.AddDays(-5), Today.Date.AddDays(5), Today);

        report.TotalItems.ShouldBe(10);
        report.RemainingAtStart.ShouldBe(10);
        report.RemainingNow.ShouldBe(4);
        report.Points.Count.ShouldBe(11);
        report.Points.Select(p => p.Remaining).ShouldBe(new int?[] { 10, 8, 8, 5, 5, 4, null, null, null, null, null });
        report.Points[^1].Ideal.ShouldBe(0m);
        report.Points[0].Ideal.ShouldBe(9.09m);   // 10 items over 11 days (both ends included): 1/11 of them burned on the first day
    }

    [Fact]
    public void The_Ideal_Line_Falls_Evenly_To_Zero_On_The_End_Day()
    {
        var items = Enumerable.Range(0, 10).Select(_ => Item(TestResultStatus.Untested)).ToList();

        var report = DashboardCalculator.BurnDown(items, Today.Date, Today.Date.AddDays(4), Today);

        report.Points.Select(p => p.Ideal).ShouldBe(new[] { 8m, 6m, 4m, 2m, 0m });
    }

    [Fact]
    public void Remaining_Today_Above_The_Ideal_Line_Is_Behind_And_Below_It_Is_On_Track()
    {
        var items = Enumerable.Range(0, 10).Select(i => Item(i < 2 ? TestResultStatus.Passed : TestResultStatus.Untested, i < 2 ? 0 : null)).ToList();

        var behind = DashboardCalculator.BurnDown(items, Today.Date.AddDays(-4), Today.Date.AddDays(1), Today);
        behind.RemainingNow.ShouldBe(8);
        behind.OnTrack.ShouldBe(false);

        var ahead = DashboardCalculator.BurnDown(items, Today.Date, Today.Date.AddDays(30), Today);
        ahead.OnTrack.ShouldBe(true);
    }

    [Fact]
    public void The_Projection_Divides_What_Is_Left_By_The_Pace_Of_The_Last_Seven_Days()
    {
        // 14 items done in the last 7 days is 2 a day; 6 are left: 3 more days.
        var items = Enumerable.Range(0, 14).Select(i => Item(TestResultStatus.Passed, i % 7)).ToList();
        items.AddRange(Enumerable.Range(0, 6).Select(_ => Item(TestResultStatus.Untested)));

        var report = DashboardCalculator.BurnDown(items, Today.Date.AddDays(-9), null, Today);

        report.ItemsPerDay.ShouldBe(2m);
        report.ProjectedFinish.ShouldBe(Today.Date.AddDays(3));
    }

    [Fact]
    public void Nothing_Is_Projected_When_Nothing_Is_Left_Or_Nothing_Moves()
    {
        var done = new List<InsightItem> { Item(TestResultStatus.Passed, 1), Item(TestResultStatus.Failed, 0) };
        DashboardCalculator.BurnDown(done, Today.Date.AddDays(-3), null, Today).ProjectedFinish.ShouldBeNull();

        var stuck = new List<InsightItem> { Item(TestResultStatus.Untested), Item(TestResultStatus.Untested) };
        var report = DashboardCalculator.BurnDown(stuck, Today.Date.AddDays(-3), null, Today);
        report.ProjectedFinish.ShouldBeNull();
        report.ItemsPerDay.ShouldBe(0m);
    }

    [Fact]
    public void Items_Executed_Before_The_Start_Are_Not_Remaining_At_The_Start()
    {
        var items = new List<InsightItem> { Item(TestResultStatus.Passed, 10), Item(TestResultStatus.Untested), Item(TestResultStatus.Untested) };

        var report = DashboardCalculator.BurnDown(items, Today.Date.AddDays(-2), null, Today);

        report.TotalItems.ShouldBe(3);
        report.RemainingAtStart.ShouldBe(2);
    }

    [Fact]
    public void An_Empty_Scope_Burns_Nothing_And_A_Late_Plan_Keeps_Counting_Days()
    {
        var empty = DashboardCalculator.BurnDown(new List<InsightItem>(), Today.Date.AddDays(-2), null, Today);
        empty.OnTrack.ShouldBeNull();
        empty.Points.Count.ShouldBe(3);

        // The plan ended 3 days ago: the chart goes on to today and the ideal is 0 on every day after the end.
        var items = new List<InsightItem> { Item(TestResultStatus.Untested) };
        var late = DashboardCalculator.BurnDown(items, Today.Date.AddDays(-8), Today.Date.AddDays(-3), Today);
        late.End.ShouldBe(Today.Date.AddDays(-3));
        late.Points[^1].Date.ShouldBe(Today.Date);
        late.Points[^1].Ideal.ShouldBe(0m);
        late.OnTrack.ShouldBe(false);
    }

    [Fact]
    public void A_Very_Long_Plan_Is_Cut_To_The_Latest_120_Days()
    {
        var report = DashboardCalculator.BurnDown(new List<InsightItem> { Item(TestResultStatus.Untested) }, Today.Date.AddDays(-400), null, Today);

        report.Points.Count.ShouldBe(120);
        report.Points[^1].Date.ShouldBe(Today.Date);
    }

    // ---- defect density

    [Fact]
    public void Defect_Density_Counts_Distinct_Defects_Per_100_Executed_Test_Cases()
    {
        var tc1 = Guid.NewGuid();
        var tc2 = Guid.NewGuid();
        var items = new List<InsightItem>
        {
            Item(TestResultStatus.Failed, 1, testCaseId: tc1), Item(TestResultStatus.Failed, 1, testCaseId: tc2),
            Item(TestResultStatus.Passed, 1), Item(TestResultStatus.Passed, 1), Item(TestResultStatus.Untested),
        };
        var defects = new List<InsightDefect>
        {
            // One ticket linked from two failing tests is one defect, written in two spellings.
            new(tc1, "Jira", "BUG-1", SeverityLevel.High, false),
            new(tc2, "jira ", "bug-1", SeverityLevel.Critical, false),
            new(tc2, "Jira", "BUG-2", SeverityLevel.Low, true),
        };

        var density = DashboardCalculator.DefectDensity(items, defects);

        density.ExecutedTests.ShouldBe(4);
        density.Defects.ShouldBe(2);
        density.OpenDefects.ShouldBe(1);
        density.ResolvedDefects.ShouldBe(1);
        density.DefectsPer100Executed.ShouldBe(50m);
        density.TestsWithDefects.ShouldBe(2);
        density.TestsWithDefectsPercent.ShouldBe(50m);
        density.OpenBySeverity.Critical.ShouldBe(1);   // the worst severity of the open links of BUG-1
        density.OpenBySeverity.High.ShouldBe(0);
        density.OpenBySeverity.Total.ShouldBe(1);
    }

    [Fact]
    public void An_Issue_Is_Open_While_Any_Of_Its_Links_Is_And_Is_Resolved_When_All_Are()
    {
        var items = new List<InsightItem> { Item(TestResultStatus.Failed, 1) };
        var tc = Guid.NewGuid();

        var someOpen = DashboardCalculator.DefectDensity(items, new List<InsightDefect>
        {
            new(tc, "Jira", "BUG-1", SeverityLevel.High, true), new(tc, "Jira", "BUG-1", SeverityLevel.Medium, false),
        });
        someOpen.OpenDefects.ShouldBe(1);
        someOpen.OpenBySeverity.Medium.ShouldBe(1);   // only the open link counts for the severity

        var allResolved = DashboardCalculator.DefectDensity(items, new List<InsightDefect>
        {
            new(tc, "Jira", "BUG-1", SeverityLevel.High, true), new(tc, "Jira", "BUG-1", SeverityLevel.Medium, true),
        });
        allResolved.OpenDefects.ShouldBe(0);
        allResolved.ResolvedDefects.ShouldBe(1);
    }

    [Fact]
    public void Defect_Density_Needs_Executed_Tests()
    {
        var density = DashboardCalculator.DefectDensity(
            new List<InsightItem> { Item(TestResultStatus.Untested) },
            new List<InsightDefect> { new(Guid.NewGuid(), "Jira", "BUG-1", SeverityLevel.High, false) });

        density.DefectsPer100Executed.ShouldBeNull();
        density.TestsWithDefectsPercent.ShouldBeNull();
        density.Defects.ShouldBe(1);
    }
}
