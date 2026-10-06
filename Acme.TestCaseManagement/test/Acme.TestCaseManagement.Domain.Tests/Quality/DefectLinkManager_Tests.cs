using Acme.TestCaseManagement.Enums;
using Acme.TestCaseManagement.Runs;
using Acme.TestCaseManagement.Suites;
using Acme.TestCaseManagement.TestCases;
using Shouldly;
using Volo.Abp;
using Volo.Abp.Domain.Repositories;
using Xunit;

namespace Acme.TestCaseManagement.Quality;

public class DefectLinkManager_Tests : TestCaseManagementDomainTestBase
{
    private readonly DefectLinkManager _manager;
    private readonly TestRunManager _runManager;
    private readonly TestCaseManager _testCaseManager;
    private readonly TestSuiteManager _suiteManager;
    private readonly IRepository<TestSuite, Guid> _suiteRepository;
    private readonly IRepository<TestCase, Guid> _testCaseRepository;
    private readonly IRepository<TestRun, Guid> _runRepository;
    private readonly IRepository<DefectLink, Guid> _defectRepository;

    public DefectLinkManager_Tests()
    {
        _manager = GetRequiredService<DefectLinkManager>();
        _runManager = GetRequiredService<TestRunManager>();
        _testCaseManager = GetRequiredService<TestCaseManager>();
        _suiteManager = GetRequiredService<TestSuiteManager>();
        _suiteRepository = GetRequiredService<IRepository<TestSuite, Guid>>();
        _testCaseRepository = GetRequiredService<IRepository<TestCase, Guid>>();
        _runRepository = GetRequiredService<IRepository<TestRun, Guid>>();
        _defectRepository = GetRequiredService<IRepository<DefectLink, Guid>>();
    }

    private async Task<TestExecution> ExecuteAsync(
        TestResultStatus status, SeverityLevel testCaseSeverity = SeverityLevel.Medium)
    {
        var suite = await _suiteRepository.InsertAsync(await _suiteManager.CreateAsync("S", null), autoSave: true);
        var testCase = await _testCaseManager.CreateAsync(suite.Id, "TC-1", "Title");
        testCase.SetDetails(
            null, null, null, PriorityLevel.Medium, testCaseSeverity,
            ExecutionType.Manual, TestKind.Functional, TestLayer.Acceptance, null);
        testCase.SetSteps(new[] { new TestStepInput(null, "Act", "Expect", null) });
        await _testCaseRepository.InsertAsync(testCase, autoSave: true);
        await _testCaseManager.ApproveAsync(testCase, null);
        await SaveChangesAsync();

        var run = await _runManager.CreateRunAsync("Run", "Staging");
        await _runManager.AddTestCaseAsync(run, testCase.Id, null);
        await _runRepository.InsertAsync(run, autoSave: true);

        return await _runManager.RecordExecutionAttemptAsync(run.Items.Single().Id, status, "actual", 5);
    }

    [Fact]
    public async Task Add_Should_Link_A_Failed_Execution_To_An_External_Issue()
    {
        await WithUnitOfWorkAsync(async () =>
        {
            var execution = await ExecuteAsync(TestResultStatus.Failed);

            var link = await _manager.AddAsync(execution, " Jira ", " BUG-88 ", "https://acme.atlassian.net/browse/BUG-88");

            link.TestExecutionId.ShouldBe(execution.Id);
            link.ExternalSystem.ShouldBe("Jira");
            link.IssueKey.ShouldBe("BUG-88");
            link.IssueUrl.ShouldBe("https://acme.atlassian.net/browse/BUG-88");
            link.TenantId.ShouldBe(execution.TenantId);
            (await _defectRepository.CountAsync(x => x.TestExecutionId == execution.Id)).ShouldBe(1);
        });
    }

    [Theory]
    [InlineData(SeverityLevel.Critical)]
    [InlineData(SeverityLevel.High)]
    [InlineData(SeverityLevel.Low)]
    public async Task Add_Should_Default_The_Severity_To_That_Of_The_Failing_Test_Case(SeverityLevel testCaseSeverity)
    {
        await WithUnitOfWorkAsync(async () =>
        {
            var execution = await ExecuteAsync(TestResultStatus.Failed, testCaseSeverity);

            var link = await _manager.AddAsync(execution, "Jira", "BUG-1", null);

            link.Severity.ShouldBe(testCaseSeverity);
            link.IsResolved.ShouldBeFalse();
            link.ResolvedTime.ShouldBeNull();
        });
    }

    [Fact]
    public async Task Add_Should_Use_An_Explicit_Severity_Over_The_Test_Case_Severity()
    {
        await WithUnitOfWorkAsync(async () =>
        {
            var execution = await ExecuteAsync(TestResultStatus.Failed, SeverityLevel.Critical);

            var link = await _manager.AddAsync(execution, "Jira", "BUG-1", null, SeverityLevel.Low);

            link.Severity.ShouldBe(SeverityLevel.Low);
        });
    }

    [Fact]
    public async Task Update_Should_Resolve_Reopen_And_Change_Severity()
    {
        await WithUnitOfWorkAsync(async () =>
        {
            var execution = await ExecuteAsync(TestResultStatus.Failed);
            var link = await _manager.AddAsync(execution, "Jira", "BUG-1", null, SeverityLevel.Critical);

            await _manager.UpdateAsync(link, SeverityLevel.High, isResolved: true);
            link.Severity.ShouldBe(SeverityLevel.High);
            link.IsResolved.ShouldBeTrue();
            var resolvedTime = link.ResolvedTime.ShouldNotBeNull();

            // Saving again as resolved keeps the original resolution time.
            await _manager.UpdateAsync(link, SeverityLevel.High, isResolved: true);
            link.ResolvedTime.ShouldBe(resolvedTime);

            await _manager.UpdateAsync(link, SeverityLevel.Critical, isResolved: false);
            link.IsResolved.ShouldBeFalse();
            link.ResolvedTime.ShouldBeNull();
            link.Severity.ShouldBe(SeverityLevel.Critical);

            var stored = await _defectRepository.GetAsync(link.Id);
            stored.IsResolved.ShouldBeFalse();
        });
    }

    [Fact]
    public async Task Add_Should_Accept_A_Missing_Url()
    {
        await WithUnitOfWorkAsync(async () =>
        {
            var execution = await ExecuteAsync(TestResultStatus.Failed);

            var link = await _manager.AddAsync(execution, "GitHub", "#42", null);

            link.IssueUrl.ShouldBeNull();
        });
    }

    [Theory]
    [InlineData(TestResultStatus.Passed)]
    [InlineData(TestResultStatus.Blocked)]
    [InlineData(TestResultStatus.Skipped)]
    public async Task Add_Should_Reject_An_Execution_That_Did_Not_Fail(TestResultStatus status)
    {
        await WithUnitOfWorkAsync(async () =>
        {
            var execution = await ExecuteAsync(status);

            var exception = await Should.ThrowAsync<BusinessException>(
                () => _manager.AddAsync(execution, "Jira", "BUG-1", null));

            exception.Code.ShouldBe(TestCaseManagementErrorCodes.DefectRequiresFailedExecution);
        });
    }

    [Fact]
    public async Task Add_Should_Reject_The_Same_Issue_Twice_Ignoring_Case()
    {
        await WithUnitOfWorkAsync(async () =>
        {
            var execution = await ExecuteAsync(TestResultStatus.Failed);
            await _manager.AddAsync(execution, "Jira", "BUG-88", null);

            (await Should.ThrowAsync<BusinessException>(() => _manager.AddAsync(execution, "jira", "bug-88", null)))
                .Code.ShouldBe(TestCaseManagementErrorCodes.DuplicateDefectLink);

            // The same key in another tracker is a different issue.
            await _manager.AddAsync(execution, "GitHub", "BUG-88", null);
        });
    }

    [Theory]
    [InlineData("not a url")]
    [InlineData("/relative/path")]
    [InlineData("ftp://example.com/BUG-1")]
    [InlineData("javascript:alert(1)")]
    public async Task Add_Should_Reject_A_Url_That_Is_Not_Http_Or_Https(string url)
    {
        await WithUnitOfWorkAsync(async () =>
        {
            var execution = await ExecuteAsync(TestResultStatus.Failed);

            (await Should.ThrowAsync<BusinessException>(() => _manager.AddAsync(execution, "Jira", "BUG-1", url)))
                .Code.ShouldBe(TestCaseManagementErrorCodes.InvalidDefectUrl);
        });
    }

    [Fact]
    public async Task Add_Should_Require_A_System_And_A_Key()
    {
        await WithUnitOfWorkAsync(async () =>
        {
            var execution = await ExecuteAsync(TestResultStatus.Failed);

            await Should.ThrowAsync<ArgumentException>(() => _manager.AddAsync(execution, " ", "BUG-1", null));
            await Should.ThrowAsync<ArgumentException>(() => _manager.AddAsync(execution, "Jira", "", null));
        });
    }
}
