using Acme.TestCaseManagement.Enums;
using Acme.TestCaseManagement.Plans;
using Acme.TestCaseManagement.Plans.Dtos;
using Acme.TestCaseManagement.Requirements;
using Acme.TestCaseManagement.Requirements.Dtos;
using Acme.TestCaseManagement.Rtm;
using Acme.TestCaseManagement.Rtm.Dtos;
using Acme.TestCaseManagement.Runs;
using Acme.TestCaseManagement.Runs.Dtos;
using Acme.TestCaseManagement.Suites;
using Acme.TestCaseManagement.Suites.Dtos;
using Acme.TestCaseManagement.TestCases;
using Acme.TestCaseManagement.TestCases.Dtos;
using Shouldly;
using Xunit;

namespace Acme.TestCaseManagement;

public class RtmAppService_Tests : TestCaseManagementApplicationTestBase
{
    private readonly ITestSuiteAppService _suites;
    private readonly ITestCaseAppService _testCases;
    private readonly ITestPlanAppService _plans;
    private readonly ITestRunAppService _runs;
    private readonly IRequirementAppService _requirements;
    private readonly IRtmAppService _rtm;

    private Guid? _suiteId;

    public RtmAppService_Tests()
    {
        _suites = GetRequiredService<ITestSuiteAppService>();
        _testCases = GetRequiredService<ITestCaseAppService>();
        _plans = GetRequiredService<ITestPlanAppService>();
        _runs = GetRequiredService<ITestRunAppService>();
        _requirements = GetRequiredService<IRequirementAppService>();
        _rtm = GetRequiredService<IRtmAppService>();
    }

    private async Task<TestCaseDto> CreateTestCaseAsync(string code, bool approve = true)
    {
        _suiteId ??= (await _suites.CreateAsync(new CreateTestSuiteDto { Name = "Library" })).Id;

        var created = await _testCases.CreateAsync(new CreateUpdateTestCaseDto
        {
            SuiteId = _suiteId.Value,
            Code = code,
            Title = $"Title of {code}",
            Steps = { new TestStepDto { Action = "Do", ExpectedResult = "Done" } },
        });

        return approve
            ? await _testCases.ChangeStatusAsync(created.Id, new ChangeTestCaseStatusDto { TargetStatus = TestCaseStatus.Approved })
            : created;
    }

    private async Task<RequirementDto> CreateRequirementAsync(string code, Guid? milestoneId = null, params Guid[] testCaseIds)
    {
        var requirement = await _requirements.CreateAsync(new CreateUpdateRequirementDto
        {
            Code = code,
            Title = $"Title of {code}",
            MilestoneId = milestoneId,
        });

        if (testCaseIds.Length > 0)
        {
            await _requirements.LinkTestCasesAsync(requirement.Id, new LinkTestCasesDto { TestCaseIds = testCaseIds.ToList() });
        }

        return requirement;
    }

    /// <summary>Runs the test case once on an environment and returns the execution.</summary>
    private async Task<TestExecutionDto> RunAsync(
        Guid testCaseId, TestResultStatus status, string environment = "Staging", Guid? planId = null, string? runTitle = null)
    {
        var run = await _runs.CreateAsync(new CreateTestRunDto
        {
            Title = runTitle ?? $"Run on {environment}",
            Environment = environment,
            TestPlanId = planId,
            TestCaseIds = { testCaseId },
        });

        return await _runs.ExecuteItemAsync(run.Id, run.Items.Single().Id, new ExecuteTestItemDto { Status = status });
    }

    private async Task<RequirementCoverageDto> RowAsync(string code, GetRtmInput? input = null)
    {
        var matrix = await _rtm.GetMatrixAsync(input ?? new GetRtmInput());
        return matrix.Requirements.Single(r => r.Code == code);
    }

    [Fact]
    public async Task Us4_Independent_Test_Three_Requirements_Two_Linked_The_Third_Is_Uncovered()
    {
        var auth5 = await CreateTestCaseAsync("TC-AUTH-005");
        var auth6 = await CreateTestCaseAsync("TC-AUTH-006");
        var other = await CreateTestCaseAsync("TC-OTHER-001");
        await CreateRequirementAsync("REQ-AUTH-01", null, auth5.Id, auth6.Id);
        await CreateRequirementAsync("REQ-PAY-01", null, other.Id);
        await CreateRequirementAsync("REQ-ORDER-01");

        var matrix = await _rtm.GetMatrixAsync(new GetRtmInput());

        matrix.Summary.TotalRequirements.ShouldBe(3);
        matrix.Summary.CoveredRequirements.ShouldBe(2);
        matrix.Summary.UncoveredRequirements.ShouldBe(1);
        matrix.Summary.CoveragePercentage.ShouldBe(66.67);

        var uncovered = matrix.Requirements.Single(r => r.Code == "REQ-ORDER-01");
        uncovered.Status.ShouldBe(RequirementCoverageStatus.Uncovered);
        uncovered.TestCases.ShouldBeEmpty();

        var covered = matrix.Requirements.Single(r => r.Code == "REQ-AUTH-01");
        covered.TestCases.Select(t => t.Code).ShouldBe(new[] { "TC-AUTH-005", "TC-AUTH-006" });
    }

    [Fact]
    public async Task Requirement_Status_Reflects_The_Execution_Outcomes_Of_Its_Linked_Test_Cases()
    {
        var auth5 = await CreateTestCaseAsync("TC-AUTH-005");
        var auth6 = await CreateTestCaseAsync("TC-AUTH-006");
        await CreateRequirementAsync("REQ-AUTH-01", null, auth5.Id, auth6.Id);

        (await RowAsync("REQ-AUTH-01")).Status.ShouldBe(RequirementCoverageStatus.NotRun);

        await RunAsync(auth5.Id, TestResultStatus.Passed);
        var half = await RowAsync("REQ-AUTH-01");
        half.Status.ShouldBe(RequirementCoverageStatus.NotRun); // TC-AUTH-006 has not been executed
        half.TestCases.Single(t => t.Code == "TC-AUTH-005").Result.ShouldBe(TestResultStatus.Passed);
        half.TestCases.Single(t => t.Code == "TC-AUTH-006").Result.ShouldBe(TestResultStatus.Untested);

        await RunAsync(auth6.Id, TestResultStatus.Passed);
        (await RowAsync("REQ-AUTH-01")).Status.ShouldBe(RequirementCoverageStatus.Passed);

        await RunAsync(auth6.Id, TestResultStatus.Failed); // a later regression run
        (await RowAsync("REQ-AUTH-01")).Status.ShouldBe(RequirementCoverageStatus.Failed);
    }

    [Fact]
    public async Task Summary_Counts_Statuses_And_Uses_Total_Requirements_As_The_Denominator()
    {
        var passing = await CreateTestCaseAsync("TC-P");
        var failing = await CreateTestCaseAsync("TC-F");
        var blocked = await CreateTestCaseAsync("TC-B");
        var notRun = await CreateTestCaseAsync("TC-N");
        await CreateRequirementAsync("R1", null, passing.Id);
        await CreateRequirementAsync("R2", null, failing.Id);
        await CreateRequirementAsync("R3", null, blocked.Id);
        await CreateRequirementAsync("R4", null, notRun.Id);
        await CreateRequirementAsync("R5"); // uncovered
        await RunAsync(passing.Id, TestResultStatus.Passed);
        await RunAsync(failing.Id, TestResultStatus.Failed);
        await RunAsync(blocked.Id, TestResultStatus.Blocked);

        var summary = (await _rtm.GetMatrixAsync(new GetRtmInput())).Summary;

        summary.TotalRequirements.ShouldBe(5);
        summary.CoveredRequirements.ShouldBe(4);
        summary.CoveragePercentage.ShouldBe(80);
        summary.PassedRequirements.ShouldBe(1);
        summary.PassedPercentage.ShouldBe(20);
        summary.FailedRequirements.ShouldBe(1);
        summary.BlockedRequirements.ShouldBe(1);
        summary.NotRunRequirements.ShouldBe(1);
        summary.UncoveredRequirements.ShouldBe(1);
    }

    [Fact]
    public async Task A_Draft_Test_Case_Counts_As_Covered_But_The_Requirement_Is_Not_Run()
    {
        var draft = await CreateTestCaseAsync("TC-DRAFT", approve: false);
        await CreateRequirementAsync("R1", null, draft.Id);

        var row = await RowAsync("R1");

        row.Status.ShouldBe(RequirementCoverageStatus.NotRun);
        row.TestCases.Single().CountsTowardCoverage.ShouldBeTrue();
    }

    [Fact]
    public async Task A_Deprecated_Test_Case_Is_Listed_But_Ignored_By_The_Coverage_Rules()
    {
        var active = await CreateTestCaseAsync("TC-ACTIVE");
        var retired = await CreateTestCaseAsync("TC-RETIRED");
        await RunAsync(retired.Id, TestResultStatus.Failed);
        await _testCases.ChangeStatusAsync(retired.Id, new ChangeTestCaseStatusDto { TargetStatus = TestCaseStatus.Deprecated });
        await CreateRequirementAsync("ONLY-RETIRED", null, retired.Id);
        await CreateRequirementAsync("BOTH", null, active.Id, retired.Id);
        await RunAsync(active.Id, TestResultStatus.Passed);

        var onlyRetired = await RowAsync("ONLY-RETIRED");
        onlyRetired.Status.ShouldBe(RequirementCoverageStatus.Uncovered);
        onlyRetired.TestCases.Single().CountsTowardCoverage.ShouldBeFalse();

        var both = await RowAsync("BOTH");
        both.Status.ShouldBe(RequirementCoverageStatus.Passed); // the retired test's failure no longer counts
        both.TestCases.Count.ShouldBe(2);
        both.BlockingDefects.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_Failure_In_Any_Environment_Fails_The_Requirement_And_The_Environment_Filter_Narrows_The_Scope()
    {
        var testCase = await CreateTestCaseAsync("TC-1");
        await CreateRequirementAsync("R1", null, testCase.Id);
        await RunAsync(testCase.Id, TestResultStatus.Passed, "Chrome");
        await RunAsync(testCase.Id, TestResultStatus.Failed, "Safari");

        (await RowAsync("R1")).Status.ShouldBe(RequirementCoverageStatus.Failed);
        (await RowAsync("R1", new GetRtmInput { Environment = "chrome" })).Status.ShouldBe(RequirementCoverageStatus.Passed);
        (await RowAsync("R1", new GetRtmInput { Environment = "SAFARI" })).Status.ShouldBe(RequirementCoverageStatus.Failed);
        (await RowAsync("R1", new GetRtmInput { Environment = "Firefox" })).Status.ShouldBe(RequirementCoverageStatus.NotRun);

        // Re-testing Safari after the fix clears the failure.
        await RunAsync(testCase.Id, TestResultStatus.Passed, "safari");
        (await RowAsync("R1")).Status.ShouldBe(RequirementCoverageStatus.Passed);
    }

    [Fact]
    public async Task The_Test_Plan_Filter_Counts_Only_Results_From_Runs_Of_That_Plan()
    {
        var plan23 = await _plans.CreateAsync(new CreateTestPlanDto { Name = "Sprint 23" });
        var plan24 = await _plans.CreateAsync(new CreateTestPlanDto { Name = "Sprint 24" });
        var testCase = await CreateTestCaseAsync("TC-1");
        await CreateRequirementAsync("R1", null, testCase.Id);
        await RunAsync(testCase.Id, TestResultStatus.Failed, planId: plan23.Id);
        await RunAsync(testCase.Id, TestResultStatus.Passed, planId: plan24.Id);

        (await RowAsync("R1", new GetRtmInput { TestPlanId = plan23.Id })).Status.ShouldBe(RequirementCoverageStatus.Failed);
        (await RowAsync("R1", new GetRtmInput { TestPlanId = plan24.Id })).Status.ShouldBe(RequirementCoverageStatus.Passed);
        (await RowAsync("R1")).Status.ShouldBe(RequirementCoverageStatus.Passed); // newest result overall
    }

    [Fact]
    public async Task Blocking_Defects_Are_The_Unresolved_Defects_Of_The_Requirements_Test_Cases()
    {
        var testCase = await CreateTestCaseAsync("TC-1");
        var unrelated = await CreateTestCaseAsync("TC-2");
        await CreateRequirementAsync("R1", null, testCase.Id);
        var run = await _runs.CreateAsync(new CreateTestRunDto
        {
            Title = "R", Environment = "Staging", TestCaseIds = { testCase.Id, unrelated.Id },
        });

        var failed = await _runs.ExecuteItemAsync(run.Id, run.Items[0].Id, new ExecuteTestItemDto
        {
            Status = TestResultStatus.Failed,
            Defects = { new AddDefectLinkDto { ExternalSystem = "Jira", IssueKey = "BUG-88", IssueUrl = "https://acme.test/BUG-88", Severity = SeverityLevel.High } },
        });
        // A defect on a test case that is not linked to the requirement must not show up.
        var otherFailure = await _runs.ExecuteItemAsync(run.Id, run.Items[1].Id, new ExecuteTestItemDto { Status = TestResultStatus.Failed });
        await _runs.AddDefectLinkAsync(otherFailure.Id, new AddDefectLinkDto { ExternalSystem = "Jira", IssueKey = "BUG-99" });

        var row = await RowAsync("R1");
        row.Status.ShouldBe(RequirementCoverageStatus.Failed);
        var defect = row.BlockingDefects.Single();
        defect.IssueKey.ShouldBe("BUG-88");
        defect.Severity.ShouldBe(SeverityLevel.High);
        defect.TestCaseCode.ShouldBe("TC-1");
        defect.TestExecutionId.ShouldBe(failed.Id);
    }

    [Fact]
    public async Task A_Defect_Keeps_Blocking_After_A_Passing_Retest_Until_It_Is_Marked_Resolved()
    {
        var testCase = await CreateTestCaseAsync("TC-1");
        await CreateRequirementAsync("R1", null, testCase.Id);
        var run = await _runs.CreateAsync(new CreateTestRunDto { Title = "R", Environment = "Staging", TestCaseIds = { testCase.Id } });
        var itemId = run.Items.Single().Id;

        var failed = await _runs.ExecuteItemAsync(run.Id, itemId, new ExecuteTestItemDto
        {
            Status = TestResultStatus.Failed,
            Defects = { new AddDefectLinkDto { ExternalSystem = "Jira", IssueKey = "BUG-88" } },
        });
        await _runs.ExecuteItemAsync(run.Id, itemId, new ExecuteTestItemDto { Status = TestResultStatus.Passed });

        // The retest passed, so the requirement is Passed, but nobody has confirmed the ticket is closed.
        var afterRetest = await RowAsync("R1");
        afterRetest.Status.ShouldBe(RequirementCoverageStatus.Passed);
        afterRetest.BlockingDefects.Single().IssueKey.ShouldBe("BUG-88");

        await _runs.UpdateDefectLinkAsync(failed.Id, failed.DefectLinks.Single().Id,
            new UpdateDefectLinkDto { Severity = SeverityLevel.Medium, IsResolved = true });

        (await RowAsync("R1")).BlockingDefects.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_Defect_From_An_Earlier_Failure_Is_Not_Lost_When_A_Later_Attempt_Fails_Again()
    {
        var testCase = await CreateTestCaseAsync("TC-1");
        await CreateRequirementAsync("R1", null, testCase.Id);
        var run = await _runs.CreateAsync(new CreateTestRunDto { Title = "R", Environment = "Staging", TestCaseIds = { testCase.Id } });
        var itemId = run.Items.Single().Id;

        await _runs.ExecuteItemAsync(run.Id, itemId, new ExecuteTestItemDto
        {
            Status = TestResultStatus.Failed,
            Defects = { new AddDefectLinkDto { ExternalSystem = "Jira", IssueKey = "BUG-1" } },
        });
        await _runs.ExecuteItemAsync(run.Id, itemId, new ExecuteTestItemDto
        {
            Status = TestResultStatus.Failed, // the fix did not work, or something else broke
            Defects = { new AddDefectLinkDto { ExternalSystem = "Jira", IssueKey = "BUG-2" } },
        });

        (await RowAsync("R1")).BlockingDefects.Select(d => d.IssueKey).ShouldBe(new[] { "BUG-1", "BUG-2" });
    }

    [Fact]
    public async Task Blocking_Defects_Follow_The_Environment_And_Plan_Filters_And_Ignore_Deprecated_Test_Cases()
    {
        var plan = await _plans.CreateAsync(new CreateTestPlanDto { Name = "Sprint 24" });
        var testCase = await CreateTestCaseAsync("TC-1");
        var retired = await CreateTestCaseAsync("TC-OLD");
        await CreateRequirementAsync("R1", null, testCase.Id, retired.Id);

        var chrome = await RunAsync(testCase.Id, TestResultStatus.Failed, "Chrome", plan.Id);
        await _runs.AddDefectLinkAsync(chrome.Id, new AddDefectLinkDto { ExternalSystem = "Jira", IssueKey = "CHROME-1" });
        var safari = await RunAsync(testCase.Id, TestResultStatus.Failed, "Safari");
        await _runs.AddDefectLinkAsync(safari.Id, new AddDefectLinkDto { ExternalSystem = "Jira", IssueKey = "SAFARI-1" });
        var old = await RunAsync(retired.Id, TestResultStatus.Failed, "Chrome");
        await _runs.AddDefectLinkAsync(old.Id, new AddDefectLinkDto { ExternalSystem = "Jira", IssueKey = "OLD-1" });
        await _testCases.ChangeStatusAsync(retired.Id, new ChangeTestCaseStatusDto { TargetStatus = TestCaseStatus.Deprecated });

        (await RowAsync("R1")).BlockingDefects.Select(d => d.IssueKey).ShouldBe(new[] { "CHROME-1", "SAFARI-1" });
        (await RowAsync("R1", new GetRtmInput { Environment = "safari" })).BlockingDefects.Single().IssueKey.ShouldBe("SAFARI-1");
        (await RowAsync("R1", new GetRtmInput { TestPlanId = plan.Id })).BlockingDefects.Single().IssueKey.ShouldBe("CHROME-1");
    }

    [Fact]
    public async Task Filters_Limit_The_Rows_But_The_Summary_Ignores_The_Status_Filter_And_Paging()
    {
        var milestone = Guid.NewGuid();
        var testCase = await CreateTestCaseAsync("TC-1");
        await CreateRequirementAsync("AUTH-1", milestone, testCase.Id);
        await CreateRequirementAsync("AUTH-2", milestone);
        await CreateRequirementAsync("PAY-1", Guid.NewGuid());
        await RunAsync(testCase.Id, TestResultStatus.Passed);

        var byMilestone = await _rtm.GetMatrixAsync(new GetRtmInput { MilestoneId = milestone });
        byMilestone.Summary.TotalRequirements.ShouldBe(2);

        var byText = await _rtm.GetMatrixAsync(new GetRtmInput { Filter = "pay" });
        byText.Requirements.Single().Code.ShouldBe("PAY-1");

        var byStatus = await _rtm.GetMatrixAsync(new GetRtmInput { Status = RequirementCoverageStatus.Uncovered });
        byStatus.TotalCount.ShouldBe(2);
        byStatus.Requirements.Select(r => r.Code).ShouldBe(new[] { "AUTH-2", "PAY-1" });
        byStatus.Summary.TotalRequirements.ShouldBe(3);

        var page = await _rtm.GetMatrixAsync(new GetRtmInput { SkipCount = 1, MaxResultCount = 1 });
        page.Requirements.Single().Code.ShouldBe("AUTH-2");
        page.TotalCount.ShouldBe(3);
        page.Summary.TotalRequirements.ShouldBe(3);
    }

    [Fact]
    public async Task An_Empty_Inventory_Gives_An_Empty_Matrix_With_Zero_Percentages()
    {
        var matrix = await _rtm.GetMatrixAsync(new GetRtmInput());

        matrix.Requirements.ShouldBeEmpty();
        matrix.Summary.TotalRequirements.ShouldBe(0);
        matrix.Summary.CoveragePercentage.ShouldBe(0);
        matrix.Summary.PassedPercentage.ShouldBe(0);
    }

    [Fact]
    public async Task Deleted_Requirements_And_Unlinked_Test_Cases_Disappear_From_The_Matrix()
    {
        var testCase = await CreateTestCaseAsync("TC-1");
        var requirement = await CreateRequirementAsync("R1", null, testCase.Id);
        var other = await CreateRequirementAsync("R2", null, testCase.Id);

        await _requirements.UnlinkTestCaseAsync(requirement.Id, testCase.Id);
        (await RowAsync("R1")).Status.ShouldBe(RequirementCoverageStatus.Uncovered);

        // Linking again restores it.
        await _requirements.LinkTestCasesAsync(requirement.Id, new LinkTestCasesDto { TestCaseIds = { testCase.Id } });
        (await RowAsync("R1")).Status.ShouldBe(RequirementCoverageStatus.NotRun);

        await _requirements.DeleteAsync(other.Id);
        (await _rtm.GetMatrixAsync(new GetRtmInput())).Requirements.Select(r => r.Code).ShouldBe(new[] { "R1" });
    }
}
