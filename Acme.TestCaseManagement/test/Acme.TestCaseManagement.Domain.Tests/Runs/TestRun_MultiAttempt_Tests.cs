using Acme.TestCaseManagement.Enums;
using Acme.TestCaseManagement.Suites;
using Acme.TestCaseManagement.TestCases;
using Shouldly;
using Volo.Abp;
using Volo.Abp.Domain.Repositories;
using Xunit;

namespace Acme.TestCaseManagement.Runs;

public class TestRun_MultiAttempt_Tests : TestCaseManagementDomainTestBase
{
    private readonly TestRunManager _runManager;
    private readonly TestCaseManager _testCaseManager;
    private readonly TestSuiteManager _suiteManager;
    private readonly IRepository<TestSuite, Guid> _suiteRepository;
    private readonly IRepository<TestCase, Guid> _testCaseRepository;
    private readonly IRepository<TestCaseVersion, Guid> _versionRepository;
    private readonly IRepository<TestRun, Guid> _runRepository;
    private readonly IRepository<TestRunItem, Guid> _itemRepository;
    private readonly IRepository<TestExecution, Guid> _executionRepository;

    public TestRun_MultiAttempt_Tests()
    {
        _runManager = GetRequiredService<TestRunManager>();
        _testCaseManager = GetRequiredService<TestCaseManager>();
        _suiteManager = GetRequiredService<TestSuiteManager>();
        _suiteRepository = GetRequiredService<IRepository<TestSuite, Guid>>();
        _testCaseRepository = GetRequiredService<IRepository<TestCase, Guid>>();
        _versionRepository = GetRequiredService<IRepository<TestCaseVersion, Guid>>();
        _runRepository = GetRequiredService<IRepository<TestRun, Guid>>();
        _itemRepository = GetRequiredService<IRepository<TestRunItem, Guid>>();
        _executionRepository = GetRequiredService<IRepository<TestExecution, Guid>>();
    }

    private async Task<TestCase> CreateTestCaseAsync(string code, bool approve = true)
    {
        var suite = (await _suiteRepository.GetListAsync()).FirstOrDefault()
                    ?? await _suiteRepository.InsertAsync(await _suiteManager.CreateAsync("Suite", null), autoSave: true);

        var testCase = await _testCaseManager.CreateAsync(suite.Id, code, $"Title of {code}");
        testCase.SetSteps(new[]
        {
            new TestStepInput(null, "Open the page", "Page is shown", null),
            new TestStepInput(null, "Submit the form", "Form is accepted", "data"),
        });
        await _testCaseRepository.InsertAsync(testCase, autoSave: true);

        if (approve)
        {
            await _testCaseManager.ApproveAsync(testCase, "initial");
            await SaveChangesAsync();
        }

        return testCase;
    }

    private async Task<TestRun> CreateRunAsync(params TestCase[] testCases)
    {
        var run = await _runManager.CreateRunAsync("Staging Chrome", "Staging");
        foreach (var testCase in testCases)
        {
            await _runManager.AddTestCaseAsync(run, testCase.Id, assignedUserId: null);
        }

        await _runRepository.InsertAsync(run, autoSave: true);
        return run;
    }

    [Fact]
    public async Task AddTestCase_Should_Bind_The_Item_To_The_Current_Approved_Version()
    {
        await WithUnitOfWorkAsync(async () =>
        {
            var testCase = await CreateTestCaseAsync("TC-1");
            var version = await _versionRepository.GetAsync(v => v.TestCaseId == testCase.Id && v.VersionNumber == 1);

            var run = await CreateRunAsync(testCase);

            var item = run.Items.Single();
            item.TestCaseVersionId.ShouldBe(version.Id);
            item.CurrentStatus.ShouldBe(TestResultStatus.Untested);
            item.TenantId.ShouldBe(run.TenantId);
            run.Status.ShouldBe(RunStatus.Planned);
        });
    }

    [Fact]
    public async Task AddTestCase_Should_Reject_A_Test_Case_That_Is_Not_Approved()
    {
        await WithUnitOfWorkAsync(async () =>
        {
            var draft = await CreateTestCaseAsync("TC-DRAFT", approve: false);
            var run = await _runManager.CreateRunAsync("Run", "Staging");

            var exception = await Should.ThrowAsync<BusinessException>(
                () => _runManager.AddTestCaseAsync(run, draft.Id, null));

            exception.Code.ShouldBe(TestCaseManagementErrorCodes.TestCaseNotApproved);
            run.Items.ShouldBeEmpty();
        });
    }

    [Fact]
    public async Task AddTestCase_Should_Reject_The_Same_Test_Case_Twice_And_The_Run_Itself_The_Same_Version()
    {
        await WithUnitOfWorkAsync(async () =>
        {
            var testCase = await CreateTestCaseAsync("TC-1");
            var run = await CreateRunAsync(testCase);

            var exception = await Should.ThrowAsync<BusinessException>(
                () => _runManager.AddTestCaseAsync(run, testCase.Id, null));

            // The manager answers per test case (a newer version of the same test case is refused too, see the application tests)...
            exception.Code.ShouldBe(TestCaseManagementErrorCodes.TestCaseAlreadyInRun);

            // ...and the run keeps its own guard against the same version, for anyone who adds items directly.
            var versionId = run.Items.Single().TestCaseVersionId;
            Should.Throw<BusinessException>(() => run.AddItem(versionId, null)).Code.ShouldBe(TestCaseManagementErrorCodes.DuplicateTestRunItem);
        });
    }

    [Fact]
    public async Task Run_Item_Should_Stay_Bound_To_Its_Version_When_The_Library_Test_Case_Is_Updated()
    {
        await WithUnitOfWorkAsync(async () =>
        {
            var testCase = await CreateTestCaseAsync("TC-1");
            var v1 = await _versionRepository.GetAsync(v => v.TestCaseId == testCase.Id && v.VersionNumber == 1);
            var v1Json = v1.StepsJson;
            var runA = await CreateRunAsync(testCase);

            // An author edits the library test case and publishes v2 while run A is in flight.
            testCase.SetTitle("Edited after the run started");
            testCase.SetSteps(new[] { new TestStepInput(null, "Completely new step", "New result", null) });
            await _testCaseManager.PublishNewVersionAsync(testCase, "v2");
            await _testCaseRepository.UpdateAsync(testCase, autoSave: true);

            var itemA = await _itemRepository.GetAsync(runA.Items.Single().Id);
            itemA.TestCaseVersionId.ShouldBe(v1.Id);

            var storedV1 = await _versionRepository.GetAsync(v1.Id);
            storedV1.Title.ShouldBe("Title of TC-1");
            storedV1.StepsJson.ShouldBe(v1Json);
            TestStepSnapshot.Deserialize(storedV1.StepsJson).Count.ShouldBe(2);

            // A run created now picks up the new version, proving the binding is per run item.
            var v2 = await _versionRepository.GetAsync(v => v.TestCaseId == testCase.Id && v.VersionNumber == 2);
            var runB = await CreateRunAsync(testCase);
            runB.Items.Single().TestCaseVersionId.ShouldBe(v2.Id);
        });
    }

    [Fact]
    public async Task Executing_An_Item_Twice_Should_Append_Two_Attempts_And_Keep_The_First_Intact()
    {
        await WithUnitOfWorkAsync(async () =>
        {
            var testCase = await CreateTestCaseAsync("TC-1");
            var run = await CreateRunAsync(testCase);
            var itemId = run.Items.Single().Id;

            var first = await _runManager.RecordExecutionAttemptAsync(itemId, TestResultStatus.Failed, "Button disabled", 45);
            (await _itemRepository.GetAsync(itemId)).CurrentStatus.ShouldBe(TestResultStatus.Failed);

            var second = await _runManager.RecordExecutionAttemptAsync(itemId, TestResultStatus.Passed, "Works after fix", 30);
            await SaveChangesAsync();

            first.AttemptNumber.ShouldBe(1);
            second.AttemptNumber.ShouldBe(2);
            (await _itemRepository.GetAsync(itemId)).CurrentStatus.ShouldBe(TestResultStatus.Passed);

            var attempts = (await _executionRepository.GetListAsync(x => x.TestRunItemId == itemId))
                .OrderBy(x => x.AttemptNumber).ToList();
            attempts.Count.ShouldBe(2);
            attempts[0].Id.ShouldBe(first.Id);
            attempts[0].Status.ShouldBe(TestResultStatus.Failed);
            attempts[0].ActualResult.ShouldBe("Button disabled");
            attempts[0].DurationSeconds.ShouldBe(45);
            attempts[1].Status.ShouldBe(TestResultStatus.Passed);
        });
    }

    [Fact]
    public async Task Attempt_Numbers_Should_Be_Independent_Per_Item()
    {
        await WithUnitOfWorkAsync(async () =>
        {
            var run = await CreateRunAsync(await CreateTestCaseAsync("TC-1"), await CreateTestCaseAsync("TC-2"));
            var items = run.Items.OrderBy(i => i.Sequence).ToList();

            await _runManager.RecordExecutionAttemptAsync(items[0].Id, TestResultStatus.Failed, null, 1);
            await _runManager.RecordExecutionAttemptAsync(items[0].Id, TestResultStatus.Failed, null, 1);
            var other = await _runManager.RecordExecutionAttemptAsync(items[1].Id, TestResultStatus.Passed, null, 1);

            other.AttemptNumber.ShouldBe(1);
        });
    }

    [Fact]
    public async Task First_Attempt_Should_Start_The_Run_And_Roll_Up_Completion()
    {
        await WithUnitOfWorkAsync(async () =>
        {
            var run = await CreateRunAsync(await CreateTestCaseAsync("TC-1"), await CreateTestCaseAsync("TC-2"));
            run.CompletionPercentage.ShouldBe(0);

            await _runManager.RecordExecutionAttemptAsync(run.Items.First().Id, TestResultStatus.Passed, null, 5);
            await SaveChangesAsync();

            var reloaded = await _runRepository.GetAsync(run.Id);
            reloaded.Status.ShouldBe(RunStatus.InProgress);
            reloaded.CompletionPercentage.ShouldBe(50);
        });
    }

    [Fact]
    public async Task Execution_Should_Reject_Untested_As_A_Result()
    {
        await WithUnitOfWorkAsync(async () =>
        {
            var run = await CreateRunAsync(await CreateTestCaseAsync("TC-1"));

            var exception = await Should.ThrowAsync<BusinessException>(
                () => _runManager.RecordExecutionAttemptAsync(run.Items.Single().Id, TestResultStatus.Untested, null, 0));

            exception.Code.ShouldBe(TestCaseManagementErrorCodes.InvalidExecutionStatus);
        });
    }

    [Fact]
    public async Task Execution_Should_Reject_An_Unknown_Item()
    {
        await WithUnitOfWorkAsync(async () =>
        {
            var exception = await Should.ThrowAsync<BusinessException>(
                () => _runManager.RecordExecutionAttemptAsync(Guid.NewGuid(), TestResultStatus.Passed, null, 0));

            exception.Code.ShouldBe(TestCaseManagementErrorCodes.TestRunItemNotFound);
        });
    }

    [Fact]
    public async Task Completed_Run_Should_Reject_New_Attempts_And_New_Items()
    {
        await WithUnitOfWorkAsync(async () =>
        {
            var testCase = await CreateTestCaseAsync("TC-1");
            var other = await CreateTestCaseAsync("TC-2");
            var run = await CreateRunAsync(testCase);

            run.Complete();
            await _runRepository.UpdateAsync(run, autoSave: true);

            (await Should.ThrowAsync<BusinessException>(
                    () => _runManager.RecordExecutionAttemptAsync(run.Items.Single().Id, TestResultStatus.Passed, null, 1)))
                .Code.ShouldBe(TestCaseManagementErrorCodes.TestRunAlreadyCompleted);
            (await Should.ThrowAsync<BusinessException>(
                    () => _runManager.AddTestCaseAsync(run, other.Id, null)))
                .Code.ShouldBe(TestCaseManagementErrorCodes.TestRunAlreadyCompleted);
            Should.Throw<BusinessException>(() => run.Complete())
                .Code.ShouldBe(TestCaseManagementErrorCodes.TestRunAlreadyCompleted);
        });
    }

    [Fact]
    public async Task CreateRun_Should_Reject_An_Unknown_Plan()
    {
        await WithUnitOfWorkAsync(async () =>
        {
            var exception = await Should.ThrowAsync<BusinessException>(
                () => _runManager.CreateRunAsync("Run", "Staging", testPlanId: Guid.NewGuid()));

            exception.Code.ShouldBe(TestCaseManagementErrorCodes.TestPlanNotFound);
        });
    }
}
