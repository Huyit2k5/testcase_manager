using Acme.TestCaseManagement.Enums;
using Shouldly;
using Volo.Abp;
using Volo.Abp.Domain.Entities;
using Volo.Abp.Domain.Repositories;
using Xunit;

namespace Acme.TestCaseManagement.Quality;

public class QualityGateManager_Tests : QualityTestBase
{
    private readonly QualityGateManager _manager;
    private readonly IRepository<QualityGate, Guid> _gateRepository;

    public QualityGateManager_Tests()
    {
        _manager = GetRequiredService<QualityGateManager>();
        _gateRepository = GetRequiredService<IRepository<QualityGate, Guid>>();
    }

    private async Task<QualityGate> CreateGateAsync(
        string name, decimal minPassRate = 95m, int requiredApprovals = 2, bool isDefault = false)
    {
        var gate = await _manager.CreateAsync(name, minPassRate, requiredApprovals, null, isDefault);
        return await _gateRepository.InsertAsync(gate, autoSave: true);
    }

    private static GateCriterionResult Criterion(QualityGateEvaluation evaluation, string code) =>
        evaluation.Criteria.Single(c => c.Code == code);

    // ---- gate configuration -----------------------------------------------------------------------------

    [Fact]
    public async Task Create_Should_Reject_A_Duplicate_Name_Ignoring_Case()
    {
        await WithUnitOfWorkAsync(async () =>
        {
            await CreateGateAsync("Release");

            (await Should.ThrowAsync<BusinessException>(() => _manager.CreateAsync("release", 90m, 1, null, false)))
                .Code.ShouldBe(TestCaseManagementErrorCodes.DuplicateQualityGateName);
        });
    }

    [Theory]
    [InlineData(0, 2)]
    [InlineData(-5, 2)]
    [InlineData(100.01, 2)]
    [InlineData(95, 0)]
    [InlineData(95, 11)]
    public async Task Create_Should_Reject_Thresholds_That_Would_Disable_Or_Break_The_Gate(double minPassRate, int approvals)
    {
        await WithUnitOfWorkAsync(async () =>
        {
            await Should.ThrowAsync<ArgumentOutOfRangeException>(
                () => _manager.CreateAsync("Bad", (decimal)minPassRate, approvals, null, false));
        });
    }

    [Fact]
    public async Task Only_One_Gate_Can_Be_The_Default()
    {
        await WithUnitOfWorkAsync(async () =>
        {
            var first = await CreateGateAsync("First", isDefault: true);
            var second = await CreateGateAsync("Second", isDefault: true);

            (await _gateRepository.GetAsync(first.Id)).IsDefault.ShouldBeFalse();
            (await _gateRepository.GetAsync(second.Id)).IsDefault.ShouldBeTrue();

            // Promoting the first one through an update moves the flag back.
            await _manager.UpdateAsync(first, "First", 95m, 2, null, isDefault: true);
            await _gateRepository.UpdateAsync(first, autoSave: true);

            (await _gateRepository.GetListAsync(x => x.IsDefault)).Select(g => g.Name).ShouldBe(new[] { "First" });
        });
    }

    [Fact]
    public async Task Update_Should_Change_The_Fields_And_Reject_Another_Gates_Name()
    {
        await WithUnitOfWorkAsync(async () =>
        {
            var first = await CreateGateAsync("First");
            await CreateGateAsync("Second");

            await _manager.UpdateAsync(first, " Renamed ", 90m, 3, "text", isDefault: false);

            first.Name.ShouldBe("Renamed");
            first.MinPassRate.ShouldBe(90m);
            first.RequiredApprovals.ShouldBe(3);
            first.Description.ShouldBe("text");

            (await Should.ThrowAsync<BusinessException>(() => _manager.UpdateAsync(first, "second", 90m, 3, null, false)))
                .Code.ShouldBe(TestCaseManagementErrorCodes.DuplicateQualityGateName);
        });
    }

    [Fact]
    public async Task ResolveSettings_Should_Prefer_The_Named_Gate_Then_The_Default_Then_The_Baseline()
    {
        await WithUnitOfWorkAsync(async () =>
        {
            var baseline = await _manager.ResolveSettingsAsync(null);
            baseline.ShouldBe(QualityGateSettings.Baseline);
            baseline.IsBuiltIn.ShouldBeTrue();
            baseline.MinPassRate.ShouldBe(95m);
            baseline.RequiredApprovals.ShouldBe(2);

            var named = await CreateGateAsync("Hotfix", 80m, 1);
            await CreateGateAsync("Release", 97m, 2, isDefault: true);

            var byDefault = await _manager.ResolveSettingsAsync(null);
            (byDefault.Name, byDefault.MinPassRate, byDefault.IsBuiltIn).ShouldBe(("Release", 97m, false));

            var byId = await _manager.ResolveSettingsAsync(named.Id);
            (byId.Id, byId.Name, byId.MinPassRate, byId.RequiredApprovals).ShouldBe((named.Id, "Hotfix", 80m, 1));

            await Should.ThrowAsync<EntityNotFoundException>(() => _manager.ResolveSettingsAsync(Guid.NewGuid()));

            // Without a default gate the baseline applies again.
            var release = await _gateRepository.GetAsync(x => x.Name == "Release");
            await _gateRepository.DeleteAsync(release, autoSave: true);
            (await _manager.ResolveSettingsAsync(null)).IsBuiltIn.ShouldBeTrue();
        });
    }

    // ---- scope ------------------------------------------------------------------------------------------

    [Fact]
    public async Task Scope_Must_Name_Exactly_One_Existing_Plan_Or_A_Milestone_With_Plans()
    {
        await WithUnitOfWorkAsync(async () =>
        {
            var plan = await PlanAsync("Sprint 24");

            (await Should.ThrowAsync<BusinessException>(() => _manager.EvaluateAsync(new QualityGateScope(null, null), null)))
                .Code.ShouldBe(TestCaseManagementErrorCodes.InvalidSignOffScope);
            (await Should.ThrowAsync<BusinessException>(
                    () => _manager.EvaluateAsync(new QualityGateScope(plan.Id, Guid.NewGuid()), null)))
                .Code.ShouldBe(TestCaseManagementErrorCodes.InvalidSignOffScope);
            (await Should.ThrowAsync<BusinessException>(
                    () => _manager.EvaluateAsync(new QualityGateScope(Guid.NewGuid(), null), null)))
                .Code.ShouldBe(TestCaseManagementErrorCodes.TestPlanNotFound);
            (await Should.ThrowAsync<BusinessException>(
                    () => _manager.EvaluateAsync(new QualityGateScope(null, Guid.NewGuid()), null)))
                .Code.ShouldBe(TestCaseManagementErrorCodes.SignOffScopeEmpty);
        });
    }

    // ---- evaluation -------------------------------------------------------------------------------------

    [Fact]
    public async Task Evaluate_Should_Compute_The_Metrics_And_Criteria_Of_A_Plan()
    {
        await WithUnitOfWorkAsync(async () =>
        {
            var p1 = await ApprovedTestCaseAsync("TC-P1", PriorityLevel.Urgent);
            var b = await ApprovedTestCaseAsync("TC-B");
            var c = await ApprovedTestCaseAsync("TC-C");
            var d = await ApprovedTestCaseAsync("TC-D");
            var e = await ApprovedTestCaseAsync("TC-E");
            var (plan, _) = await ExecutedPlanAsync("Sprint 24",
                (p1, TestResultStatus.Passed),
                (b, TestResultStatus.Passed),
                (c, TestResultStatus.Passed),
                (d, TestResultStatus.Failed),
                (e, null)); // never executed

            var evaluation = await _manager.EvaluateAsync(new QualityGateScope(plan.Id, null), null);

            evaluation.Passed.ShouldBeFalse();
            evaluation.Gate.IsBuiltIn.ShouldBeTrue();
            evaluation.Scope.TestPlanId.ShouldBe(plan.Id);
            evaluation.Scope.Plans.Select(p => p.Name).ShouldBe(new[] { "Sprint 24" });

            var m = evaluation.Metrics;
            (m.RunCount, m.TotalItems, m.Passed, m.Failed, m.Untested).ShouldBe((1, 5, 3, 1, 1));
            m.PassRate.ShouldBe(60m);
            m.CompletionPercentage.ShouldBe(80m);
            (m.P1Total, m.P1Executed, m.P1ExecutionRate).ShouldBe((1, 1, 100m));

            Criterion(evaluation, QualityGateCriteria.PassRate).Passed.ShouldBeFalse();
            Criterion(evaluation, QualityGateCriteria.P1Executed).Passed.ShouldBeTrue();
            Criterion(evaluation, QualityGateCriteria.OpenCriticalDefects).Passed.ShouldBeTrue();
            Criterion(evaluation, QualityGateCriteria.OpenHighDefects).Passed.ShouldBeTrue();
        });
    }

    [Fact]
    public async Task A_Plan_At_The_Threshold_Passes_And_A_Blocked_P1_Test_Fails_The_Gate()
    {
        await WithUnitOfWorkAsync(async () =>
        {
            var cases = new List<(TestCases.TestCase, TestResultStatus?)>();
            for (var i = 1; i <= 19; i++)
            {
                cases.Add((await ApprovedTestCaseAsync($"TC-{i:00}"), TestResultStatus.Passed));
            }

            cases.Add((await ApprovedTestCaseAsync("TC-FAIL"), TestResultStatus.Failed));
            var (plan, _) = await ExecutedPlanAsync("At threshold", cases.ToArray());

            var atThreshold = await _manager.EvaluateAsync(new QualityGateScope(plan.Id, null), null);
            atThreshold.Metrics.PassRate.ShouldBe(95m);
            atThreshold.Passed.ShouldBeTrue(); // exactly 19 / 20

            var p1 = await ApprovedTestCaseAsync("TC-SMOKE", PriorityLevel.Urgent);
            var (blockedPlan, _) = await ExecutedPlanAsync("Blocked smoke", (p1, TestResultStatus.Blocked));
            var blocked = await _manager.EvaluateAsync(new QualityGateScope(blockedPlan.Id, null), null);

            Criterion(blocked, QualityGateCriteria.P1Executed).Passed.ShouldBeFalse();
            blocked.Passed.ShouldBeFalse();
        });
    }

    [Fact]
    public async Task Open_Defects_Are_Counted_Once_Per_Issue_And_Resolved_Ones_Are_Ignored()
    {
        await WithUnitOfWorkAsync(async () =>
        {
            var one = await ApprovedTestCaseAsync("TC-1");
            var two = await ApprovedTestCaseAsync("TC-2");
            var (plan, run) = await ExecutedPlanAsync("Sprint", (one, TestResultStatus.Failed), (two, TestResultStatus.Failed));
            var executions = (await GetRequiredService<IRepository<Runs.TestExecution, Guid>>().GetListAsync()).ToList();

            // The same Critical ticket from both failing tests, plus a High ticket and a Medium one.
            var critical1 = await DefectManager.AddAsync(executions[0], "Jira", "BUG-1", null, SeverityLevel.Critical);
            await DefectManager.AddAsync(executions[1], "jira", "bug-1", null, SeverityLevel.Critical);
            var high = await DefectManager.AddAsync(executions[0], "Jira", "BUG-2", null, SeverityLevel.High);
            await DefectManager.AddAsync(executions[1], "Jira", "BUG-3", null, SeverityLevel.Medium);

            var open = await _manager.EvaluateAsync(new QualityGateScope(plan.Id, null), null);
            (open.Metrics.OpenDefects.Critical, open.Metrics.OpenDefects.High, open.Metrics.OpenDefects.Medium)
                .ShouldBe((1, 1, 1));
            Criterion(open, QualityGateCriteria.OpenCriticalDefects).Actual.ShouldBe(1m);
            Criterion(open, QualityGateCriteria.OpenCriticalDefects).Passed.ShouldBeFalse();
            Criterion(open, QualityGateCriteria.OpenHighDefects).Passed.ShouldBeFalse();
            open.Metrics.OpenDefectIssues.Select(i => i.Severity).ShouldBe(
                new[] { SeverityLevel.Critical, SeverityLevel.High, SeverityLevel.Medium });

            // Resolving every link of BUG-1 and the High ticket clears both criteria; the Medium one stays as residual risk.
            await DefectManager.UpdateAsync(critical1, SeverityLevel.Critical, true);
            var secondLink = (await GetRequiredService<IRepository<DefectLink, Guid>>()
                .GetListAsync(x => x.IssueKey == "bug-1")).Single();
            await DefectManager.UpdateAsync(secondLink, SeverityLevel.Critical, true);
            await DefectManager.UpdateAsync(high, SeverityLevel.High, true);

            var resolved = await _manager.EvaluateAsync(new QualityGateScope(plan.Id, null), null);
            (resolved.Metrics.OpenDefects.Critical, resolved.Metrics.OpenDefects.High, resolved.Metrics.OpenDefects.Medium)
                .ShouldBe((0, 0, 1));
            Criterion(resolved, QualityGateCriteria.OpenCriticalDefects).Passed.ShouldBeTrue();
            Criterion(resolved, QualityGateCriteria.OpenHighDefects).Passed.ShouldBeTrue();
        });
    }

    [Fact]
    public async Task A_Milestone_Aggregates_All_Its_Plans_And_Other_Plans_Are_Not_Counted()
    {
        await WithUnitOfWorkAsync(async () =>
        {
            var milestone = Guid.NewGuid();
            var a = await ApprovedTestCaseAsync("TC-A");
            var b = await ApprovedTestCaseAsync("TC-B");
            var other = await ApprovedTestCaseAsync("TC-OTHER");

            var plan1 = await PlanAsync("Sprint 23", milestone);
            var run1 = await RunAsync(plan1, "Staging", a);
            await ExecuteAsync(run1, 1, TestResultStatus.Passed);

            var plan2 = await PlanAsync("Sprint 24", milestone);
            var run2 = await RunAsync(plan2, "Staging", b);
            await ExecuteAsync(run2, 1, TestResultStatus.Failed);

            var outsider = await PlanAsync("Unrelated sprint"); // no milestone
            var outsiderRun = await RunAsync(outsider, "Staging", other);
            await ExecuteAsync(outsiderRun, 1, TestResultStatus.Failed);

            var evaluation = await _manager.EvaluateAsync(new QualityGateScope(null, milestone), null);

            evaluation.Scope.MilestoneId.ShouldBe(milestone);
            evaluation.Scope.Plans.Select(p => p.Name).ShouldBe(new[] { "Sprint 23", "Sprint 24" });
            (evaluation.Metrics.RunCount, evaluation.Metrics.TotalItems, evaluation.Metrics.Passed, evaluation.Metrics.Failed)
                .ShouldBe((2, 2, 1, 1));
            evaluation.Metrics.PassRate.ShouldBe(50m);
        });
    }

    [Fact]
    public async Task Deleting_A_Library_Test_Case_Does_Not_Remove_Its_Items_Or_Its_Priority_From_The_Scope()
    {
        await WithUnitOfWorkAsync(async () =>
        {
            var failingP1 = await ApprovedTestCaseAsync("TC-P1", PriorityLevel.Urgent);
            var passing = await ApprovedTestCaseAsync("TC-OK");
            var (plan, _) = await ExecutedPlanAsync("Sprint", (failingP1, TestResultStatus.Blocked), (passing, TestResultStatus.Passed));

            await TestCaseRepository.DeleteAsync(failingP1, autoSave: true);

            var evaluation = await _manager.EvaluateAsync(new QualityGateScope(plan.Id, null), null);

            evaluation.Metrics.TotalItems.ShouldBe(2); // the deleted test case's item still counts against the pass rate
            evaluation.Metrics.PassRate.ShouldBe(50m);
            evaluation.Metrics.P1Total.ShouldBe(1);   // and it is still a P1 test
            Criterion(evaluation, QualityGateCriteria.P1Executed).Passed.ShouldBeFalse();
        });
    }

    [Fact]
    public async Task Evaluation_Uses_The_Thresholds_Of_The_Chosen_Gate_And_The_Frozen_Settings()
    {
        await WithUnitOfWorkAsync(async () =>
        {
            var cases = new List<(TestCases.TestCase, TestResultStatus?)>();
            for (var i = 1; i <= 4; i++)
            {
                cases.Add((await ApprovedTestCaseAsync($"TC-{i}"), i == 4 ? TestResultStatus.Failed : TestResultStatus.Passed));
            }

            var (plan, _) = await ExecutedPlanAsync("Sprint", cases.ToArray()); // 75% passed
            var lenient = await CreateGateAsync("Lenient", 70m, 1);
            await CreateGateAsync("Strict", 99m, 2, isDefault: true);
            var scope = new QualityGateScope(plan.Id, null);

            (await _manager.EvaluateAsync(scope, lenient.Id)).Passed.ShouldBeTrue();
            (await _manager.EvaluateAsync(scope, null)).Passed.ShouldBeFalse(); // the default (strict) gate
            (await _manager.EvaluateWithSettingsAsync(scope, new QualityGateSettings(null, "Frozen", 75m, 1, false)))
                .Passed.ShouldBeTrue();
        });
    }
}
