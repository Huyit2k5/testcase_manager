using Acme.TestCaseManagement.Enums;
using Acme.TestCaseManagement.Suites;
using Shouldly;
using Volo.Abp;
using Volo.Abp.Domain.Repositories;
using Xunit;

namespace Acme.TestCaseManagement.TestCases;

public class TestCase_Versioning_Tests : TestCaseManagementDomainTestBase
{
    private readonly TestCaseManager _manager;
    private readonly TestSuiteManager _suiteManager;
    private readonly IRepository<TestSuite, Guid> _suiteRepository;
    private readonly IRepository<TestCase, Guid> _testCaseRepository;
    private readonly IRepository<TestCaseVersion, Guid> _versionRepository;

    public TestCase_Versioning_Tests()
    {
        _manager = GetRequiredService<TestCaseManager>();
        _suiteManager = GetRequiredService<TestSuiteManager>();
        _suiteRepository = GetRequiredService<IRepository<TestSuite, Guid>>();
        _testCaseRepository = GetRequiredService<IRepository<TestCase, Guid>>();
        _versionRepository = GetRequiredService<IRepository<TestCaseVersion, Guid>>();
    }

    private async Task<TestCase> CreateTestCaseWithThreeStepsAsync(string code = "TC-AUTH-001")
    {
        var suite = await _suiteRepository.InsertAsync(await _suiteManager.CreateAsync("Authentication", null), autoSave: true);

        var testCase = await _manager.CreateAsync(suite.Id, code, "Verify successful login with valid credentials");
        testCase.SetDetails(
            description: "Happy path login",
            preconditions: "User account exists",
            postconditions: "Session is active",
            priority: PriorityLevel.High,
            severity: SeverityLevel.Critical,
            executionType: ExecutionType.Manual,
            kind: TestKind.Functional,
            layer: TestLayer.E2E,
            automationId: null);
        testCase.SetSteps(new[]
        {
            new TestStepInput(null, "Open the login page", "Login form is displayed", null),
            new TestStepInput(null, "Enter valid credentials", "Fields accept input", "user=qa@acme.test"),
            new TestStepInput(null, "Click Sign in", "Dashboard is displayed", null),
        });

        return await _testCaseRepository.InsertAsync(testCase, autoSave: true);
    }

    [Fact]
    public async Task Create_Should_Start_As_Draft_At_Version_Zero()
    {
        await WithUnitOfWorkAsync(async () =>
        {
            var testCase = await CreateTestCaseWithThreeStepsAsync();

            testCase.Status.ShouldBe(TestCaseStatus.Draft);
            testCase.CurrentVersion.ShouldBe(0);
            testCase.Steps.Select(s => s.StepOrder).OrderBy(x => x).ShouldBe(new[] { 1, 2, 3 });
        });
    }

    [Fact]
    public async Task PublishNewVersion_Should_Create_Version_One_With_Step_Snapshot()
    {
        await WithUnitOfWorkAsync(async () =>
        {
            var testCase = await CreateTestCaseWithThreeStepsAsync();

            var version = await _manager.PublishNewVersionAsync(testCase, "Initial version");

            version.VersionNumber.ShouldBe(1);
            version.TestCaseId.ShouldBe(testCase.Id);
            version.ChangeSummary.ShouldBe("Initial version");
            version.Title.ShouldBe(testCase.Title);
            version.Preconditions.ShouldBe("User account exists");
            version.Postconditions.ShouldBe("Session is active");
            testCase.CurrentVersion.ShouldBe(1);

            var steps = TestStepSnapshot.Deserialize(version.StepsJson);
            steps.Count.ShouldBe(3);
            steps.Select(s => s.Order).ShouldBe(new[] { 1, 2, 3 });
            steps.Select(s => s.Action).ShouldBe(new[] { "Open the login page", "Enter valid credentials", "Click Sign in" });
            steps[1].ExpectedResult.ShouldBe("Fields accept input");
            steps[1].TestData.ShouldBe("user=qa@acme.test");
            steps[0].TestData.ShouldBeNull();
            steps.Select(s => s.Id).ShouldBe(testCase.Steps.OrderBy(s => s.StepOrder).Select(s => s.Id));
        });
    }

    [Fact]
    public async Task PublishNewVersion_Should_Increment_And_Leave_Earlier_Snapshots_Untouched()
    {
        await WithUnitOfWorkAsync(async () =>
        {
            var testCase = await CreateTestCaseWithThreeStepsAsync();
            var v1 = await _manager.PublishNewVersionAsync(testCase, "v1");
            var v1Json = v1.StepsJson;

            // Edit the library test case after v1 was published.
            var existing = testCase.Steps.OrderBy(s => s.StepOrder).ToList();
            testCase.SetTitle("Verify login (renamed)");
            testCase.SetSteps(new[]
            {
                new TestStepInput(existing[0].Id, "Open the login page (edited)", "Login form is displayed", null),
                new TestStepInput(existing[2].Id, "Click Sign in", "Dashboard is displayed", null),
            });

            var v2 = await _manager.PublishNewVersionAsync(testCase, "v2");
            await SaveChangesAsync();

            v2.VersionNumber.ShouldBe(2);
            testCase.CurrentVersion.ShouldBe(2);
            TestStepSnapshot.Deserialize(v2.StepsJson).Count.ShouldBe(2);

            var storedV1 = await _versionRepository.GetAsync(v1.Id);
            storedV1.VersionNumber.ShouldBe(1);
            storedV1.Title.ShouldBe("Verify successful login with valid credentials");
            storedV1.StepsJson.ShouldBe(v1Json);
            TestStepSnapshot.Deserialize(storedV1.StepsJson).Select(s => s.Action)
                .ShouldBe(new[] { "Open the login page", "Enter valid credentials", "Click Sign in" });
        });
    }

    [Fact]
    public async Task Approve_From_Draft_Should_Create_Version_One()
    {
        await WithUnitOfWorkAsync(async () =>
        {
            var testCase = await CreateTestCaseWithThreeStepsAsync();

            var version = await _manager.ApproveAsync(testCase, "Approved by QA Lead");
            await SaveChangesAsync();

            testCase.Status.ShouldBe(TestCaseStatus.Approved);
            version.VersionNumber.ShouldBe(1);
            (await _versionRepository.CountAsync(v => v.TestCaseId == testCase.Id)).ShouldBe(1);
        });
    }

    [Fact]
    public async Task Approve_Without_Steps_Should_Fail_And_Not_Create_A_Version()
    {
        await WithUnitOfWorkAsync(async () =>
        {
            var suite = await _suiteRepository.InsertAsync(await _suiteManager.CreateAsync("S", null), autoSave: true);
            var empty = await _testCaseRepository.InsertAsync(
                await _manager.CreateAsync(suite.Id, "TC-EMPTY", "No steps"), autoSave: true);

            var exception = await Should.ThrowAsync<BusinessException>(() => _manager.ApproveAsync(empty, null));

            exception.Code.ShouldBe(TestCaseManagementErrorCodes.TestCaseHasNoSteps);
            empty.Status.ShouldBe(TestCaseStatus.Draft);
            (await _versionRepository.CountAsync(v => v.TestCaseId == empty.Id)).ShouldBe(0);
        });
    }

    [Fact]
    public async Task ChangeStatus_Should_Reject_Invalid_Transitions()
    {
        await WithUnitOfWorkAsync(async () =>
        {
            var testCase = await CreateTestCaseWithThreeStepsAsync();
            await _manager.ChangeStatusAsync(testCase, TestCaseStatus.Deprecated, null)
                .ShouldThrowAsync<BusinessException>();

            await _manager.ApproveAsync(testCase, null);
            await _manager.ChangeStatusAsync(testCase, TestCaseStatus.Deprecated, null);

            var exception = await Should.ThrowAsync<BusinessException>(
                () => _manager.ChangeStatusAsync(testCase, TestCaseStatus.Approved, null));
            exception.Code.ShouldBe(TestCaseManagementErrorCodes.InvalidTestCaseStatusTransition);
        });
    }

    [Fact]
    public async Task Review_Flow_Should_Reach_Approved_And_Create_A_Version()
    {
        await WithUnitOfWorkAsync(async () =>
        {
            var testCase = await CreateTestCaseWithThreeStepsAsync();

            await _manager.ChangeStatusAsync(testCase, TestCaseStatus.UnderReview, null);
            testCase.Status.ShouldBe(TestCaseStatus.UnderReview);
            testCase.CurrentVersion.ShouldBe(0);

            await _manager.ChangeStatusAsync(testCase, TestCaseStatus.Approved, "Reviewed");
            testCase.Status.ShouldBe(TestCaseStatus.Approved);
            testCase.CurrentVersion.ShouldBe(1);
        });
    }

    [Fact]
    public async Task Create_Should_Reject_A_Duplicate_Code()
    {
        await WithUnitOfWorkAsync(async () =>
        {
            var existing = await CreateTestCaseWithThreeStepsAsync("TC-DUP-001");

            var exception = await Should.ThrowAsync<BusinessException>(
                () => _manager.CreateAsync(existing.SuiteId, "TC-DUP-001", "Another"));

            exception.Code.ShouldBe(TestCaseManagementErrorCodes.DuplicateTestCaseCode);
        });
    }

    [Fact]
    public async Task ReorderSteps_Should_Renumber_And_Reject_An_Incomplete_List()
    {
        await WithUnitOfWorkAsync(async () =>
        {
            var testCase = await CreateTestCaseWithThreeStepsAsync();
            var ids = testCase.Steps.OrderBy(s => s.StepOrder).Select(s => s.Id).ToList();

            testCase.ReorderSteps(new[] { ids[2], ids[0], ids[1] });

            testCase.Steps.OrderBy(s => s.StepOrder).Select(s => s.Id).ShouldBe(new[] { ids[2], ids[0], ids[1] });
            testCase.Steps.Select(s => s.StepOrder).OrderBy(x => x).ShouldBe(new[] { 1, 2, 3 });

            Should.Throw<BusinessException>(() => testCase.ReorderSteps(new[] { ids[0], ids[1] }))
                .Code.ShouldBe(TestCaseManagementErrorCodes.InvalidStepOrder);
        });
    }
}

internal static class TaskShouldlyExtensions
{
    public static async Task ShouldThrowAsync<TException>(this Task task) where TException : Exception
    {
        await Should.ThrowAsync<TException>(() => task);
    }
}
