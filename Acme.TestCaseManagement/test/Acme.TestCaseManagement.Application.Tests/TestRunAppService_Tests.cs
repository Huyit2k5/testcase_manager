using Acme.TestCaseManagement.Enums;
using Acme.TestCaseManagement.Plans;
using Acme.TestCaseManagement.Plans.Dtos;
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
using Xunit;

namespace Acme.TestCaseManagement;

public class TestRunAppService_Tests : TestCaseManagementApplicationTestBase
{
    private readonly ITestSuiteAppService _suites;
    private readonly ITestCaseAppService _testCases;
    private readonly ITestPlanAppService _plans;
    private readonly ITestRunAppService _runs;

    public TestRunAppService_Tests()
    {
        _suites = GetRequiredService<ITestSuiteAppService>();
        _testCases = GetRequiredService<ITestCaseAppService>();
        _plans = GetRequiredService<ITestPlanAppService>();
        _runs = GetRequiredService<ITestRunAppService>();
    }

    private Guid? _suiteId;

    private async Task<TestCaseDto> CreateApprovedTestCaseAsync(string code)
    {
        _suiteId ??= (await _suites.CreateAsync(new CreateTestSuiteDto { Name = "Library" })).Id;

        var created = await _testCases.CreateAsync(new CreateUpdateTestCaseDto
        {
            SuiteId = _suiteId.Value,
            Code = code,
            Title = $"Title of {code}",
            Steps = { new TestStepDto { Action = "Do it", ExpectedResult = "Done" } },
        });

        return await _testCases.ChangeStatusAsync(
            created.Id, new ChangeTestCaseStatusDto { TargetStatus = TestCaseStatus.Approved });
    }

    private async Task<TestRunDto> CreateRunWithItemsAsync(int count)
    {
        var ids = new List<Guid>();
        for (var i = 1; i <= count; i++)
        {
            ids.Add((await CreateApprovedTestCaseAsync($"TC-{i:000}")).Id);
        }

        return await _runs.CreateAsync(new CreateTestRunDto
        {
            Title = "Staging Chrome",
            Environment = "Staging",
            TestCaseIds = ids,
        });
    }

    [Fact]
    public async Task Executing_An_Item_Twice_Should_Create_Two_Executions_And_Not_Mutate_The_Master_Test_Case()
    {
        var run = await CreateRunWithItemsAsync(1);
        var item = run.Items.Single();
        var before = await _testCases.GetAsync(item.TestCaseId);
        var versionsBefore = await _testCases.GetVersionsAsync(item.TestCaseId);
        var stampBefore = await GetConcurrencyStampAsync(item.TestCaseId);

        var first = await _runs.ExecuteItemAsync(run.Id, item.Id, new ExecuteTestItemDto
        {
            Status = TestResultStatus.Failed,
            ActualResult = "Button disabled",
            DurationSeconds = 45,
        });
        var second = await _runs.ExecuteItemAsync(run.Id, item.Id, new ExecuteTestItemDto
        {
            Status = TestResultStatus.Passed,
            ActualResult = "Button enabled after the fix",
            DurationSeconds = 30,
        });

        first.AttemptNumber.ShouldBe(1);
        second.AttemptNumber.ShouldBe(2);
        first.Id.ShouldNotBe(second.Id);

        var history = await _runs.GetExecutionsAsync(run.Id, item.Id);
        history.Select(x => x.AttemptNumber).ShouldBe(new[] { 1, 2 });
        history[0].Status.ShouldBe(TestResultStatus.Failed);
        history[0].ActualResult.ShouldBe("Button disabled");
        history[0].DurationSeconds.ShouldBe(45);
        history[1].Status.ShouldBe(TestResultStatus.Passed);

        // The master test case is untouched: same content, status, version and concurrency stamp.
        var after = await _testCases.GetAsync(item.TestCaseId);
        after.Status.ShouldBe(before.Status);
        after.CurrentVersion.ShouldBe(before.CurrentVersion);
        after.Title.ShouldBe(before.Title);
        after.LastModificationTime.ShouldBe(before.LastModificationTime);
        after.Steps.Select(s => (s.Id, s.Action, s.ExpectedResult)).ShouldBe(before.Steps.Select(s => (s.Id, s.Action, s.ExpectedResult)));
        (await _testCases.GetVersionsAsync(item.TestCaseId)).Count.ShouldBe(versionsBefore.Count);
        (await GetConcurrencyStampAsync(item.TestCaseId)).ShouldBe(stampBefore);

        var reloaded = await _runs.GetAsync(run.Id);
        reloaded.Items.Single().CurrentStatus.ShouldBe(TestResultStatus.Passed);
        reloaded.Items.Single().AttemptCount.ShouldBe(2);
    }

    [Fact]
    public async Task Us2_Independent_Test_Plan_Run_Five_Items_And_Retest()
    {
        var plan = await _plans.CreateAsync(new CreateTestPlanDto { Name = "Sprint 24" });

        var ids = new List<Guid>();
        for (var i = 1; i <= 5; i++)
        {
            ids.Add((await CreateApprovedTestCaseAsync($"TC-{i:000}")).Id);
        }

        var run = await _runs.CreateAsync(new CreateTestRunDto
        {
            TestPlanId = plan.Id,
            Title = "Staging Chrome",
            Environment = "Staging",
            TestCaseIds = ids,
        });
        run.TestPlanId.ShouldBe(plan.Id);
        run.Status.ShouldBe(RunStatus.Planned);
        run.Items.Count.ShouldBe(5);
        run.Items.ShouldAllBe(i => i.CurrentStatus == TestResultStatus.Untested);

        var items = run.Items;
        await _runs.ExecuteItemAsync(run.Id, items[0].Id, new ExecuteTestItemDto { Status = TestResultStatus.Failed });
        await _runs.ExecuteItemAsync(run.Id, items[1].Id, new ExecuteTestItemDto { Status = TestResultStatus.Passed });

        var midway = await _runs.GetAsync(run.Id);
        midway.Status.ShouldBe(RunStatus.InProgress);
        midway.Summary.ExecutedItems.ShouldBe(2);
        midway.Summary.CompletionPercentage.ShouldBe(40);
        midway.Summary.FirstTimePassRate.ShouldBe(50);

        // Re-test item #1 after the bug fix.
        await _runs.ExecuteItemAsync(run.Id, items[0].Id, new ExecuteTestItemDto { Status = TestResultStatus.Passed });

        var final = await _runs.GetAsync(run.Id);
        (await _runs.GetExecutionsAsync(run.Id, items[0].Id)).Count.ShouldBe(2);
        final.Items[0].CurrentStatus.ShouldBe(TestResultStatus.Passed);
        final.Items[0].AttemptCount.ShouldBe(2);
        final.Summary.Passed.ShouldBe(2);
        final.Summary.Untested.ShouldBe(3);
        // Retesting does not change the first-time pass rate: item #1 failed first, item #2 passed first.
        final.Summary.FirstTimePassRate.ShouldBe(50);
    }

    [Fact]
    public async Task Items_Should_Capture_The_Version_Number_At_The_Time_They_Were_Added()
    {
        var testCase = await CreateApprovedTestCaseAsync("TC-VER");
        var run = await _runs.CreateAsync(new CreateTestRunDto
        {
            Title = "R1",
            Environment = "Staging",
            TestCaseIds = { testCase.Id },
        });

        // Library edit after the run started publishes v2 (the test case is Approved).
        await _testCases.UpdateAsync(testCase.Id, new CreateUpdateTestCaseDto
        {
            SuiteId = testCase.SuiteId,
            Code = testCase.Code,
            Title = "Edited title",
            ChangeSummary = "edit",
            Steps = { new TestStepDto { Action = "New", ExpectedResult = "New" } },
        });

        var reloaded = await _runs.GetAsync(run.Id);
        var item = reloaded.Items.Single();
        item.VersionNumber.ShouldBe(1);
        item.TestCaseTitle.ShouldBe("Title of TC-VER");
        item.TestCaseCode.ShouldBe("TC-VER");

        var second = await _runs.CreateAsync(new CreateTestRunDto
        {
            Title = "R2",
            Environment = "Staging",
            TestCaseIds = { testCase.Id },
        });
        second.Items.Single().VersionNumber.ShouldBe(2);
        second.Items.Single().TestCaseTitle.ShouldBe("Edited title");
    }

    [Fact]
    public async Task AddItems_Should_Append_To_An_Existing_Run_And_Reject_Draft_Test_Cases()
    {
        var run = await CreateRunWithItemsAsync(1);
        var extra = await CreateApprovedTestCaseAsync("TC-EXTRA");

        var updated = await _runs.AddItemsAsync(run.Id, new AddTestRunItemsDto { TestCaseIds = { extra.Id } });
        updated.Items.Count.ShouldBe(2);
        updated.Items.Select(i => i.TestCaseCode).ShouldBe(new[] { "TC-001", "TC-EXTRA" });

        var draft = await _testCases.CreateAsync(new CreateUpdateTestCaseDto
        {
            SuiteId = _suiteId!.Value,
            Code = "TC-DRAFT",
            Title = "Draft",
            Steps = { new TestStepDto { Action = "a", ExpectedResult = "b" } },
        });

        (await Should.ThrowAsync<BusinessException>(
                () => _runs.AddItemsAsync(run.Id, new AddTestRunItemsDto { TestCaseIds = { draft.Id } })))
            .Code.ShouldBe(TestCaseManagementErrorCodes.TestCaseNotApproved);
    }

    [Fact]
    public async Task BatchExecute_Should_Record_Every_Result_In_One_Call_And_Be_All_Or_Nothing()
    {
        var run = await CreateRunWithItemsAsync(3);

        var results = await _runs.BatchExecuteAsync(run.Id, new BatchExecuteTestItemsDto
        {
            Items =
            {
                new BatchExecuteTestItemDto { TestRunItemId = run.Items[0].Id, Status = TestResultStatus.Passed },
                new BatchExecuteTestItemDto { TestRunItemId = run.Items[1].Id, Status = TestResultStatus.Failed, ActualResult = "boom" },
                // Same item again in the same batch: becomes attempt #2.
                new BatchExecuteTestItemDto { TestRunItemId = run.Items[1].Id, Status = TestResultStatus.Passed },
            },
        });

        results.Select(r => r.AttemptNumber).ShouldBe(new[] { 1, 1, 2 });
        var summary = (await _runs.GetAsync(run.Id)).Summary;
        summary.Passed.ShouldBe(2);
        summary.Untested.ShouldBe(1);

        // An invalid entry anywhere in the batch must roll back the valid ones.
        await Should.ThrowAsync<BusinessException>(() => _runs.BatchExecuteAsync(run.Id, new BatchExecuteTestItemsDto
        {
            Items =
            {
                new BatchExecuteTestItemDto { TestRunItemId = run.Items[2].Id, Status = TestResultStatus.Passed },
                new BatchExecuteTestItemDto { TestRunItemId = Guid.NewGuid(), Status = TestResultStatus.Passed },
            },
        }));
        (await _runs.GetExecutionsAsync(run.Id, run.Items[2].Id)).ShouldBeEmpty();
    }

    [Fact]
    public async Task Execution_Should_Reject_An_Item_That_Belongs_To_Another_Run()
    {
        var runA = await CreateRunWithItemsAsync(1);
        var testCase = await CreateApprovedTestCaseAsync("TC-OTHER");
        var runB = await _runs.CreateAsync(new CreateTestRunDto
        {
            Title = "B",
            Environment = "Prod",
            TestCaseIds = { testCase.Id },
        });

        (await Should.ThrowAsync<BusinessException>(() => _runs.ExecuteItemAsync(
                runA.Id, runB.Items.Single().Id, new ExecuteTestItemDto { Status = TestResultStatus.Passed })))
            .Code.ShouldBe(TestCaseManagementErrorCodes.TestRunItemNotFound);
    }

    [Fact]
    public async Task Complete_Should_Close_The_Run_And_Block_Further_Execution()
    {
        var run = await CreateRunWithItemsAsync(1);

        var completed = await _runs.CompleteAsync(run.Id);
        completed.Status.ShouldBe(RunStatus.Completed);

        (await Should.ThrowAsync<BusinessException>(() => _runs.ExecuteItemAsync(
                run.Id, run.Items.Single().Id, new ExecuteTestItemDto { Status = TestResultStatus.Passed })))
            .Code.ShouldBe(TestCaseManagementErrorCodes.TestRunAlreadyCompleted);
    }

    [Fact]
    public async Task AssignTester_Should_Set_And_Clear_The_Item_Assignee()
    {
        var run = await CreateRunWithItemsAsync(1);
        var tester = Guid.NewGuid();

        var assigned = await _runs.AssignTesterAsync(run.Id, run.Items.Single().Id, new AssignTestRunItemDto { AssignedUserId = tester });
        assigned.Items.Single().AssignedUserId.ShouldBe(tester);

        var cleared = await _runs.AssignTesterAsync(run.Id, run.Items.Single().Id, new AssignTestRunItemDto());
        cleared.Items.Single().AssignedUserId.ShouldBeNull();
    }

    [Fact]
    public async Task GetList_Should_Filter_By_Plan_Status_And_Title()
    {
        var plan = await _plans.CreateAsync(new CreateTestPlanDto { Name = "Sprint 25" });
        var testCase = await CreateApprovedTestCaseAsync("TC-L1");

        var inPlan = await _runs.CreateAsync(new CreateTestRunDto
        {
            TestPlanId = plan.Id, Title = "Android 14", Environment = "Device farm", TestCaseIds = { testCase.Id },
        });
        await _runs.CreateAsync(new CreateTestRunDto { Title = "Loose run", Environment = "Staging" });
        await _runs.ExecuteItemAsync(inPlan.Id, inPlan.Items.Single().Id, new ExecuteTestItemDto { Status = TestResultStatus.Passed });

        (await _runs.GetListAsync(new GetTestRunListInput { TestPlanId = plan.Id })).Items.Single().Id.ShouldBe(inPlan.Id);
        (await _runs.GetListAsync(new GetTestRunListInput { Status = RunStatus.InProgress })).Items.Single().Id.ShouldBe(inPlan.Id);
        (await _runs.GetListAsync(new GetTestRunListInput { Filter = "loose" })).Items.Single().Title.ShouldBe("Loose run");
        (await _runs.GetListAsync(new GetTestRunListInput())).TotalCount.ShouldBe(2);
    }

    [Fact]
    public async Task Create_Should_Reject_An_Unknown_Plan_Or_A_Duplicate_Test_Case_Id()
    {
        (await Should.ThrowAsync<BusinessException>(() => _runs.CreateAsync(new CreateTestRunDto
            {
                TestPlanId = Guid.NewGuid(), Title = "x", Environment = "y",
            })))
            .Code.ShouldBe(TestCaseManagementErrorCodes.TestPlanNotFound);

        var testCase = await CreateApprovedTestCaseAsync("TC-DUP");
        var run = await _runs.CreateAsync(new CreateTestRunDto
        {
            Title = "x", Environment = "y", TestCaseIds = { testCase.Id, testCase.Id },
        });
        run.Items.Count.ShouldBe(1);
    }

    private async Task<string> GetConcurrencyStampAsync(Guid testCaseId)
    {
        return await WithUnitOfWorkAsync(async () =>
            (await GetRequiredService<IRepository<TestCase, Guid>>().GetAsync(testCaseId)).ConcurrencyStamp);
    }
}
