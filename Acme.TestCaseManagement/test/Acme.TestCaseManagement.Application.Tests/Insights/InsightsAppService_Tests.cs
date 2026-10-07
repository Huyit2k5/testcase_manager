using Acme.TestCaseManagement.Automation;
using Acme.TestCaseManagement.Automation.Dtos;
using Acme.TestCaseManagement.Enums;
using Acme.TestCaseManagement.Insights.Dtos;
using Acme.TestCaseManagement.Plans;
using Acme.TestCaseManagement.Plans.Dtos;
using Acme.TestCaseManagement.Runs.Dtos;
using Acme.TestCaseManagement.Suites;
using Acme.TestCaseManagement.Suites.Dtos;
using Acme.TestCaseManagement.TestCases;
using Acme.TestCaseManagement.TestCases.Dtos;
using Shouldly;
using Volo.Abp.Domain.Repositories;
using Xunit;

namespace Acme.TestCaseManagement.Insights;

/// <summary>Flaky detection and the dashboard on real history, written the way a pipeline writes it.</summary>
public class InsightsAppService_Tests : TestCaseManagementApplicationTestBase
{
    private readonly ITestSuiteAppService _suites;
    private readonly ITestCaseAppService _testCases;
    private readonly IAutomationResultsAppService _publisher;
    private readonly ITestPlanAppService _plans;
    private readonly IFlakyTestAppService _flaky;
    private readonly IDashboardAppService _dashboard;
    private Guid? _suiteId;

    public InsightsAppService_Tests()
    {
        _suites = GetRequiredService<ITestSuiteAppService>();
        _testCases = GetRequiredService<ITestCaseAppService>();
        _publisher = GetRequiredService<IAutomationResultsAppService>();
        _plans = GetRequiredService<ITestPlanAppService>();
        _flaky = GetRequiredService<IFlakyTestAppService>();
        _dashboard = GetRequiredService<IDashboardAppService>();
    }

    private async Task<TestCaseDto> CaseAsync(string code, string automationId)
    {
        _suiteId ??= (await _suites.CreateAsync(new CreateTestSuiteDto { Name = "Automated" })).Id;
        var created = await _testCases.CreateAsync(new CreateUpdateTestCaseDto
        {
            SuiteId = _suiteId.Value,
            Code = code,
            Title = $"Title of {code}",
            AutomationId = automationId,
            Steps = { new TestStepDto { Action = "Run the script", ExpectedResult = "It passes" } },
        });

        return await _testCases.ChangeStatusAsync(created.Id, new ChangeTestCaseStatusDto { TargetStatus = TestCaseStatus.Approved });
    }

    /// <summary>One run per letter (P = Passed, F = Failed), each with one result for the test.</summary>
    private async Task HistoryAsync(string automationId, string pattern, Guid? planId = null, string? defectKey = null)
    {
        foreach (var letter in pattern)
        {
            var result = new AutomationResultInput
            {
                AutomationId = automationId,
                Status = letter == 'P' ? TestResultStatus.Passed : TestResultStatus.Failed,
                DurationSeconds = 3,
            };
            if (letter == 'F' && defectKey != null)
            {
                result.Defects.Add(new AddDefectLinkDto { ExternalSystem = "Jira", IssueKey = defectKey });
            }

            await _publisher.PublishAsync(new PublishAutomationResultsInput
            {
                Run = new AutomationRunInput { Title = "CI", Environment = "Staging", TestPlanId = planId },
                Results = { result },
            });
        }
    }

    // ---- flaky tests

    [Fact]
    public async Task Tests_That_Flip_Are_Scored_Flaky_And_Steady_Ones_Are_Not()
    {
        await CaseAsync("TC-FLIP", "e2e.flip");
        await CaseAsync("TC-STEADY", "e2e.steady");
        await CaseAsync("TC-BROKEN", "e2e.broken");
        await CaseAsync("TC-NEW", "e2e.new");
        await HistoryAsync("e2e.flip", "PFPFPFPFPF");
        await HistoryAsync("e2e.steady", "PPPPPPPPPP");
        await HistoryAsync("e2e.broken", "PPPPPFFFFF");
        await HistoryAsync("e2e.new", "PFP");

        var all = await _flaky.GetListAsync(new GetFlakyTestsInput { MinimumLevel = FlakinessLevel.Insufficient });

        // Most unstable first: the level, then the score, then the number of outcomes.
        all.Items.Select(i => (i.Code, i.Level)).ShouldBe(new[]
        {
            ("TC-FLIP", FlakinessLevel.Flaky),
            ("TC-BROKEN", FlakinessLevel.Stable),
            ("TC-STEADY", FlakinessLevel.Stable),
            ("TC-NEW", FlakinessLevel.Insufficient),
        });

        var flip = all.Items.Single(i => i.Code == "TC-FLIP");
        flip.Observations.ShouldBe(10);
        flip.Flips.ShouldBe(9);
        flip.Score.ShouldBe(1m);
        flip.Passes.ShouldBe(5);
        flip.Failures.ShouldBe(5);
        flip.IsFlagged.ShouldBeFalse();
        flip.LastFailedAt.ShouldNotBeNull();

        all.FlakyCount.ShouldBe(1);
        all.Settings.WindowSize.ShouldBe(20);
        all.Settings.FlakyScore.ShouldBe(0.30m);
    }

    [Fact]
    public async Task The_List_Shows_Watch_And_Flaky_By_Default_Filters_And_Is_Cut_At_The_Maximum()
    {
        await CaseAsync("TC-A", "e2e.a");
        await CaseAsync("TC-B", "e2e.b");
        await CaseAsync("TC-C", "e2e.c");
        await HistoryAsync("e2e.a", "PFPFPFPF");
        await HistoryAsync("e2e.b", "PPPPPFPPPPPPPPPFPPPP");   // 4 changes: watch
        await HistoryAsync("e2e.c", "PPPPPPPPPP");

        var byDefault = await _flaky.GetListAsync(new GetFlakyTestsInput());
        byDefault.Items.Select(i => i.Code).ShouldBe(new[] { "TC-A", "TC-B" });
        byDefault.Items[1].Level.ShouldBe(FlakinessLevel.Watch);
        byDefault.WatchCount.ShouldBe(1);

        (await _flaky.GetListAsync(new GetFlakyTestsInput { MinimumLevel = FlakinessLevel.Flaky })).Items.Select(i => i.Code).ShouldBe(new[] { "TC-A" });
        (await _flaky.GetListAsync(new GetFlakyTestsInput { Filter = "e2e.b" })).Items.Select(i => i.Code).ShouldBe(new[] { "TC-B" });

        var cut = await _flaky.GetListAsync(new GetFlakyTestsInput { MaxResultCount = 1 });
        cut.Items.Count.ShouldBe(1);
        cut.TotalCount.ShouldBe(2);
    }

    [Fact]
    public async Task Apply_Flags_The_Flaky_Tests_And_Only_Clears_Recovered_Ones_When_Asked()
    {
        var flaky = await CaseAsync("TC-FLAKY", "e2e.flaky");
        var recovered = await CaseAsync("TC-RECOVERED", "e2e.recovered");
        await HistoryAsync("e2e.flaky", "PFPFPFPFPF");
        await HistoryAsync("e2e.recovered", "PPPPPPPPPP");

        // A runner flagged the recovered test long ago.
        var repository = GetRequiredService<IRepository<TestCase, Guid>>();
        await WithUnitOfWorkAsync(async () =>
        {
            var entity = await repository.GetAsync(recovered.Id);
            entity.SetFlaky(true);
            await repository.UpdateAsync(entity);
        });

        var first = await _flaky.ApplyAsync(new ApplyFlakyFlagsInput());
        first.FlaggedCodes.ShouldBe(new[] { "TC-FLAKY" });
        first.Cleared.ShouldBe(0);
        (await _testCases.GetAsync(flaky.Id)).IsFlaky.ShouldBeTrue();
        (await _testCases.GetAsync(recovered.Id)).IsFlaky.ShouldBeTrue();

        var again = await _flaky.ApplyAsync(new ApplyFlakyFlagsInput());
        again.Flagged.ShouldBe(0);

        var clear = await _flaky.ApplyAsync(new ApplyFlakyFlagsInput { ClearRecovered = true });
        clear.ClearedCodes.ShouldBe(new[] { "TC-RECOVERED" });
        (await _testCases.GetAsync(recovered.Id)).IsFlaky.ShouldBeFalse();
        (await _testCases.GetAsync(flaky.Id)).IsFlaky.ShouldBeTrue();
    }

    [Fact]
    public async Task A_Deleted_Test_Case_Drops_Out_Of_The_List()
    {
        var gone = await CaseAsync("TC-GONE", "e2e.gone");
        await HistoryAsync("e2e.gone", "PFPFPFPF");
        (await _flaky.GetListAsync(new GetFlakyTestsInput())).Items.Count.ShouldBe(1);

        await _testCases.DeleteAsync(gone.Id);

        (await _flaky.GetListAsync(new GetFlakyTestsInput())).Items.ShouldBeEmpty();
    }

    // ---- dashboard

    [Fact]
    public async Task The_Dashboard_Of_An_Empty_Library_Has_No_Rates_And_A_Full_Calendar()
    {
        var dashboard = await _dashboard.GetAsync(new GetDashboardInput { Days = 7 });

        dashboard.RunCount.ShouldBe(0);
        dashboard.Progress.PassRate.ShouldBeNull();
        dashboard.Velocity.Points.Count.ShouldBe(7);
        dashboard.BurnDown.Points.ShouldNotBeEmpty();
        dashboard.BurnDown.OnTrack.ShouldBeNull();
        dashboard.DefectDensity.DefectsPer100Executed.ShouldBeNull();
        dashboard.Flaky.Scored.ShouldBe(0);
    }

    [Fact]
    public async Task The_Dashboard_Adds_Up_The_History_Of_All_Runs_Including_Runs_Without_A_Plan()
    {
        await CaseAsync("TC-1", "e2e.one");
        await CaseAsync("TC-2", "e2e.two");
        await HistoryAsync("e2e.one", "PFPFPF", defectKey: "BUG-1");
        await HistoryAsync("e2e.two", "P");

        var dashboard = await _dashboard.GetAsync(new GetDashboardInput { Days = 14 });

        dashboard.RunCount.ShouldBe(7);
        dashboard.Progress.TotalItems.ShouldBe(7);
        dashboard.Progress.Passed.ShouldBe(4);
        dashboard.Progress.Failed.ShouldBe(3);
        dashboard.Progress.PassRate.ShouldBe(57.14m);
        dashboard.Progress.CompletionPercentage.ShouldBe(100m);

        dashboard.Velocity.TotalAttempts.ShouldBe(7);
        dashboard.Velocity.Points[^1].Attempts.ShouldBe(7);
        dashboard.Velocity.Points[^1].ItemsCompleted.ShouldBe(7);

        dashboard.BurnDown.TotalItems.ShouldBe(7);
        dashboard.BurnDown.RemainingNow.ShouldBe(0);
        dashboard.BurnDown.ProjectedFinish.ShouldBeNull();

        dashboard.DefectDensity.Defects.ShouldBe(1);
        dashboard.DefectDensity.OpenDefects.ShouldBe(1);
        dashboard.DefectDensity.ExecutedTests.ShouldBe(2);
        dashboard.DefectDensity.DefectsPer100Executed.ShouldBe(50m);

        dashboard.Flaky.Scored.ShouldBe(1);
        dashboard.Flaky.Flaky.ShouldBe(1);
    }

    [Fact]
    public async Task A_Plan_Narrows_The_Dashboard_To_Its_Own_Runs_And_Burns_Down_Over_Its_Dates()
    {
        await CaseAsync("TC-1", "e2e.one");
        var plan = await _plans.CreateAsync(new CreateTestPlanDto
        {
            Name = "Release 1",
            StartDate = DateTime.Today.AddDays(-2),
            EndDate = DateTime.Today.AddDays(4),
        });
        await HistoryAsync("e2e.one", "PP", plan.Id);
        await HistoryAsync("e2e.one", "FFF");   // runs of no plan

        var all = await _dashboard.GetAsync(new GetDashboardInput());
        var scoped = await _dashboard.GetAsync(new GetDashboardInput { TestPlanId = plan.Id });

        all.RunCount.ShouldBe(5);
        scoped.RunCount.ShouldBe(2);
        scoped.TestPlanName.ShouldBe("Release 1");
        scoped.Progress.Passed.ShouldBe(2);
        scoped.Progress.Failed.ShouldBe(0);
        scoped.BurnDown.Start.ShouldBe(DateTime.Today.AddDays(-2));
        scoped.BurnDown.End.ShouldBe(DateTime.Today.AddDays(4));
        scoped.BurnDown.Points.Count.ShouldBe(7);
        scoped.BurnDown.RemainingAtStart.ShouldBe(2);
        scoped.BurnDown.RemainingNow.ShouldBe(0);
    }

    [Fact]
    public async Task A_Plan_That_Does_Not_Exist_Is_Refused()
    {
        await Should.ThrowAsync<Volo.Abp.Domain.Entities.EntityNotFoundException>(
            () => _dashboard.GetAsync(new GetDashboardInput { TestPlanId = Guid.NewGuid() }));
    }
}
