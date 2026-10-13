using Acme.TestCaseManagement.Enums;
using Shouldly;
using Xunit;

namespace Acme.TestCaseManagement.Insights;

/// <summary>The definitions of the trend over test plans and of the workload of the testers, as numbers (plan.md, section 4.32).</summary>
public class TrendAndWorkloadCalculator_Tests
{
    private static readonly Guid Ann = Guid.Parse("00000000-0000-0000-0000-00000000000a");
    private static readonly Guid Bob = Guid.Parse("00000000-0000-0000-0000-00000000000b");
    private static readonly DateTime Today = new(2026, 3, 20);

    private static WorkloadItem Item(Guid? assignedTo, TestResultStatus status, Guid? id = null) => new(id ?? Guid.NewGuid(), assignedTo, status);

    private static WorkloadAttempt Attempt(Guid? user, TestResultStatus status, Guid? item = null, int seconds = 0, int daysAgo = 0) =>
        new(item ?? Guid.NewGuid(), user, status, seconds, Today.AddDays(-daysAgo).AddHours(10));

    private static TesterWorkload Of(WorkloadReport report, Guid? user) => report.Testers.Single(t => t.UserId == user);

    // ---- workload

    [Fact]
    public void Items_Belong_To_Who_They_Are_Given_To_And_Attempts_To_Who_Made_Them()
    {
        var shared = Guid.NewGuid();
        var data = new WorkloadScopeData
        {
            Items =
            {
                Item(Ann, TestResultStatus.Passed), Item(Ann, TestResultStatus.Failed), Item(Ann, TestResultStatus.Untested), Item(Bob, TestResultStatus.Blocked, shared),
                Item(null, TestResultStatus.Untested), Item(null, TestResultStatus.Passed),
            },
            // Bob ran an item that is given to Ann: the item stays hers, the attempt is his.
            Attempts = { Attempt(Bob, TestResultStatus.Passed, shared), Attempt(Bob, TestResultStatus.Failed, shared) },
        };

        var report = WorkloadCalculator.Calculate(data, 14);

        var ann = Of(report, Ann);
        (ann.AssignedItems, ann.AssignedDone, ann.AssignedPassed, ann.AssignedFailed).ShouldBe((3, 2, 1, 1));
        ann.Attempts.ShouldBe(0);
        var bob = Of(report, Bob);
        (bob.AssignedItems, bob.AssignedDone, bob.Attempts, bob.ItemsExecuted).ShouldBe((1, 1, 2, 1));
        (report.UnassignedItems, report.UnassignedUntested, report.TotalItems, report.TotalAttempts).ShouldBe((2, 1, 6, 2));
    }

    [Fact]
    public void The_Attempts_Of_A_Person_Are_Counted_By_Result_Time_And_Days_Worked()
    {
        var one = Guid.NewGuid();
        var two = Guid.NewGuid();
        var data = new WorkloadScopeData
        {
            Attempts =
            {
                Attempt(Ann, TestResultStatus.Passed, one, 30, 0), Attempt(Ann, TestResultStatus.Passed, two, 60, 0),
                Attempt(Ann, TestResultStatus.Failed, one, 90, 2), Attempt(Ann, TestResultStatus.Skipped, two, 0, 2),
                Attempt(Ann, TestResultStatus.Blocked, two, 10, 5),
            },
        };

        var ann = Of(WorkloadCalculator.Calculate(data, 10), Ann);

        (ann.Attempts, ann.Passed, ann.Failed, ann.Blocked, ann.Skipped).ShouldBe((5, 2, 1, 1, 1));
        ann.ItemsExecuted.ShouldBe(2, "the same item attempted again is one item");
        ann.DurationSeconds.ShouldBe(190);
        ann.ActiveDays.ShouldBe(3);
        ann.AttemptsPerDay.ShouldBe(0.5m);
        ann.PassRate.ShouldBe(50m, "2 passed of the 4 attempts that were not skipped");
    }

    [Fact]
    public void Attempts_Whose_Creator_Is_Not_Known_Are_A_Row_Of_Their_Own_And_Never_Have_Assigned_Items()
    {
        var data = new WorkloadScopeData
        {
            Items = { Item(Ann, TestResultStatus.Untested) },
            Attempts = { Attempt(null, TestResultStatus.Passed), Attempt(null, TestResultStatus.Passed), Attempt(Ann, TestResultStatus.Failed) },
        };

        var report = WorkloadCalculator.Calculate(data, 7);

        report.Testers.Select(t => (t.UserId, t.Attempts)).ShouldBe(new (Guid?, int)[] { (null, 2), (Ann, 1) });
        Of(report, null).AssignedItems.ShouldBe(0);
    }

    [Fact]
    public void The_Busiest_Comes_First_Then_The_One_With_More_Items_And_A_Person_With_Nothing_To_Count_Has_No_Pass_Rate()
    {
        var data = new WorkloadScopeData
        {
            Items = { Item(Bob, TestResultStatus.Untested), Item(Bob, TestResultStatus.Untested), Item(Ann, TestResultStatus.Untested) },
            Attempts = { Attempt(Ann, TestResultStatus.Passed) },
        };

        var report = WorkloadCalculator.Calculate(data, 14);

        report.Testers.Select(t => t.UserId).ShouldBe(new Guid?[] { Ann, Bob });
        Of(report, Bob).PassRate.ShouldBeNull();
        Of(report, Bob).AttemptsPerDay.ShouldBe(0m);
    }

    [Fact]
    public void An_Empty_Scope_Has_Nobody_And_Nothing()
    {
        var report = WorkloadCalculator.Calculate(new WorkloadScopeData(), 14);

        report.Testers.ShouldBeEmpty();
        (report.UnassignedItems, report.TotalItems, report.TotalAttempts).ShouldBe((0, 0, 0));
    }

    // ---- trend

    private static PlanInsightData Plan(Guid id, int runs, params InsightItem[] items) => new() { PlanId = id, RunCount = runs, Items = items.ToList() };

    private static InsightItem Run(TestResultStatus status, bool executed = true, TestResultStatus? first = null) =>
        new(Guid.NewGuid(), Guid.NewGuid(), status, first ?? (executed ? status : null), executed ? Today : null);

    [Fact]
    public void Each_Plan_Is_Worked_Out_As_The_Dashboard_Works_Out_A_Plan()
    {
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();

        var figures = SprintTrendCalculator.Calculate(new[]
        {
            Plan(a, 2, Run(TestResultStatus.Passed), Run(TestResultStatus.Failed), Run(TestResultStatus.Skipped), Run(TestResultStatus.Untested, executed: false)),
            Plan(b, 1, Run(TestResultStatus.Passed, first: TestResultStatus.Failed)),
        });

        figures.Select(f => f.PlanId).ShouldBe(new[] { a, b });
        figures[0].RunCount.ShouldBe(2);
        figures[0].Progress.TotalItems.ShouldBe(4);
        figures[0].Progress.PassRate.ShouldBe(33.33m);   // 1 passed of the 3 items that are not skipped, rounded down
        figures[0].Progress.CompletionPercentage.ShouldBe(75m);
        figures[1].Progress.PassRate.ShouldBe(100m);
        figures[1].Progress.FirstTimePassRate.ShouldBe(0m);   // it failed the first time
    }

    [Fact]
    public void The_Change_Is_The_Last_Rate_Minus_The_One_Before_And_Is_Missing_When_Either_Is()
    {
        SprintTrendCalculator.Change(new decimal?[] { 60m, 80m, 87.5m }).ShouldBe(7.5m);
        SprintTrendCalculator.Change(new decimal?[] { 90m, 80m }).ShouldBe(-10m);
        SprintTrendCalculator.Change(new decimal?[] { 90m, null }).ShouldBeNull();
        SprintTrendCalculator.Change(new decimal?[] { null, 90m }).ShouldBeNull();
        SprintTrendCalculator.Change(new decimal?[] { 90m }).ShouldBeNull();
        SprintTrendCalculator.Change(Array.Empty<decimal?>()).ShouldBeNull();
    }
}
