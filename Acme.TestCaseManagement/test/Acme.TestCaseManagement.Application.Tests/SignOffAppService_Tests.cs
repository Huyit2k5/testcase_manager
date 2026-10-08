using Acme.TestCaseManagement.Enums;
using Acme.TestCaseManagement.Plans;
using Acme.TestCaseManagement.Plans.Dtos;
using Acme.TestCaseManagement.Quality;
using Acme.TestCaseManagement.QualityGates;
using Acme.TestCaseManagement.QualityGates.Dtos;
using Acme.TestCaseManagement.Runs;
using Acme.TestCaseManagement.Runs.Dtos;
using Acme.TestCaseManagement.SignOff;
using Acme.TestCaseManagement.SignOff.Dtos;
using Acme.TestCaseManagement.Suites;
using Acme.TestCaseManagement.Suites.Dtos;
using Acme.TestCaseManagement.TestCases;
using Acme.TestCaseManagement.TestCases.Dtos;
using Shouldly;
using Volo.Abp;
using Volo.Abp.Domain.Entities;
using Volo.Abp.DistributedLocking;
using Xunit;

namespace Acme.TestCaseManagement;

public class SignOffAppService_Tests : TestCaseManagementApplicationTestBase
{
    private readonly ISignOffAppService _signOff;
    private readonly IQualityGateAppService _gates;
    private readonly ITestSuiteAppService _suites;
    private readonly ITestCaseAppService _testCases;
    private readonly ITestPlanAppService _plans;
    private readonly ITestRunAppService _runs;

    private readonly Guid _qaLead = Guid.NewGuid();
    private readonly Guid _productOwner = Guid.NewGuid();

    private Guid? _suiteId;

    public SignOffAppService_Tests()
    {
        _signOff = GetRequiredService<ISignOffAppService>();
        _gates = GetRequiredService<IQualityGateAppService>();
        _suites = GetRequiredService<ITestSuiteAppService>();
        _testCases = GetRequiredService<ITestCaseAppService>();
        _plans = GetRequiredService<ITestPlanAppService>();
        _runs = GetRequiredService<ITestRunAppService>();
    }

    /// <summary>A plan with one run of approved test cases whose results are given in order.</summary>
    private async Task<(TestPlanDto Plan, TestRunDto Run)> PlanWithResultsAsync(
        string name, IReadOnlyList<TestResultStatus> results, Guid? milestoneId = null)
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
            batch.Items.Add(new BatchExecuteTestItemDto { TestRunItemId = run.Items[i].Id, Status = results[i] });
        }

        await _runs.BatchExecuteAsync(run.Id, batch);
        return (plan, await _runs.GetAsync(run.Id));
    }

    private static List<TestResultStatus> Results(int passed, int failed = 0) =>
        Enumerable.Repeat(TestResultStatus.Passed, passed)
            .Concat(Enumerable.Repeat(TestResultStatus.Failed, failed))
            .ToList();

    private Task<TestExecutionDto> RetestAsync(TestRunDto run, int itemIndex, TestResultStatus status) =>
        _runs.ExecuteItemAsync(run.Id, run.Items[itemIndex].Id, new ExecuteTestItemDto { Status = status });

    [Fact]
    public async Task Us5_Independent_Test_Blocked_At_90_Percent_Then_Signed_Off_By_Two_Users_After_The_Fix()
    {
        var release = await _gates.CreateAsync(new CreateUpdateQualityGateDto
        {
            Name = "Release", MinPassRate = 95m, RequiredApprovals = 2, IsDefault = true,
        });
        var milestone = Guid.NewGuid();
        var (plan, run) = await PlanWithResultsAsync("Sprint 24", Results(passed: 18, failed: 2), milestone);

        // Scenario 1: 90% against a 95% gate is blocked, with the breakdown of what failed.
        BusinessException blocked;
        using (ChangeUser(_qaLead, "qa.lead"))
        {
            blocked = await Should.ThrowAsync<BusinessException>(
                () => _signOff.SignOffAsync(new StartSignOffDto { TestPlanId = plan.Id }));
        }

        blocked.Code.ShouldBe(TestCaseManagementErrorCodes.QualityGateNotPassed);
        blocked.Details.ShouldBe("PassRate 90 (required >= 95)");
        blocked.Data["GateName"].ShouldBe("Release");
        (await _signOff.GetListAsync(new GetSignOffListInput())).TotalCount.ShouldBe(0);

        var evaluation = await _gates.EvaluateAsync(new EvaluateQualityGateInput { TestPlanId = plan.Id });
        evaluation.Passed.ShouldBeFalse();
        evaluation.Gate.Id.ShouldBe(release.Id);
        evaluation.Criteria.Where(c => !c.Passed).Select(c => c.Code).ShouldBe(new[] { QualityGateCriteria.PassRate });

        // The failing test is fixed and re-tested: 19 of 20 = exactly 95%.
        await RetestAsync(run, 18, TestResultStatus.Passed);

        // Scenario 2: the QA Lead and the Product Owner approve; the report freezes the statistics.
        SignOffReportDto pending;
        using (ChangeUser(_qaLead, "qa.lead"))
        {
            pending = await _signOff.SignOffAsync(new StartSignOffDto
            {
                TestPlanId = plan.Id, ApproverRole = "QA Lead", Comment = "Regression complete",
            });
        }

        pending.Status.ShouldBe(SignOffStatus.Pending);
        pending.RequiredApprovals.ShouldBe(2);
        pending.QualityGateId.ShouldBe(release.Id);
        pending.TestPlanId.ShouldBe(plan.Id);
        pending.Title.ShouldBe("Sign-off: Sprint 24");
        pending.Approvals.Single().ApproverName.ShouldBe("qa.lead");

        SignOffReportDto approved;
        using (ChangeUser(_productOwner, "product.owner"))
        {
            approved = await _signOff.ApproveAsync(pending.Id, new ApproveSignOffDto { ApproverRole = "Product Owner", Comment = "Accepted" });
        }

        approved.Id.ShouldBe(pending.Id);
        approved.Status.ShouldBe(SignOffStatus.Approved);
        approved.ApprovedTime.ShouldNotBeNull();
        approved.Approvals.Select(a => (a.ApproverRole ?? string.Empty, a.ApproverName)).ShouldBe(new[]
        {
            ("QA Lead", "qa.lead"), ("Product Owner", "product.owner"),
        });
        approved.IntegrityVerified.ShouldBeTrue();
        approved.SnapshotHash.Length.ShouldBe(SignOffConsts.HashLength);
        approved.Approvals.ShouldAllBe(a => a.Signature.Length == SignOffConsts.HashLength);

        var summary = approved.Summary.ShouldNotBeNull();
        summary.Passed.ShouldBeTrue();
        summary.Metrics.PassRate.ShouldBe(95m);
        (summary.Metrics.TotalItems, summary.Metrics.Passed, summary.Metrics.Failed).ShouldBe((20, 19, 1));
        summary.Gate.Name.ShouldBe("Release");
        summary.Scope.Plans.Single().Name.ShouldBe("Sprint 24");
        approved.SummaryStatsJson.ShouldContain("\"passRate\":95");
    }

    [Fact]
    public async Task The_Approved_Report_Stays_Frozen_When_The_Live_Data_And_The_Gate_Change_Later()
    {
        var gate = await _gates.CreateAsync(new CreateUpdateQualityGateDto { Name = "Single", MinPassRate = 90m, RequiredApprovals = 1, IsDefault = true });
        var (plan, run) = await PlanWithResultsAsync("Sprint 24", Results(passed: 10));

        SignOffReportDto report;
        using (ChangeUser(_qaLead, "qa.lead"))
        {
            report = await _signOff.SignOffAsync(new StartSignOffDto { TestPlanId = plan.Id });
        }

        report.Status.ShouldBe(SignOffStatus.Approved); // one approval is enough for this gate
        var frozenJson = report.SummaryStatsJson;
        var frozenHash = report.SnapshotHash;

        // Afterwards: a test regresses, a critical defect appears, and the gate is relaxed.
        var failure = await RetestAsync(run, 0, TestResultStatus.Failed);
        await _runs.AddDefectLinkAsync(failure.Id, new AddDefectLinkDto { ExternalSystem = "Jira", IssueKey = "BUG-9", Severity = SeverityLevel.Critical });
        await _gates.UpdateAsync(gate.Id, new CreateUpdateQualityGateDto { Name = "Single", MinPassRate = 10m, RequiredApprovals = 1, IsDefault = true });

        var reloaded = await _signOff.GetAsync(report.Id);

        reloaded.SummaryStatsJson.ShouldBe(frozenJson);
        reloaded.SnapshotHash.ShouldBe(frozenHash);
        reloaded.IntegrityVerified.ShouldBeTrue();
        reloaded.MinPassRate.ShouldBe(90m); // the thresholds it was signed against
        reloaded.Summary!.Metrics.PassRate.ShouldBe(100m);
        reloaded.Summary.Metrics.OpenDefects.Critical.ShouldBe(0);

        // A fresh evaluation, in contrast, sees the regression.
        (await _gates.EvaluateAsync(new EvaluateQualityGateInput { TestPlanId = plan.Id })).Metrics.OpenDefects.Critical.ShouldBe(1);
    }

    [Fact]
    public async Task Sign_Off_Requires_An_Authenticated_User()
    {
        var (plan, _) = await PlanWithResultsAsync("Sprint 24", Results(passed: 10));

        (await Should.ThrowAsync<BusinessException>(() => _signOff.SignOffAsync(new StartSignOffDto { TestPlanId = plan.Id })))
            .Code.ShouldBe(TestCaseManagementErrorCodes.SignOffRequiresUser);
    }

    [Fact]
    public async Task A_User_Cannot_Approve_Twice_And_A_Finished_Report_Accepts_No_More_Approvals()
    {
        var (plan, _) = await PlanWithResultsAsync("Sprint 24", Results(passed: 10));
        await _gates.CreateAsync(new CreateUpdateQualityGateDto { Name = "Two", MinPassRate = 95m, RequiredApprovals = 2, IsDefault = true });

        SignOffReportDto report;
        using (ChangeUser(_qaLead, "qa.lead"))
        {
            report = await _signOff.SignOffAsync(new StartSignOffDto { TestPlanId = plan.Id });

            (await Should.ThrowAsync<BusinessException>(() => _signOff.ApproveAsync(report.Id, new ApproveSignOffDto())))
                .Code.ShouldBe(TestCaseManagementErrorCodes.DuplicateSignOffApproval);
        }

        using (ChangeUser(_productOwner, "product.owner"))
        {
            (await _signOff.ApproveAsync(report.Id, new ApproveSignOffDto())).Status.ShouldBe(SignOffStatus.Approved);
        }

        using (ChangeUser(Guid.NewGuid(), "late.reviewer"))
        {
            (await Should.ThrowAsync<BusinessException>(() => _signOff.ApproveAsync(report.Id, new ApproveSignOffDto())))
                .Code.ShouldBe(TestCaseManagementErrorCodes.SignOffNotPending);
        }

        (await _signOff.GetAsync(report.Id)).Approvals.Count.ShouldBe(2);
    }

    [Fact]
    public async Task The_Second_Approval_Is_Refused_When_A_Critical_Defect_Was_Linked_After_The_First()
    {
        var (plan, run) = await PlanWithResultsAsync("Sprint 24", Results(passed: 10));

        SignOffReportDto report;
        using (ChangeUser(_qaLead, "qa.lead"))
        {
            report = await _signOff.SignOffAsync(new StartSignOffDto { TestPlanId = plan.Id });
        }

        // A defect is found in a test that was passing: it fails again and gets a Critical ticket.
        var failure = await RetestAsync(run, 0, TestResultStatus.Failed);
        var defect = await _runs.AddDefectLinkAsync(failure.Id, new AddDefectLinkDto
        {
            ExternalSystem = "Jira", IssueKey = "BUG-1", Severity = SeverityLevel.Critical,
        });
        await RetestAsync(run, 0, TestResultStatus.Passed); // fixed and re-tested; the ticket is still open

        using (ChangeUser(_productOwner, "product.owner"))
        {
            var refusal = await Should.ThrowAsync<BusinessException>(() => _signOff.ApproveAsync(report.Id, new ApproveSignOffDto()));
            refusal.Code.ShouldBe(TestCaseManagementErrorCodes.QualityGateNotPassed);
            refusal.Details.ShouldBe("OpenCriticalDefects 1 (required <= 0)");
            (await _signOff.GetAsync(report.Id)).Status.ShouldBe(SignOffStatus.Pending);

            await _runs.UpdateDefectLinkAsync(failure.Id, defect.Id, new UpdateDefectLinkDto { Severity = SeverityLevel.Critical, IsResolved = true });

            (await _signOff.ApproveAsync(report.Id, new ApproveSignOffDto())).Status.ShouldBe(SignOffStatus.Approved);
        }
    }

    [Fact]
    public async Task Starting_Again_Supersedes_The_Pending_Report_Of_The_Same_Plan()
    {
        var (plan, _) = await PlanWithResultsAsync("Sprint 24", Results(passed: 10));

        SignOffReportDto first;
        SignOffReportDto second;
        using (ChangeUser(_qaLead, "qa.lead"))
        {
            first = await _signOff.SignOffAsync(new StartSignOffDto { TestPlanId = plan.Id, Title = "First attempt" });
            second = await _signOff.SignOffAsync(new StartSignOffDto { TestPlanId = plan.Id, Title = "Second attempt" });
        }

        (await _signOff.GetAsync(first.Id)).Status.ShouldBe(SignOffStatus.Superseded);
        (await _signOff.GetAsync(second.Id)).Status.ShouldBe(SignOffStatus.Pending);

        using (ChangeUser(_productOwner, "product.owner"))
        {
            (await Should.ThrowAsync<BusinessException>(() => _signOff.ApproveAsync(first.Id, new ApproveSignOffDto())))
                .Code.ShouldBe(TestCaseManagementErrorCodes.SignOffNotPending);
        }
    }

    [Fact]
    public async Task A_Milestone_Can_Be_Signed_Off_As_A_Whole()
    {
        var milestone = Guid.NewGuid();
        await PlanWithResultsAsync("Sprint 23", Results(passed: 10), milestone);
        await PlanWithResultsAsync("Sprint 24", Results(passed: 10), milestone);

        SignOffReportDto report;
        using (ChangeUser(_qaLead, "qa.lead"))
        {
            report = await _signOff.SignOffAsync(new StartSignOffDto { MilestoneId = milestone, Title = "Release 2.0" });
        }

        report.MilestoneId.ShouldBe(milestone);
        report.TestPlanId.ShouldBeNull();
        report.Title.ShouldBe("Release 2.0");
        report.Summary!.Scope.Plans.Select(p => p.Name).ShouldBe(new[] { "Sprint 23", "Sprint 24" });
        report.Summary.Metrics.TotalItems.ShouldBe(20);
    }

    [Fact]
    public async Task Sign_Off_Rejects_A_Missing_Scope_An_Unknown_Plan_And_An_Unknown_Gate()
    {
        var (plan, _) = await PlanWithResultsAsync("Sprint 24", Results(passed: 10));

        using var _ = ChangeUser(_qaLead, "qa.lead");

        (await Should.ThrowAsync<BusinessException>(() => _signOff.SignOffAsync(new StartSignOffDto())))
            .Code.ShouldBe(TestCaseManagementErrorCodes.InvalidSignOffScope);
        (await Should.ThrowAsync<BusinessException>(() => _signOff.SignOffAsync(new StartSignOffDto { TestPlanId = Guid.NewGuid() })))
            .Code.ShouldBe(TestCaseManagementErrorCodes.TestPlanNotFound);
        await Should.ThrowAsync<EntityNotFoundException>(() => _signOff.SignOffAsync(
            new StartSignOffDto { TestPlanId = plan.Id, QualityGateId = Guid.NewGuid() }));
        await Should.ThrowAsync<EntityNotFoundException>(() => _signOff.ApproveAsync(Guid.NewGuid(), new ApproveSignOffDto()));
    }

    [Fact]
    public async Task GetList_Should_Filter_By_Plan_Milestone_And_Status_Newest_First()
    {
        var milestone = Guid.NewGuid();
        var (plan1, _) = await PlanWithResultsAsync("Sprint 23", Results(passed: 10), milestone);
        var (plan2, _) = await PlanWithResultsAsync("Sprint 24", Results(passed: 10));

        using var _ = ChangeUser(_qaLead, "qa.lead");
        var first = await _signOff.SignOffAsync(new StartSignOffDto { TestPlanId = plan1.Id });
        var second = await _signOff.SignOffAsync(new StartSignOffDto { TestPlanId = plan2.Id });
        var milestoneReport = await _signOff.SignOffAsync(new StartSignOffDto { MilestoneId = milestone });

        (await _signOff.GetListAsync(new GetSignOffListInput())).Items.Select(r => r.Id)
            .ShouldBe(new[] { milestoneReport.Id, second.Id, first.Id });
        (await _signOff.GetListAsync(new GetSignOffListInput { TestPlanId = plan2.Id })).Items.Single().Id.ShouldBe(second.Id);
        (await _signOff.GetListAsync(new GetSignOffListInput { MilestoneId = milestone })).Items.Single().Id.ShouldBe(milestoneReport.Id);
        (await _signOff.GetListAsync(new GetSignOffListInput { Status = SignOffStatus.Approved })).TotalCount.ShouldBe(0);
        (await _signOff.GetListAsync(new GetSignOffListInput { Sorting = "creationTime", MaxResultCount = 1 })).Items.Single().Id.ShouldBe(first.Id);
        (await _signOff.GetListAsync(new GetSignOffListInput())).Items.ShouldAllBe(r => r.Approvals.Count == 1 && r.IntegrityVerified);
    }

    [Fact]
    public async Task Two_Approvals_Of_The_Same_Sign_Off_Cannot_Run_At_Once()
    {
        await _gates.CreateAsync(new CreateUpdateQualityGateDto { Name = "Two", MinPassRate = 50m, RequiredApprovals = 2, IsDefault = true });
        var (plan, _) = await PlanWithResultsAsync("LOCK", Results(2));
        SignOffReportDto report;
        using (ChangeUser(_qaLead)) report = await _signOff.SignOffAsync(new StartSignOffDto { TestPlanId = plan.Id });
        report.Status.ShouldBe(SignOffStatus.Pending);

        // Another approval of the same report is in progress (its lock is held): this one is told to try again, and changes nothing.
        var held = await GetRequiredService<IAbpDistributedLock>().TryAcquireAsync($"tcm::signoff:{report.Id}");
        held.ShouldNotBeNull();
        using (ChangeUser(_productOwner))
        {
            (await Should.ThrowAsync<BusinessException>(() => _signOff.ApproveAsync(report.Id, new ApproveSignOffDto())))
                .Code.ShouldBe(TestCaseManagementErrorCodes.OperationInProgress);
        }

        (await _signOff.GetAsync(report.Id)).Status.ShouldBe(SignOffStatus.Pending);

        // The lock of an approval that finished is gone: the next one goes through and completes the report.
        await held!.DisposeAsync();
        using (ChangeUser(_productOwner))
        {
            (await _signOff.ApproveAsync(report.Id, new ApproveSignOffDto())).Status.ShouldBe(SignOffStatus.Approved);
        }
    }

    [Fact]
    public async Task Starting_A_Sign_Off_Waits_For_Another_Start_For_The_Same_Plan()
    {
        await _gates.CreateAsync(new CreateUpdateQualityGateDto { Name = "Single", MinPassRate = 50m, RequiredApprovals = 1, IsDefault = true });
        var (plan, _) = await PlanWithResultsAsync("START", Results(2));
        var held = await GetRequiredService<IAbpDistributedLock>().TryAcquireAsync($"tcm::signoff-start:{plan.Id}:");

        using (ChangeUser(_qaLead))
        {
            (await Should.ThrowAsync<BusinessException>(() => _signOff.SignOffAsync(new StartSignOffDto { TestPlanId = plan.Id })))
                .Code.ShouldBe(TestCaseManagementErrorCodes.OperationInProgress);
        }

        await held!.DisposeAsync();
        using (ChangeUser(_qaLead))
        {
            (await _signOff.SignOffAsync(new StartSignOffDto { TestPlanId = plan.Id })).Status.ShouldBe(SignOffStatus.Approved);
        }
    }

    [Fact]
    public async Task Saving_A_Default_Quality_Gate_Waits_For_Another_Save_Of_The_Default_But_Not_A_Plain_Gate()
    {
        var held = await GetRequiredService<IAbpDistributedLock>().TryAcquireAsync("tcm::qualitygate-default");

        (await Should.ThrowAsync<BusinessException>(
                () => _gates.CreateAsync(new CreateUpdateQualityGateDto { Name = "Default A", MinPassRate = 90m, RequiredApprovals = 1, IsDefault = true })))
            .Code.ShouldBe(TestCaseManagementErrorCodes.OperationInProgress);
        (await _gates.CreateAsync(new CreateUpdateQualityGateDto { Name = "Plain", MinPassRate = 90m, RequiredApprovals = 1, IsDefault = false }))
            .IsDefault.ShouldBeFalse();

        await held!.DisposeAsync();
        (await _gates.CreateAsync(new CreateUpdateQualityGateDto { Name = "Default B", MinPassRate = 90m, RequiredApprovals = 1, IsDefault = true }))
            .IsDefault.ShouldBeTrue();
    }
}
