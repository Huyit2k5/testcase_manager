using Acme.TestCaseManagement.Enums;
using Acme.TestCaseManagement.Insights.Dtos;
using Acme.TestCaseManagement.Plans;
using Acme.TestCaseManagement.Plans.Dtos;
using Acme.TestCaseManagement.Projects;
using Acme.TestCaseManagement.Runs;
using Acme.TestCaseManagement.Runs.Dtos;
using Acme.TestCaseManagement.Suites;
using Acme.TestCaseManagement.Suites.Dtos;
using Acme.TestCaseManagement.TestCases;
using Acme.TestCaseManagement.TestCases.Dtos;
using Shouldly;
using Volo.Abp.Domain.Entities;
using Xunit;

namespace Acme.TestCaseManagement.Insights;

/// <summary>The trend over test plans and the workload of the testers, on plans, runs and attempts made by different people.</summary>
public class TrendAndWorkload_Tests : TestCaseManagementApplicationTestBase
{
    private static readonly Guid Ann = Guid.Parse("00000000-0000-0000-0000-00000000000a");
    private static readonly Guid Bob = Guid.Parse("00000000-0000-0000-0000-00000000000b");

    private readonly IProjectAppService _projects;
    private readonly ITestSuiteAppService _suites;
    private readonly ITestCaseAppService _testCases;
    private readonly ITestPlanAppService _plans;
    private readonly ITestRunAppService _runs;
    private readonly IDashboardAppService _dashboard;
    private int _codes;

    public TrendAndWorkload_Tests()
    {
        _projects = GetRequiredService<IProjectAppService>();
        _suites = GetRequiredService<ITestSuiteAppService>();
        _testCases = GetRequiredService<ITestCaseAppService>();
        _plans = GetRequiredService<ITestPlanAppService>();
        _runs = GetRequiredService<ITestRunAppService>();
        _dashboard = GetRequiredService<IDashboardAppService>();
    }

    private async Task<Guid> NewProjectAsync(string key)
    {
        var project = await _projects.CreateAsync(new Acme.TestCaseManagement.Projects.CreateProjectDto { Key = key, Name = $"Project {key}" });
        return project.Id;
    }

    private async Task<List<Guid>> CasesAsync(Guid projectId, int count)
    {
        var suite = await _suites.CreateAsync(new CreateTestSuiteDto { Name = $"Suite {++_codes}", ProjectId = projectId });
        var ids = new List<Guid>();
        for (var i = 0; i < count; i++)
        {
            var created = await _testCases.CreateAsync(new CreateUpdateTestCaseDto
            {
                SuiteId = suite.Id, Code = $"TW-{++_codes}", Title = "A test", Steps = { new TestStepDto { Action = "Do", ExpectedResult = "Done" } },
            });
            ids.Add((await _testCases.ChangeStatusAsync(created.Id, new ChangeTestCaseStatusDto { TargetStatus = TestCaseStatus.Approved })).Id);
        }

        return ids;
    }

    private async Task<TestPlanDto> PlanAsync(Guid projectId, string name, DateTime? start)
    {
        return await _plans.CreateAsync(new CreateTestPlanDto { ProjectId = projectId, Name = name, StartDate = start, EndDate = start?.AddDays(10) });
    }

    /// <summary>A run of the plan with the results given (null: not run); every result is recorded by <paramref name="by"/>.</summary>
    private async Task<TestRunDto> RunAsync(TestPlanDto plan, Guid projectId, Guid by, params TestResultStatus?[] results)
    {
        var cases = await CasesAsync(projectId, results.Length);
        var run = await _runs.CreateAsync(new CreateTestRunDto { TestPlanId = plan.Id, Title = $"Run of {plan.Name}", Environment = "Staging", TestCaseIds = cases });
        using (ChangeUser(by, "tester"))
        {
            for (var i = 0; i < results.Length; i++)
            {
                if (results[i] is { } status)
                {
                    await _runs.ExecuteItemAsync(run.Id, run.Items[i].Id, new ExecuteTestItemDto { Status = status, DurationSeconds = 30 });
                }
            }
        }

        return await _runs.GetAsync(run.Id);
    }

    // ---- trend

    [Fact]
    public async Task The_Plans_Are_Shown_Oldest_First_By_Their_Start_Date_With_Their_Own_Figures_And_The_Change_Between_The_Last_Two()
    {
        var project = await NewProjectAsync("TRD");
        // Created in the order newest, oldest, middle: the start date decides, not the order of creation.
        var third = await PlanAsync(project, "Sprint 3", new DateTime(2026, 3, 1));
        var first = await PlanAsync(project, "Sprint 1", new DateTime(2026, 1, 1));
        var second = await PlanAsync(project, "Sprint 2", new DateTime(2026, 2, 1));
        await RunAsync(first, project, Ann, TestResultStatus.Passed, TestResultStatus.Failed, TestResultStatus.Failed, TestResultStatus.Failed);    // 25
        await RunAsync(second, project, Ann, TestResultStatus.Passed, TestResultStatus.Passed, TestResultStatus.Failed, TestResultStatus.Failed);   // 50
        await RunAsync(third, project, Ann, TestResultStatus.Passed, TestResultStatus.Passed, TestResultStatus.Passed, TestResultStatus.Failed);    // 75

        var trend = await _dashboard.GetTrendAsync(new GetSprintTrendInput { ProjectId = project });

        trend.Points.Select(p => p.PlanName).ShouldBe(new[] { "Sprint 1", "Sprint 2", "Sprint 3" });
        trend.Points.Select(p => p.PassRate).ShouldBe(new decimal?[] { 25m, 50m, 75m });
        trend.Points.Select(p => p.TotalItems).ShouldBe(new[] { 4, 4, 4 });
        trend.Points[2].RunCount.ShouldBe(1);
        (trend.Points[2].Passed, trend.Points[2].Failed, trend.Points[2].CompletionPercentage).ShouldBe((3, 1, 100m));
        trend.PassRateChange.ShouldBe(25m);
        trend.AveragePassRate.ShouldBe(50m);
    }

    [Fact]
    public async Task Only_The_Latest_Plans_Are_Shown_And_A_Plan_Without_Run_Items_Is_Left_Out()
    {
        var project = await NewProjectAsync("LAT");
        for (var i = 1; i <= 4; i++)
        {
            await RunAsync(await PlanAsync(project, $"Sprint {i}", new DateTime(2026, i, 1)), project, Ann, TestResultStatus.Passed);
        }

        await PlanAsync(project, "Sprint 5 not started", new DateTime(2026, 5, 1));   // the latest, but nothing in it

        var trend = await _dashboard.GetTrendAsync(new GetSprintTrendInput { ProjectId = project, Sprints = 3 });

        trend.Points.Select(p => p.PlanName).ShouldBe(new[] { "Sprint 2", "Sprint 3", "Sprint 4" });
    }

    [Fact]
    public async Task The_Defects_Of_A_Plan_Are_Counted_In_That_Plan_And_The_Change_Of_Their_Density_Is_Given()
    {
        var project = await NewProjectAsync("DEF");
        var one = await PlanAsync(project, "One", new DateTime(2026, 1, 1));
        var two = await PlanAsync(project, "Two", new DateTime(2026, 2, 1));
        var failed = await RunAsync(one, project, Ann, TestResultStatus.Passed, TestResultStatus.Failed);
        using (ChangeUser(Ann, "tester"))
        {
            var attempts = await _runs.GetExecutionsAsync(failed.Id, failed.Items[1].Id);
            await _runs.AddDefectLinkAsync(attempts.Single().Id, new AddDefectLinkDto { ExternalSystem = "Jira", IssueKey = "DEF-1" });
        }

        await RunAsync(two, project, Ann, TestResultStatus.Passed, TestResultStatus.Passed);

        var trend = await _dashboard.GetTrendAsync(new GetSprintTrendInput { ProjectId = project });

        (trend.Points[0].Defects, trend.Points[0].OpenDefects, trend.Points[0].DefectsPer100Executed).ShouldBe((1, 1, 50m));
        (trend.Points[1].Defects, trend.Points[1].DefectsPer100Executed).ShouldBe((0, 0m));
        trend.DefectDensityChange.ShouldBe(-50m);
    }

    [Fact]
    public async Task A_Project_Sees_Only_Its_Own_Plans_And_The_Number_Of_Plans_Is_Validated_At_The_Edge()
    {
        var mine = await NewProjectAsync("MIN");
        var other = await NewProjectAsync("OTH");
        await RunAsync(await PlanAsync(mine, "Mine", new DateTime(2026, 1, 1)), mine, Ann, TestResultStatus.Passed);
        await RunAsync(await PlanAsync(other, "Theirs", new DateTime(2026, 1, 1)), other, Ann, TestResultStatus.Failed);

        (await _dashboard.GetTrendAsync(new GetSprintTrendInput { ProjectId = mine })).Points.Select(p => p.PlanName).ShouldBe(new[] { "Mine" });
        (await _dashboard.GetTrendAsync(new GetSprintTrendInput { ProjectId = other })).Points.Single().PassRate.ShouldBe(0m);
        (await _dashboard.GetTrendAsync(new GetSprintTrendInput { ProjectId = Guid.NewGuid() })).Points.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_Project_Without_Plans_Has_An_Empty_Trend_With_No_Change()
    {
        var empty = await NewProjectAsync("EMP");

        var trend = await _dashboard.GetTrendAsync(new GetSprintTrendInput { ProjectId = empty });

        trend.Points.ShouldBeEmpty();
        (trend.PassRateChange, trend.AveragePassRate).ShouldBe((null, null));
    }

    // ---- workload

    [Fact]
    public async Task The_Workload_Shows_Who_Was_Given_What_And_Who_Made_The_Attempts()
    {
        var project = await NewProjectAsync("WRK");
        var plan = await PlanAsync(project, "Sprint", new DateTime(2026, 1, 1));
        var run = await RunAsync(plan, project, Ann, TestResultStatus.Passed, TestResultStatus.Failed, null, null);
        // Items 1 and 2 are given to Ann, item 3 to Bob; item 4 to nobody.
        await _runs.AssignTesterAsync(run.Id, run.Items[0].Id, new AssignTestRunItemDto { AssignedUserId = Ann });
        await _runs.AssignTesterAsync(run.Id, run.Items[1].Id, new AssignTestRunItemDto { AssignedUserId = Ann });
        await _runs.AssignTesterAsync(run.Id, run.Items[2].Id, new AssignTestRunItemDto { AssignedUserId = Bob });
        using (ChangeUser(Bob, "bob"))
        {
            await _runs.ExecuteItemAsync(run.Id, run.Items[2].Id, new ExecuteTestItemDto { Status = TestResultStatus.Blocked, DurationSeconds = 60 });
        }

        var workload = await _dashboard.GetWorkloadAsync(new GetWorkloadInput { ProjectId = project });

        workload.Testers.Select(t => t.UserId).ShouldBe(new Guid?[] { Ann, Bob });
        var ann = workload.Testers[0];
        (ann.AssignedItems, ann.AssignedDone, ann.AssignedPassed, ann.AssignedFailed).ShouldBe((2, 2, 1, 1));
        (ann.Attempts, ann.Passed, ann.Failed, ann.DurationSeconds, ann.ActiveDays).ShouldBe((2, 1, 1, 60, 1));
        ann.PassRate.ShouldBe(50m);
        var bob = workload.Testers[1];
        (bob.AssignedItems, bob.AssignedDone, bob.Attempts, bob.Blocked, bob.ItemsExecuted).ShouldBe((1, 1, 1, 1, 1));
        (workload.UnassignedItems, workload.UnassignedUntested, workload.TotalItems, workload.TotalAttempts).ShouldBe((1, 1, 4, 3));
        workload.Days.ShouldBe(14);
    }

    [Fact]
    public async Task The_Workload_Can_Be_Limited_To_A_Plan_And_Is_Empty_For_A_Project_With_No_Run()
    {
        var project = await NewProjectAsync("LIM");
        var one = await PlanAsync(project, "One", new DateTime(2026, 1, 1));
        var two = await PlanAsync(project, "Two", new DateTime(2026, 2, 1));
        await RunAsync(one, project, Ann, TestResultStatus.Passed, TestResultStatus.Passed);
        await RunAsync(two, project, Bob, TestResultStatus.Failed);

        var inOne = await _dashboard.GetWorkloadAsync(new GetWorkloadInput { ProjectId = project, TestPlanId = one.Id });
        var all = await _dashboard.GetWorkloadAsync(new GetWorkloadInput { ProjectId = project });

        inOne.TestPlanName.ShouldBe("One");
        inOne.Testers.Select(t => (t.UserId, t.Attempts)).ShouldBe(new (Guid?, int)[] { (Ann, 2) });
        all.Testers.Select(t => (t.UserId, t.Attempts)).ShouldBe(new (Guid?, int)[] { (Ann, 2), (Bob, 1) });
        (await _dashboard.GetWorkloadAsync(new GetWorkloadInput { ProjectId = await NewProjectAsync("NIL") })).Testers.ShouldBeEmpty();
        await Should.ThrowAsync<EntityNotFoundException>(() => _dashboard.GetWorkloadAsync(new GetWorkloadInput { TestPlanId = Guid.NewGuid() }));
    }
}
