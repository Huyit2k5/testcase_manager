using Acme.TestCaseManagement.Enums;
using Acme.TestCaseManagement.Plans;
using Acme.TestCaseManagement.Plans.Dtos;
using Acme.TestCaseManagement.Quality;
using Acme.TestCaseManagement.QualityGates;
using Acme.TestCaseManagement.QualityGates.Dtos;
using Acme.TestCaseManagement.Runs;
using Acme.TestCaseManagement.Runs.Dtos;
using Acme.TestCaseManagement.Suites;
using Acme.TestCaseManagement.Suites.Dtos;
using Acme.TestCaseManagement.TestCases;
using Acme.TestCaseManagement.TestCases.Dtos;
using Shouldly;
using Volo.Abp;
using Volo.Abp.Domain.Entities;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Validation;
using Xunit;

namespace Acme.TestCaseManagement;

public class QualityGateAppService_Tests : TestCaseManagementApplicationTestBase
{
    private readonly IQualityGateAppService _gates;
    private readonly ITestSuiteAppService _suites;
    private readonly ITestCaseAppService _testCases;
    private readonly ITestPlanAppService _plans;
    private readonly ITestRunAppService _runs;

    private Guid? _suiteId;

    public QualityGateAppService_Tests()
    {
        _gates = GetRequiredService<IQualityGateAppService>();
        _suites = GetRequiredService<ITestSuiteAppService>();
        _testCases = GetRequiredService<ITestCaseAppService>();
        _plans = GetRequiredService<ITestPlanAppService>();
        _runs = GetRequiredService<ITestRunAppService>();
    }

    private static CreateUpdateQualityGateDto Gate(
        string name, decimal minPassRate = 95m, int approvals = 2, bool isDefault = false) =>
        new() { Name = name, MinPassRate = minPassRate, RequiredApprovals = approvals, IsDefault = isDefault };

    /// <summary>Creates a plan with one run of <paramref name="results"/>.Count approved test cases and records their results.</summary>
    private async Task<TestPlanDto> PlanWithResultsAsync(
        string name, IReadOnlyList<TestResultStatus> results, Guid? milestoneId = null, PriorityLevel priority = PriorityLevel.Medium)
    {
        _suiteId ??= (await _suites.CreateAsync(new CreateTestSuiteDto { Name = "Library" })).Id;

        var plan = await _plans.CreateAsync(new CreateTestPlanDto { Name = name, MilestoneId = milestoneId });
        var ids = new List<Guid>();
        for (var i = 1; i <= results.Count; i++)
        {
            var created = await _testCases.CreateAsync(new CreateUpdateTestCaseDto
            {
                SuiteId = _suiteId.Value,
                Code = $"{name}-{i:000}",
                Title = $"Test {i}",
                Priority = priority,
                Steps = { new TestStepDto { Action = "Do", ExpectedResult = "Done" } },
            });
            await _testCases.ChangeStatusAsync(created.Id, new ChangeTestCaseStatusDto { TargetStatus = TestCaseStatus.Approved });
            ids.Add(created.Id);
        }

        var run = await _runs.CreateAsync(new CreateTestRunDto
        {
            TestPlanId = plan.Id, Title = $"{name} run", Environment = "Staging", TestCaseIds = ids,
        });

        var batch = new BatchExecuteTestItemsDto();
        for (var i = 0; i < results.Count; i++)
        {
            if (results[i] != TestResultStatus.Untested)
            {
                batch.Items.Add(new BatchExecuteTestItemDto { TestRunItemId = run.Items[i].Id, Status = results[i] });
            }
        }

        if (batch.Items.Count > 0)
        {
            await _runs.BatchExecuteAsync(run.Id, batch);
        }

        return plan;
    }

    private static List<TestResultStatus> Results(int passed, int failed = 0, int untested = 0, int blocked = 0, int skipped = 0) =>
        Enumerable.Repeat(TestResultStatus.Passed, passed)
            .Concat(Enumerable.Repeat(TestResultStatus.Failed, failed))
            .Concat(Enumerable.Repeat(TestResultStatus.Blocked, blocked))
            .Concat(Enumerable.Repeat(TestResultStatus.Skipped, skipped))
            .Concat(Enumerable.Repeat(TestResultStatus.Untested, untested))
            .ToList();

    // ---- gate management --------------------------------------------------------------------------------

    [Fact]
    public async Task Create_Get_List_Update_And_Delete_Should_Work()
    {
        var release = await _gates.CreateAsync(new CreateUpdateQualityGateDto
        {
            Name = "Release", Description = "Production releases", MinPassRate = 97.5m, RequiredApprovals = 3,
        });
        var hotfix = await _gates.CreateAsync(Gate("Hotfix", 80m, 1));

        release.MinPassRate.ShouldBe(97.5m);
        release.RequiredApprovals.ShouldBe(3);
        release.IsDefault.ShouldBeFalse();
        (await _gates.GetAsync(release.Id)).Description.ShouldBe("Production releases");
        (await _gates.GetListAsync()).Select(g => g.Name).ShouldBe(new[] { "Hotfix", "Release" });

        var updated = await _gates.UpdateAsync(hotfix.Id, Gate("Hotfix 2", 85m, 2));
        (updated.Name, updated.MinPassRate, updated.RequiredApprovals).ShouldBe(("Hotfix 2", 85m, 2));

        await _gates.DeleteAsync(hotfix.Id);
        await Should.ThrowAsync<EntityNotFoundException>(() => _gates.GetAsync(hotfix.Id));
        (await _gates.GetListAsync()).Select(g => g.Name).ShouldBe(new[] { "Release" });
    }

    [Fact]
    public async Task Names_Must_Be_Unique_And_Thresholds_Must_Be_In_Range()
    {
        await _gates.CreateAsync(Gate("Release"));

        (await Should.ThrowAsync<BusinessException>(() => _gates.CreateAsync(Gate("RELEASE"))))
            .Code.ShouldBe(TestCaseManagementErrorCodes.DuplicateQualityGateName);

        // The gate cannot be switched off by a zero threshold or by requiring no approval at all.
        await Should.ThrowAsync<AbpValidationException>(() => _gates.CreateAsync(Gate("Zero", 0m)));
        await Should.ThrowAsync<AbpValidationException>(() => _gates.CreateAsync(Gate("Over", 100.5m)));
        await Should.ThrowAsync<AbpValidationException>(() => _gates.CreateAsync(Gate("NoApprovals", 95m, 0)));
        await Should.ThrowAsync<AbpValidationException>(() => _gates.CreateAsync(Gate("TooMany", 95m, 11)));
        await Should.ThrowAsync<AbpValidationException>(() => _gates.CreateAsync(Gate("")));
    }

    [Fact]
    public async Task Making_A_Gate_The_Default_Moves_The_Flag()
    {
        var first = await _gates.CreateAsync(Gate("First", isDefault: true));
        var second = await _gates.CreateAsync(Gate("Second", isDefault: true));

        (await _gates.GetAsync(first.Id)).IsDefault.ShouldBeFalse();
        (await _gates.GetAsync(second.Id)).IsDefault.ShouldBeTrue();

        await _gates.UpdateAsync(first.Id, Gate("First", isDefault: true));
        (await _gates.GetListAsync()).Where(g => g.IsDefault).Select(g => g.Name).ShouldBe(new[] { "First" });
    }

    // ---- evaluation -------------------------------------------------------------------------------------

    [Fact]
    public async Task Evaluate_Should_Return_The_Breakdown_Of_Every_Criterion()
    {
        var plan = await PlanWithResultsAsync("Sprint 24", Results(passed: 18, failed: 2));

        var evaluation = await _gates.EvaluateAsync(new EvaluateQualityGateInput { TestPlanId = plan.Id });

        evaluation.Passed.ShouldBeFalse();
        evaluation.Gate.IsBuiltIn.ShouldBeTrue();
        evaluation.Gate.MinPassRate.ShouldBe(95m);
        evaluation.Scope.Plans.Single().Name.ShouldBe("Sprint 24");
        evaluation.Metrics.TotalItems.ShouldBe(20);
        evaluation.Metrics.PassRate.ShouldBe(90m);

        evaluation.Criteria.Select(c => c.Code).ShouldBe(new[]
        {
            QualityGateCriteria.PassRate, QualityGateCriteria.P1Executed,
            QualityGateCriteria.OpenCriticalDefects, QualityGateCriteria.OpenHighDefects,
        });
        var passRate = evaluation.Criteria[0];
        (passRate.Operator, passRate.Threshold, passRate.Actual, passRate.Passed).ShouldBe((">=", 95m, (decimal?)90m, false));
        evaluation.Criteria.Skip(1).ShouldAllBe(c => c.Passed);
        evaluation.Criteria.ShouldAllBe(c => c.Label.Length > 0 && !c.Label.StartsWith("QualityGate:"));
    }

    [Fact]
    public async Task Evaluate_Should_Use_The_Named_Gate_Then_The_Default_Then_The_Baseline()
    {
        var plan = await PlanWithResultsAsync("Sprint 24", Results(passed: 9, failed: 1)); // 90%
        var scope = new EvaluateQualityGateInput { TestPlanId = plan.Id };

        (await _gates.EvaluateAsync(scope)).Passed.ShouldBeFalse(); // baseline 95%

        var lenient = await _gates.CreateAsync(Gate("Lenient", 85m, 1));
        await _gates.CreateAsync(Gate("Strict", 99m, 2, isDefault: true));

        var byDefault = await _gates.EvaluateAsync(scope);
        (byDefault.Gate.Name, byDefault.Gate.IsBuiltIn, byDefault.Passed).ShouldBe(("Strict", false, false));

        scope.QualityGateId = lenient.Id;
        var named = await _gates.EvaluateAsync(scope);
        (named.Gate.Name, named.Passed).ShouldBe(("Lenient", true));
    }

    [Fact]
    public async Task The_Gate_Uses_The_Priority_The_Test_Case_Had_When_It_Was_Approved_Not_One_Edited_Later()
    {
        var plan = await PlanWithResultsAsync("Snap", Results(passed: 1, blocked: 1), priority: PriorityLevel.Urgent);
        var run = (await _runs.GetListAsync(new GetTestRunListInput { TestPlanId = plan.Id })).Items.Single();
        var item = (await _runs.GetAsync(run.Id)).Items.First();
        var testCase = await _testCases.GetAsync(item.TestCaseId);

        // Somebody lowers the priority in the library after the results were recorded.
        await _testCases.UpdateAsync(testCase.Id, new CreateUpdateTestCaseDto
        {
            SuiteId = testCase.SuiteId, Code = testCase.Code, Title = testCase.Title, Priority = PriorityLevel.Low,
            Steps = { new TestStepDto { Action = "Do", ExpectedResult = "Done" } },
        });

        var evaluation = await _gates.EvaluateAsync(new EvaluateQualityGateInput { TestPlanId = plan.Id });

        var p1 = evaluation.Criteria.Single(c => c.Code == QualityGateCriteria.P1Executed);
        p1.Actual.ShouldBe(50m);   // one of two P1 items is blocked, as it was when the results were recorded
    }

    [Fact]
    public async Task A_Version_Published_Before_The_Snapshot_Held_The_Priority_Falls_Back_To_The_Test_Case()
    {
        var plan = await PlanWithResultsAsync("Legacy", Results(passed: 1, blocked: 1), priority: PriorityLevel.Urgent);
        var run = (await _runs.GetListAsync(new GetTestRunListInput { TestPlanId = plan.Id })).Items.Single();
        var items = (await _runs.GetAsync(run.Id)).Items;

        // Make the versions look as they do in a database that existed before the snapshot: the five columns are empty.
        await WithUnitOfWorkAsync(async () =>
        {
            var versions = GetRequiredService<IRepository<TestCaseVersion, Guid>>();
            var testCaseIds = items.Select(i => i.TestCaseId).ToList();
            foreach (var version in await versions.GetListAsync(v => testCaseIds.Contains(v.TestCaseId)))
            {
                foreach (var name in new[] { nameof(TestCaseVersion.Priority), nameof(TestCaseVersion.Severity), nameof(TestCaseVersion.Kind), nameof(TestCaseVersion.Layer), nameof(TestCaseVersion.ExecutionType) })
                {
                    typeof(TestCaseVersion).GetProperty(name)!.SetValue(version, null);
                }

                await versions.UpdateAsync(version, autoSave: true);
            }
        });

        // The priority is read from the test case as it is now: Urgent, one of two P1 items blocked.
        var before = await _gates.EvaluateAsync(new EvaluateQualityGateInput { TestPlanId = plan.Id });
        before.Criteria.Single(c => c.Code == QualityGateCriteria.P1Executed).Actual.ShouldBe(50m);

        // And it follows the test case, which is how the gate behaved before versions held the priority.
        var testCase = await _testCases.GetAsync(items.First().TestCaseId);
        await _testCases.UpdateAsync(testCase.Id, new CreateUpdateTestCaseDto
        {
            SuiteId = testCase.SuiteId, Code = testCase.Code, Title = testCase.Title, Priority = PriorityLevel.Low,
            Steps = { new TestStepDto { Action = "Do", ExpectedResult = "Done" } },
        });
        var after = await _gates.EvaluateAsync(new EvaluateQualityGateInput { TestPlanId = plan.Id });
        after.Criteria.Single(c => c.Code == QualityGateCriteria.P1Executed).Actual.ShouldNotBe(50m);
    }

    [Fact]
    public async Task Evaluate_Should_Report_Open_Defects_P1_Execution_And_Residual_Risk()
    {
        var plan = await PlanWithResultsAsync(
            "Smoke", Results(passed: 3, failed: 1, blocked: 1), priority: PriorityLevel.Urgent);
        var run = (await _runs.GetListAsync(new GetTestRunListInput { TestPlanId = plan.Id })).Items.Single();
        var detail = await _runs.GetAsync(run.Id);
        var failedItem = detail.Items.Single(i => i.CurrentStatus == TestResultStatus.Failed);
        var failedAttempt = (await _runs.GetExecutionsAsync(run.Id, failedItem.Id)).Single();

        await _runs.AddDefectLinkAsync(failedAttempt.Id, new AddDefectLinkDto { ExternalSystem = "Jira", IssueKey = "BUG-1", Severity = SeverityLevel.Critical });
        await _runs.AddDefectLinkAsync(failedAttempt.Id, new AddDefectLinkDto { ExternalSystem = "Jira", IssueKey = "BUG-2", Severity = SeverityLevel.Low });

        var evaluation = await _gates.EvaluateAsync(new EvaluateQualityGateInput { TestPlanId = plan.Id });

        evaluation.Passed.ShouldBeFalse();
        var byCode = evaluation.Criteria.ToDictionary(c => c.Code);
        byCode[QualityGateCriteria.P1Executed].Passed.ShouldBeFalse(); // one blocked P1 test
        byCode[QualityGateCriteria.P1Executed].Actual.ShouldBe(80m);
        byCode[QualityGateCriteria.OpenCriticalDefects].Actual.ShouldBe(1m);
        byCode[QualityGateCriteria.OpenCriticalDefects].Passed.ShouldBeFalse();
        byCode[QualityGateCriteria.OpenHighDefects].Passed.ShouldBeTrue();
        evaluation.Metrics.OpenDefects.Total.ShouldBe(2);
        evaluation.Metrics.OpenDefectIssues.Select(i => (i.IssueKey, i.Severity)).ToList().ShouldBe(new List<(string, SeverityLevel)>
        {
            ("BUG-1", SeverityLevel.Critical), ("BUG-2", SeverityLevel.Low),
        });
    }

    [Fact]
    public async Task Evaluate_Should_Support_A_Milestone_And_Reject_A_Bad_Scope()
    {
        var milestone = Guid.NewGuid();
        await PlanWithResultsAsync("Sprint 23", Results(passed: 10), milestone);
        await PlanWithResultsAsync("Sprint 24", Results(passed: 9, failed: 1), milestone);

        var evaluation = await _gates.EvaluateAsync(new EvaluateQualityGateInput { MilestoneId = milestone });
        evaluation.Scope.Plans.Select(p => p.Name).ShouldBe(new[] { "Sprint 23", "Sprint 24" });
        evaluation.Metrics.TotalItems.ShouldBe(20);
        evaluation.Metrics.PassRate.ShouldBe(95m);
        evaluation.Passed.ShouldBeTrue();

        (await Should.ThrowAsync<BusinessException>(() => _gates.EvaluateAsync(new EvaluateQualityGateInput())))
            .Code.ShouldBe(TestCaseManagementErrorCodes.InvalidSignOffScope);
        (await Should.ThrowAsync<BusinessException>(() => _gates.EvaluateAsync(
                new EvaluateQualityGateInput { TestPlanId = Guid.NewGuid(), MilestoneId = milestone })))
            .Code.ShouldBe(TestCaseManagementErrorCodes.InvalidSignOffScope);
        (await Should.ThrowAsync<BusinessException>(() => _gates.EvaluateAsync(
                new EvaluateQualityGateInput { TestPlanId = Guid.NewGuid() })))
            .Code.ShouldBe(TestCaseManagementErrorCodes.TestPlanNotFound);
        (await Should.ThrowAsync<BusinessException>(() => _gates.EvaluateAsync(
                new EvaluateQualityGateInput { MilestoneId = Guid.NewGuid() })))
            .Code.ShouldBe(TestCaseManagementErrorCodes.SignOffScopeEmpty);
        await Should.ThrowAsync<EntityNotFoundException>(() => _gates.EvaluateAsync(
            new EvaluateQualityGateInput { MilestoneId = milestone, QualityGateId = Guid.NewGuid() }));
    }

    [Fact]
    public async Task A_Plan_Without_Executed_Tests_Does_Not_Pass()
    {
        var plan = await PlanWithResultsAsync("Not started", Results(passed: 0, untested: 5));

        var evaluation = await _gates.EvaluateAsync(new EvaluateQualityGateInput { TestPlanId = plan.Id });

        evaluation.Passed.ShouldBeFalse();
        evaluation.Metrics.PassRate.ShouldBe(0m);
        evaluation.Metrics.Untested.ShouldBe(5);

        var empty = await _plans.CreateAsync(new CreateTestPlanDto { Name = "Empty plan" });
        var emptyEvaluation = await _gates.EvaluateAsync(new EvaluateQualityGateInput { TestPlanId = empty.Id });
        emptyEvaluation.Passed.ShouldBeFalse();
        emptyEvaluation.Metrics.PassRate.ShouldBeNull();
        emptyEvaluation.Criteria[0].Actual.ShouldBeNull();
    }
}
