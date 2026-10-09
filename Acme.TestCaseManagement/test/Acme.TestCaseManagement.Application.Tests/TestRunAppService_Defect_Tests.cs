using Acme.TestCaseManagement.Enums;
using Acme.TestCaseManagement.Runs;
using Acme.TestCaseManagement.Runs.Dtos;
using Acme.TestCaseManagement.Suites;
using Acme.TestCaseManagement.Suites.Dtos;
using Acme.TestCaseManagement.TestCases;
using Acme.TestCaseManagement.TestCases.Dtos;
using Shouldly;
using Volo.Abp;
using Volo.Abp.Domain.Entities;
using Xunit;

namespace Acme.TestCaseManagement;

public class TestRunAppService_Defect_Tests : TestCaseManagementApplicationTestBase
{
    private readonly ITestSuiteAppService _suites;
    private readonly ITestCaseAppService _testCases;
    private readonly ITestRunAppService _runs;

    private Guid? _suiteId;

    public TestRunAppService_Defect_Tests()
    {
        _suites = GetRequiredService<ITestSuiteAppService>();
        _testCases = GetRequiredService<ITestCaseAppService>();
        _runs = GetRequiredService<ITestRunAppService>();
    }

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

    private async Task<(TestRunDto Run, TestRunItemDto Item)> CreateRunAsync(Guid testCaseId, string title = "Staging Chrome")
    {
        var run = await _runs.CreateAsync(new CreateTestRunDto
        {
            Title = title,
            Environment = "Staging",
            TestCaseIds = { testCaseId },
        });

        return (run, run.Items.Single());
    }

    private Task<TestExecutionDto> ExecuteAsync(Guid runId, Guid itemId, TestResultStatus status) =>
        _runs.ExecuteItemAsync(runId, itemId, new ExecuteTestItemDto { Status = status, ActualResult = "actual" });

    [Fact]
    public async Task Us3_Independent_Test_Failed_Execution_Gets_A_Jira_Defect_Visible_In_Execution_Details()
    {
        var testCase = await CreateApprovedTestCaseAsync("TC-1");
        var (run, item) = await CreateRunAsync(testCase.Id);
        var execution = await ExecuteAsync(run.Id, item.Id, TestResultStatus.Failed);

        var link = await _runs.AddDefectLinkAsync(execution.Id, new AddDefectLinkDto
        {
            ExternalSystem = "Jira",
            IssueKey = "BUG-88",
            IssueUrl = "https://acme.atlassian.net/browse/BUG-88",
        });

        link.TestExecutionId.ShouldBe(execution.Id);
        link.ExternalSystem.ShouldBe("Jira");
        link.IssueKey.ShouldBe("BUG-88");

        var history = await _runs.GetExecutionsAsync(run.Id, item.Id);
        history.Single().DefectLinks.Single().IssueKey.ShouldBe("BUG-88");
        (await _runs.GetDefectLinksAsync(execution.Id)).Single().Id.ShouldBe(link.Id);

        var forTestCase = await _testCases.GetDefectLinksAsync(testCase.Id);
        forTestCase.Single().IssueKey.ShouldBe("BUG-88");
        forTestCase.Single().TestRunId.ShouldBe(run.Id);
    }

    [Fact]
    public async Task Defects_Should_Be_Attachable_While_Executing_And_Only_To_A_Failed_Result()
    {
        var testCase = await CreateApprovedTestCaseAsync("TC-1");
        var (run, item) = await CreateRunAsync(testCase.Id);

        // A passed result with a defect is rejected before the attempt is recorded.
        (await Should.ThrowAsync<BusinessException>(() => _runs.ExecuteItemAsync(run.Id, item.Id, new ExecuteTestItemDto
            {
                Status = TestResultStatus.Passed,
                Defects = { new AddDefectLinkDto { ExternalSystem = "Jira", IssueKey = "BUG-1" } },
            })))
            .Code.ShouldBe(TestCaseManagementErrorCodes.DefectRequiresFailedExecution);
        (await _runs.GetExecutionsAsync(run.Id, item.Id)).ShouldBeEmpty();

        var failed = await _runs.ExecuteItemAsync(run.Id, item.Id, new ExecuteTestItemDto
        {
            Status = TestResultStatus.Failed,
            Defects =
            {
                new AddDefectLinkDto { ExternalSystem = "Jira", IssueKey = "BUG-1" },
                new AddDefectLinkDto { ExternalSystem = "GitHub", IssueKey = "#42", IssueUrl = "https://github.com/acme/app/issues/42" },
            },
        });

        failed.DefectLinks.Select(x => x.IssueKey).ShouldBe(new[] { "BUG-1", "#42" });
        (await _runs.GetExecutionsAsync(run.Id, item.Id)).Single().DefectLinks.Count.ShouldBe(2);
    }

    [Fact]
    public async Task Linking_To_A_Passed_Execution_Should_Be_Rejected()
    {
        var testCase = await CreateApprovedTestCaseAsync("TC-1");
        var (run, item) = await CreateRunAsync(testCase.Id);
        var passed = await ExecuteAsync(run.Id, item.Id, TestResultStatus.Passed);

        (await Should.ThrowAsync<BusinessException>(() => _runs.AddDefectLinkAsync(
                passed.Id, new AddDefectLinkDto { ExternalSystem = "Jira", IssueKey = "BUG-1" })))
            .Code.ShouldBe(TestCaseManagementErrorCodes.DefectRequiresFailedExecution);
    }

    [Fact]
    public async Task Defects_Of_A_Test_Case_Should_Be_Listed_Chronologically_Across_Runs_And_Versions()
    {
        var testCase = await CreateApprovedTestCaseAsync("TC-1");

        var (runA, itemA) = await CreateRunAsync(testCase.Id, "Sprint 23 regression");
        var executionA = await ExecuteAsync(runA.Id, itemA.Id, TestResultStatus.Failed);
        await _runs.AddDefectLinkAsync(executionA.Id, new AddDefectLinkDto { ExternalSystem = "Jira", IssueKey = "BUG-10" });

        // The library test case changes (v2) before the next run.
        await _testCases.UpdateAsync(testCase.Id, new CreateUpdateTestCaseDto
        {
            SuiteId = testCase.SuiteId,
            Code = testCase.Code,
            Title = testCase.Title,
            Steps = { new TestStepDto { Action = "Changed", ExpectedResult = "Changed" } },
        });
        await _testCases.ChangeStatusAsync(testCase.Id, new ChangeTestCaseStatusDto { TargetStatus = TestCaseStatus.Approved });

        var (runB, itemB) = await CreateRunAsync(testCase.Id, "Sprint 24 regression");
        var first = await ExecuteAsync(runB.Id, itemB.Id, TestResultStatus.Failed);
        await _runs.AddDefectLinkAsync(first.Id, new AddDefectLinkDto { ExternalSystem = "Jira", IssueKey = "BUG-20" });
        var retest = await ExecuteAsync(runB.Id, itemB.Id, TestResultStatus.Failed);
        await _runs.AddDefectLinkAsync(retest.Id, new AddDefectLinkDto { ExternalSystem = "GitHub", IssueKey = "#7" });

        // A defect on some other test case must not show up.
        var other = await CreateApprovedTestCaseAsync("TC-2");
        var (runC, itemC) = await CreateRunAsync(other.Id, "Other");
        var otherExecution = await ExecuteAsync(runC.Id, itemC.Id, TestResultStatus.Failed);
        await _runs.AddDefectLinkAsync(otherExecution.Id, new AddDefectLinkDto { ExternalSystem = "Jira", IssueKey = "BUG-99" });

        var defects = await _testCases.GetDefectLinksAsync(testCase.Id);

        defects.Select(x => x.IssueKey).ShouldBe(new[] { "BUG-10", "BUG-20", "#7" });
        defects.Select(x => x.TestRunTitle).ShouldBe(new[] { "Sprint 23 regression", "Sprint 24 regression", "Sprint 24 regression" });
        defects.Select(x => x.VersionNumber).ShouldBe(new[] { 1, 2, 2 });
        defects.Select(x => x.AttemptNumber).ShouldBe(new[] { 1, 1, 2 });
        defects.Select(x => x.ExecutionTime).ShouldBe(defects.Select(x => x.ExecutionTime).OrderBy(x => x));
        defects.ShouldAllBe(x => x.Environment == "Staging");
    }

    [Fact]
    public async Task Linking_Should_Reject_A_Duplicate_And_A_Bad_Url()
    {
        var testCase = await CreateApprovedTestCaseAsync("TC-1");
        var (run, item) = await CreateRunAsync(testCase.Id);
        var execution = await ExecuteAsync(run.Id, item.Id, TestResultStatus.Failed);
        await _runs.AddDefectLinkAsync(execution.Id, new AddDefectLinkDto { ExternalSystem = "Jira", IssueKey = "BUG-1" });

        (await Should.ThrowAsync<BusinessException>(() => _runs.AddDefectLinkAsync(
                execution.Id, new AddDefectLinkDto { ExternalSystem = "JIRA", IssueKey = "bug-1" })))
            .Code.ShouldBe(TestCaseManagementErrorCodes.DuplicateDefectLink);
        (await Should.ThrowAsync<BusinessException>(() => _runs.AddDefectLinkAsync(
                execution.Id, new AddDefectLinkDto { ExternalSystem = "Jira", IssueKey = "BUG-2", IssueUrl = "javascript:alert(1)" })))
            .Code.ShouldBe(TestCaseManagementErrorCodes.InvalidDefectUrl);
        await Should.ThrowAsync<EntityNotFoundException>(() => _runs.AddDefectLinkAsync(
            Guid.NewGuid(), new AddDefectLinkDto { ExternalSystem = "Jira", IssueKey = "BUG-3" }));

        (await _runs.GetDefectLinksAsync(execution.Id)).Count.ShouldBe(1);
    }

    [Fact]
    public async Task Severity_Defaults_To_The_Test_Case_Severity_And_Can_Be_Overridden_And_Changed()
    {
        _suiteId ??= (await _suites.CreateAsync(new CreateTestSuiteDto { Name = "Library" })).Id;
        var critical = await _testCases.CreateAsync(new CreateUpdateTestCaseDto
        {
            SuiteId = _suiteId.Value,
            Code = "TC-CRIT",
            Title = "Critical flow",
            Severity = SeverityLevel.Critical,
            Steps = { new TestStepDto { Action = "Do it", ExpectedResult = "Done" } },
        });
        await _testCases.ChangeStatusAsync(critical.Id, new ChangeTestCaseStatusDto { TargetStatus = TestCaseStatus.Approved });
        var (run, item) = await CreateRunAsync(critical.Id);
        var execution = await ExecuteAsync(run.Id, item.Id, TestResultStatus.Failed);

        var inherited = await _runs.AddDefectLinkAsync(execution.Id, new AddDefectLinkDto { ExternalSystem = "Jira", IssueKey = "BUG-1" });
        var explicitLow = await _runs.AddDefectLinkAsync(execution.Id, new AddDefectLinkDto
        {
            ExternalSystem = "Jira", IssueKey = "BUG-2", Severity = SeverityLevel.Low,
        });

        inherited.Severity.ShouldBe(SeverityLevel.Critical);
        inherited.IsResolved.ShouldBeFalse();
        explicitLow.Severity.ShouldBe(SeverityLevel.Low);

        var changed = await _runs.UpdateDefectLinkAsync(execution.Id, explicitLow.Id,
            new UpdateDefectLinkDto { Severity = SeverityLevel.High, IsResolved = false });
        changed.Severity.ShouldBe(SeverityLevel.High);
    }

    [Fact]
    public async Task Update_Should_Resolve_And_Reopen_A_Defect_And_Be_Visible_In_Every_View()
    {
        var testCase = await CreateApprovedTestCaseAsync("TC-1");
        var (run, item) = await CreateRunAsync(testCase.Id);
        var execution = await ExecuteAsync(run.Id, item.Id, TestResultStatus.Failed);
        var link = await _runs.AddDefectLinkAsync(execution.Id, new AddDefectLinkDto { ExternalSystem = "Jira", IssueKey = "BUG-1" });

        var resolved = await _runs.UpdateDefectLinkAsync(execution.Id, link.Id,
            new UpdateDefectLinkDto { Severity = link.Severity, IsResolved = true });

        resolved.IsResolved.ShouldBeTrue();
        resolved.ResolvedTime.ShouldNotBeNull();
        (await _runs.GetDefectLinksAsync(execution.Id)).Single().IsResolved.ShouldBeTrue();
        (await _runs.GetExecutionsAsync(run.Id, item.Id)).Single().DefectLinks.Single().IsResolved.ShouldBeTrue();
        (await _testCases.GetDefectLinksAsync(testCase.Id)).Single().IsResolved.ShouldBeTrue();

        var reopened = await _runs.UpdateDefectLinkAsync(execution.Id, link.Id,
            new UpdateDefectLinkDto { Severity = link.Severity, IsResolved = false });
        reopened.IsResolved.ShouldBeFalse();
        reopened.ResolvedTime.ShouldBeNull();

        await Should.ThrowAsync<EntityNotFoundException>(() => _runs.UpdateDefectLinkAsync(
            execution.Id, Guid.NewGuid(), new UpdateDefectLinkDto()));
    }

    [Fact]
    public async Task Remove_Should_Soft_Delete_The_Link_And_Leave_The_Execution_Untouched()
    {
        var testCase = await CreateApprovedTestCaseAsync("TC-1");
        var (run, item) = await CreateRunAsync(testCase.Id);
        var execution = await ExecuteAsync(run.Id, item.Id, TestResultStatus.Failed);
        var link = await _runs.AddDefectLinkAsync(execution.Id, new AddDefectLinkDto { ExternalSystem = "Jira", IssueKey = "TYPO-1" });

        await _runs.RemoveDefectLinkAsync(execution.Id, link.Id);

        (await _runs.GetDefectLinksAsync(execution.Id)).ShouldBeEmpty();
        (await _testCases.GetDefectLinksAsync(testCase.Id)).ShouldBeEmpty();
        (await _runs.GetExecutionsAsync(run.Id, item.Id)).Single().Status.ShouldBe(TestResultStatus.Failed);

        // The corrected key can be entered again.
        await _runs.AddDefectLinkAsync(execution.Id, new AddDefectLinkDto { ExternalSystem = "Jira", IssueKey = "BUG-1" });
        (await _runs.GetDefectLinksAsync(execution.Id)).Single().IssueKey.ShouldBe("BUG-1");

        await Should.ThrowAsync<EntityNotFoundException>(() => _runs.RemoveDefectLinkAsync(execution.Id, link.Id));
    }

    [Fact]
    public async Task Batch_Execute_Should_Link_Defects_And_Reject_Them_On_Non_Failed_Entries_Without_Recording_Anything()
    {
        var one = await CreateApprovedTestCaseAsync("TC-1");
        var two = await CreateApprovedTestCaseAsync("TC-2");
        var run = await _runs.CreateAsync(new CreateTestRunDto
        {
            Title = "CI", Environment = "Staging", TestCaseIds = { one.Id, two.Id },
        });

        await Should.ThrowAsync<BusinessException>(() => _runs.BatchExecuteAsync(run.Id, new BatchExecuteTestItemsDto
        {
            Items =
            {
                new BatchExecuteTestItemDto { TestRunItemId = run.Items[0].Id, Status = TestResultStatus.Failed },
                new BatchExecuteTestItemDto
                {
                    TestRunItemId = run.Items[1].Id,
                    Status = TestResultStatus.Passed,
                    Defects = { new AddDefectLinkDto { ExternalSystem = "Jira", IssueKey = "BUG-5" } },
                },
            },
        }));
        (await _runs.GetExecutionsAsync(run.Id, run.Items[0].Id)).ShouldBeEmpty();

        var results = await _runs.BatchExecuteAsync(run.Id, new BatchExecuteTestItemsDto
        {
            Items =
            {
                new BatchExecuteTestItemDto
                {
                    TestRunItemId = run.Items[0].Id,
                    Status = TestResultStatus.Failed,
                    Defects = { new AddDefectLinkDto { ExternalSystem = "Jira", IssueKey = "BUG-5" } },
                },
                new BatchExecuteTestItemDto { TestRunItemId = run.Items[1].Id, Status = TestResultStatus.Passed },
            },
        });

        results[0].DefectLinks.Single().IssueKey.ShouldBe("BUG-5");
        results[1].DefectLinks.ShouldBeEmpty();
    }
}
