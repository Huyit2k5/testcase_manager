using Acme.TestCaseManagement.Enums;
using Acme.TestCaseManagement.Plans;
using Acme.TestCaseManagement.Runs;
using Acme.TestCaseManagement.TestCases;
using Shouldly;
using Volo.Abp;
using Volo.Abp.Domain.Repositories;
using Xunit;

namespace Acme.TestCaseManagement.Quality;

public class SignOffManager_Tests : QualityTestBase
{
    private readonly SignOffManager _manager;
    private readonly QualityGateManager _gateManager;
    private readonly IRepository<SignOffReport, Guid> _reportRepository;
    private readonly IRepository<QualityGate, Guid> _gateRepository;

    private readonly Guid _qaLead = Guid.NewGuid();
    private readonly Guid _productOwner = Guid.NewGuid();

    public SignOffManager_Tests()
    {
        _manager = GetRequiredService<SignOffManager>();
        _gateManager = GetRequiredService<QualityGateManager>();
        _reportRepository = GetRequiredService<IRepository<SignOffReport, Guid>>();
        _gateRepository = GetRequiredService<IRepository<QualityGate, Guid>>();
    }

    /// <summary>A plan whose tests all passed, plus the attempt log so tests can attach defects. Position 1 failed first and was fixed.</summary>
    private async Task<(TestPlan Plan, TestExecution FirstFailure)> PassingPlanAsync(string name, int count = 10)
    {
        var cases = new List<TestCase>();
        for (var i = 1; i <= count; i++)
        {
            cases.Add(await ApprovedTestCaseAsync($"{name}-TC-{i:00}"));
        }

        var plan = await PlanAsync(name);
        var run = await RunAsync(plan, "Staging", cases.ToArray());
        var firstFailure = await ExecuteAsync(run, 1, TestResultStatus.Failed);
        await ExecuteAsync(run, 1, TestResultStatus.Passed); // re-tested after the fix
        for (var i = 2; i <= count; i++)
        {
            await ExecuteAsync(run, i, TestResultStatus.Passed);
        }

        return (plan, firstFailure);
    }

    private async Task<SignOffReport> StartAndSaveAsync(
        QualityGateScope scope, Guid? gateId = null, string? title = null, string? role = null, string? comment = null)
    {
        var report = await _manager.StartAsync(scope, gateId, title, role, comment);
        return await _reportRepository.InsertAsync(report, autoSave: true);
    }

    private async Task<QualityGate> CreateGateAsync(string name, decimal minPassRate, int requiredApprovals, bool isDefault = false)
    {
        var gate = await _gateManager.CreateAsync(name, minPassRate, requiredApprovals, null, isDefault);
        return await _gateRepository.InsertAsync(gate, autoSave: true);
    }

    [Fact]
    public async Task Start_Should_Require_An_Authenticated_User()
    {
        await WithUnitOfWorkAsync(async () =>
        {
            var (plan, _) = await PassingPlanAsync("Sprint 24");

            (await Should.ThrowAsync<BusinessException>(() => _manager.StartAsync(new QualityGateScope(plan.Id, null), null, null, null, null)))
                .Code.ShouldBe(TestCaseManagementErrorCodes.SignOffRequiresUser);
        });
    }

    [Fact]
    public async Task Start_Should_Be_Refused_With_The_Failed_Criteria_When_The_Gate_Does_Not_Pass()
    {
        await WithUnitOfWorkAsync(async () =>
        {
            var cases = new List<(TestCase, TestResultStatus?)>();
            for (var i = 1; i <= 10; i++)
            {
                cases.Add((await ApprovedTestCaseAsync($"TC-{i:00}"), i == 10 ? TestResultStatus.Failed : TestResultStatus.Passed));
            }

            var (plan, _) = await ExecutedPlanAsync("Sprint 24", cases.ToArray()); // 90%
            using var _ = ChangeUser(_qaLead, "qa.lead");

            var exception = await Should.ThrowAsync<QualityGateNotPassedException>(
                () => _manager.StartAsync(new QualityGateScope(plan.Id, null), null, null, null, null));

            exception.Code.ShouldBe(TestCaseManagementErrorCodes.QualityGateNotPassed);
            exception.Details.ShouldBe("PassRate 90 (required >= 95)");
            exception.Data["FailedCriteria"].ShouldBe("PassRate 90 (required >= 95)");
            exception.Data["GateName"].ShouldBe("Baseline");
            exception.Evaluation.Passed.ShouldBeFalse();
            exception.Evaluation.Criteria.Where(c => !c.Passed).Select(c => c.Code).ShouldBe(new[] { QualityGateCriteria.PassRate });
            (await _reportRepository.CountAsync()).ShouldBe(0);
        });
    }

    [Fact]
    public async Task Start_Should_Freeze_The_Evaluation_And_Record_The_First_Approval_When_The_Gate_Passes()
    {
        await WithUnitOfWorkAsync(async () =>
        {
            var (plan, _) = await PassingPlanAsync("Sprint 24");
            var scope = new QualityGateScope(plan.Id, null);

            SignOffReport report;
            using (ChangeUser(_qaLead, "qa.lead"))
            {
                report = await StartAndSaveAsync(scope, role: "QA Lead", comment: "All green");
            }

            report.Status.ShouldBe(SignOffStatus.Pending);
            report.TestPlanId.ShouldBe(plan.Id);
            report.Title.ShouldBe("Sign-off: Sprint 24");
            report.QualityGateId.ShouldBeNull(); // built-in baseline
            (report.QualityGateName, report.MinPassRate, report.RequiredApprovals).ShouldBe(("Baseline", 95m, 2));

            var approval = report.Approvals.Single();
            (approval.ApproverUserId, approval.ApproverName, approval.ApproverRole, approval.Comment)
                .ShouldBe((_qaLead, "qa.lead", "QA Lead", "All green"));

            var snapshot = SignOffSnapshot.Deserialize(report.SummaryStatsJson);
            snapshot.Evaluation.Passed.ShouldBeTrue();
            snapshot.Evaluation.Metrics.TotalItems.ShouldBe(10);
            snapshot.Evaluation.Metrics.PassRate.ShouldBe(100m);
            snapshot.Evaluation.Scope.Plans.Single().Name.ShouldBe("Sprint 24");
            report.VerifyIntegrity().ShouldBeTrue();

            var stored = await _reportRepository.GetAsync(report.Id);
            stored.Approvals.Count.ShouldBe(1);
            stored.SummaryStatsJson.ShouldBe(report.SummaryStatsJson);
        });
    }

    [Fact]
    public async Task A_Second_Approval_Is_Refused_When_The_Live_Results_Got_Worse_And_The_Snapshot_Stays_Unchanged()
    {
        await WithUnitOfWorkAsync(async () =>
        {
            var (plan, _) = await PassingPlanAsync("Sprint 24");
            var scope = new QualityGateScope(plan.Id, null);

            SignOffReport report;
            using (ChangeUser(_qaLead, "qa.lead"))
            {
                report = await StartAndSaveAsync(scope, title: "Release 2.0 sign-off", role: "QA Lead");
            }

            var frozenJson = report.SummaryStatsJson;
            var frozenHash = report.SnapshotHash;

            // The live data gets worse after the snapshot: a new failing test is added and run.
            var late = await ApprovedTestCaseAsync("TC-LATE");
            var lateRun = await RunAsync(plan, "Staging", late);
            await ExecuteAsync(lateRun, 1, TestResultStatus.Failed);

            using (ChangeUser(_productOwner, "product.owner"))
            {
                // 10 passed + 1 failed = 90.9% now, so the second approval is refused and the report stays Pending.
                await Should.ThrowAsync<QualityGateNotPassedException>(
                    () => _manager.ApproveAsync(report, "Product Owner", null));
            }

            report.Status.ShouldBe(SignOffStatus.Pending);
            report.Approvals.Count.ShouldBe(1);
            report.SummaryStatsJson.ShouldBe(frozenJson);
            report.SnapshotHash.ShouldBe(frozenHash);
            report.Title.ShouldBe("Release 2.0 sign-off");
        });
    }

    [Fact]
    public async Task The_Second_Approval_Is_Accepted_While_The_Gate_Still_Passes_And_Completes_The_Report()
    {
        await WithUnitOfWorkAsync(async () =>
        {
            var (plan, _) = await PassingPlanAsync("Sprint 24");
            var scope = new QualityGateScope(plan.Id, null);

            SignOffReport report;
            using (ChangeUser(_qaLead, "qa.lead"))
            {
                report = await StartAndSaveAsync(scope, role: "QA Lead");

                // The same user cannot count twice.
                (await Should.ThrowAsync<BusinessException>(() => _manager.ApproveAsync(report, "QA Lead", null)))
                    .Code.ShouldBe(TestCaseManagementErrorCodes.DuplicateSignOffApproval);
            }

            using (ChangeUser(_productOwner, "product.owner"))
            {
                var approval = await _manager.ApproveAsync(report, "Product Owner", "Ship it");
                await _reportRepository.UpdateAsync(report, autoSave: true);

                approval.ApproverUserId.ShouldBe(_productOwner);
            }

            var stored = await _reportRepository.GetAsync(report.Id);
            stored.Status.ShouldBe(SignOffStatus.Approved);
            stored.ApprovedTime.ShouldNotBeNull();
            stored.Approvals.Select(a => a.ApproverRole).OrderBy(x => x).ShouldBe(new[] { "Product Owner", "QA Lead" });
            stored.VerifyIntegrity().ShouldBeTrue();

            // Nobody can add to a finished report.
            using (ChangeUser(Guid.NewGuid(), "someone.else"))
            {
                (await Should.ThrowAsync<BusinessException>(() => _manager.ApproveAsync(stored, null, null)))
                    .Code.ShouldBe(TestCaseManagementErrorCodes.SignOffNotPending);
            }
        });
    }

    [Fact]
    public async Task An_Open_Critical_Defect_Linked_After_The_First_Signature_Blocks_The_Next_One_Until_It_Is_Resolved()
    {
        await WithUnitOfWorkAsync(async () =>
        {
            var (plan, firstFailure) = await PassingPlanAsync("Sprint 24");
            var scope = new QualityGateScope(plan.Id, null);

            SignOffReport report;
            using (ChangeUser(_qaLead, "qa.lead"))
            {
                report = await StartAndSaveAsync(scope);
            }

            // The first failure of the fixed test is linked to a Critical ticket after the QA Lead signed.
            var defect = await DefectManager.AddAsync(firstFailure, "Jira", "BUG-1", null, SeverityLevel.Critical);

            using (ChangeUser(_productOwner, "product.owner"))
            {
                var refusal = await Should.ThrowAsync<QualityGateNotPassedException>(
                    () => _manager.ApproveAsync(report, null, null));
                refusal.Details.ShouldBe("OpenCriticalDefects 1 (required <= 0)");

                await DefectManager.UpdateAsync(defect, SeverityLevel.Critical, isResolved: true);

                await _manager.ApproveAsync(report, null, null);
                report.Status.ShouldBe(SignOffStatus.Approved);
            }
        });
    }

    [Fact]
    public async Task Starting_Again_Supersedes_A_Pending_Report_Of_The_Same_Scope_Only()
    {
        await WithUnitOfWorkAsync(async () =>
        {
            var (plan, _) = await PassingPlanAsync("Sprint 24");
            var (otherPlan, _) = await PassingPlanAsync("Sprint 25");
            var single = await CreateGateAsync("Single", 90m, 1);

            using var _ = ChangeUser(_qaLead, "qa.lead");
            var first = await StartAndSaveAsync(new QualityGateScope(plan.Id, null));
            var otherPending = await StartAndSaveAsync(new QualityGateScope(otherPlan.Id, null));
            var approved = await StartAndSaveAsync(new QualityGateScope(plan.Id, null), single.Id);
            // `approved` superseded `first` (same scope), and finished immediately because the gate needs one approval.
            await SaveChangesAsync();

            (await _reportRepository.GetAsync(first.Id)).Status.ShouldBe(SignOffStatus.Superseded);
            (await _reportRepository.GetAsync(otherPending.Id)).Status.ShouldBe(SignOffStatus.Pending);
            (await _reportRepository.GetAsync(approved.Id)).Status.ShouldBe(SignOffStatus.Approved);

            // An approved report is history; starting again does not touch it.
            var again = await StartAndSaveAsync(new QualityGateScope(plan.Id, null), single.Id);
            await SaveChangesAsync();
            (await _reportRepository.GetAsync(approved.Id)).Status.ShouldBe(SignOffStatus.Approved);
            (await _reportRepository.GetAsync(again.Id)).Status.ShouldBe(SignOffStatus.Approved);
            (await _reportRepository.GetAsync(first.Id)).Approvals.Count.ShouldBe(1); // history is kept
        });
    }

    [Fact]
    public async Task A_Milestone_Sign_Off_Covers_All_Plans_Of_The_Milestone()
    {
        await WithUnitOfWorkAsync(async () =>
        {
            var milestone = Guid.NewGuid();
            var a = await ApprovedTestCaseAsync("TC-A");
            var b = await ApprovedTestCaseAsync("TC-B");
            var plan1 = await PlanAsync("Sprint 23", milestone);
            var plan2 = await PlanAsync("Sprint 24", milestone);
            await ExecuteAsync(await RunAsync(plan1, "Staging", a), 1, TestResultStatus.Passed);
            await ExecuteAsync(await RunAsync(plan2, "Staging", b), 1, TestResultStatus.Passed);

            using var _ = ChangeUser(_qaLead, "qa.lead");
            var report = await StartAndSaveAsync(new QualityGateScope(null, milestone));

            report.MilestoneId.ShouldBe(milestone);
            report.TestPlanId.ShouldBeNull();
            report.Title.ShouldBe("Sign-off: Sprint 23, Sprint 24");
            SignOffSnapshot.Deserialize(report.SummaryStatsJson).Evaluation.Scope.Plans.Select(p => p.Name)
                .ShouldBe(new[] { "Sprint 23", "Sprint 24" });
        });
    }

    [Fact]
    public async Task The_Chosen_Gate_Decides_Thresholds_And_Approvals_And_Is_Frozen_In_The_Report()
    {
        await WithUnitOfWorkAsync(async () =>
        {
            var (plan, _) = await PassingPlanAsync("Sprint 24");
            var gate = await CreateGateAsync("Release", 99m, 3, isDefault: true);

            using var _ = ChangeUser(_qaLead, "qa.lead");
            var report = await StartAndSaveAsync(new QualityGateScope(plan.Id, null));

            (report.QualityGateId, report.QualityGateName, report.MinPassRate, report.RequiredApprovals)
                .ShouldBe((gate.Id, "Release", 99m, 3));

            // Editing the gate later does not change what the report was signed against.
            await _gateManager.UpdateAsync(gate, "Release", 50m, 1, null, true);
            await _gateRepository.UpdateAsync(gate, autoSave: true);
            var stored = await _reportRepository.GetAsync(report.Id);
            (stored.MinPassRate, stored.RequiredApprovals).ShouldBe((99m, 3));
        });
    }

    [Fact]
    public async Task Start_Should_Reject_A_Missing_Plan_And_An_Ambiguous_Scope()
    {
        await WithUnitOfWorkAsync(async () =>
        {
            using var _ = ChangeUser(_qaLead, "qa.lead");

            (await Should.ThrowAsync<BusinessException>(
                    () => _manager.StartAsync(new QualityGateScope(Guid.NewGuid(), null), null, null, null, null)))
                .Code.ShouldBe(TestCaseManagementErrorCodes.TestPlanNotFound);
            (await Should.ThrowAsync<BusinessException>(
                    () => _manager.StartAsync(new QualityGateScope(Guid.NewGuid(), Guid.NewGuid()), null, null, null, null)))
                .Code.ShouldBe(TestCaseManagementErrorCodes.InvalidSignOffScope);
        });
    }
}
