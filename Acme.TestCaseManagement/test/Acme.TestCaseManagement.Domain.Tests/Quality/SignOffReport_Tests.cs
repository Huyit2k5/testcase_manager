using System.Reflection;
using Acme.TestCaseManagement.Enums;
using Shouldly;
using Volo.Abp;
using Xunit;

namespace Acme.TestCaseManagement.Quality;

/// <summary>The sign-off aggregate in isolation: status rules, frozen snapshot and integrity digests.</summary>
public class SignOffReport_Tests
{
    private static readonly DateTime T0 = new(2026, 10, 6, 10, 0, 0, DateTimeKind.Utc);
    private static readonly Guid PlanId = Guid.NewGuid();

    private static QualityGateEvaluation SampleEvaluation(int requiredApprovals = 2)
    {
        var metrics = QualityMetricsCalculator.Calculate(
            1,
            new[]
            {
                new QualityItemInfo(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), PriorityLevel.Urgent, TestResultStatus.Passed, TestResultStatus.Failed),
                new QualityItemInfo(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), PriorityLevel.Low, TestResultStatus.Passed, TestResultStatus.Passed),
            },
            new[] { new QualityDefectInfo(Guid.NewGuid(), "Jira", "BUG-7", "https://acme.test/BUG-7", SeverityLevel.Medium) });
        var gate = new QualityGateSettings(Guid.NewGuid(), "Release", 95m, requiredApprovals, false);

        return new QualityGateEvaluation(
            true,
            gate,
            new QualityGateScopeInfo(PlanId, null, new[] { new ScopePlan(PlanId, "Sprint 24") }),
            metrics,
            QualityGateEvaluator.Evaluate(gate, metrics),
            T0);
    }

    private static SignOffReport NewReport(int requiredApprovals = 2, QualityGateEvaluation? evaluation = null)
    {
        evaluation ??= SampleEvaluation(requiredApprovals);
        var json = SignOffSnapshot.Serialize(new SignOffSnapshot(SignOffSnapshot.CurrentSchemaVersion, T0, evaluation));

        return new SignOffReport(
            Guid.NewGuid(), null, new QualityGateScope(PlanId, null), "Sign-off: Sprint 24", evaluation.Gate, json);
    }

    private static SignOffApproval Approve(SignOffReport report, Guid? userId = null, int minutes = 0, string? role = null, string? comment = null) =>
        report.AddApproval(Guid.NewGuid(), userId ?? Guid.NewGuid(), "user", role, comment, T0.AddMinutes(minutes));

    // ---- snapshot ---------------------------------------------------------------------------------------

    [Fact]
    public void The_Snapshot_Round_Trips_Through_Json_With_Readable_Enum_Names()
    {
        var evaluation = SampleEvaluation();
        var json = SignOffSnapshot.Serialize(new SignOffSnapshot(1, T0, evaluation));

        json.ShouldContain("\"severity\":\"Medium\"");
        json.ShouldContain("\"code\":\"PassRate\"");

        var restored = SignOffSnapshot.Deserialize(json);
        restored.SchemaVersion.ShouldBe(1);
        restored.GeneratedTime.ShouldBe(T0);
        restored.Evaluation.Gate.ShouldBe(evaluation.Gate);
        restored.Evaluation.Scope.Plans.Single().Name.ShouldBe("Sprint 24");
        restored.Evaluation.Metrics.PassRate.ShouldBe(evaluation.Metrics.PassRate);
        restored.Evaluation.Metrics.OpenDefectIssues.Single().IssueKey.ShouldBe("BUG-7");
        restored.Evaluation.Criteria.Select(c => (c.Code, c.Passed)).ShouldBe(evaluation.Criteria.Select(c => (c.Code, c.Passed)));

        // Serializing the restored snapshot gives the same text, so the hash is stable.
        SignOffSnapshot.Serialize(restored).ShouldBe(json);
    }

    [Fact]
    public void The_Hash_Is_A_Lowercase_Sha256_That_Changes_With_Any_Edit()
    {
        var hash = SignOffSnapshot.ComputeHash("{\"a\":1}");

        hash.Length.ShouldBe(SignOffConsts.HashLength);
        hash.ShouldBe(hash.ToLowerInvariant());
        SignOffSnapshot.ComputeHash("{\"a\":1}").ShouldBe(hash);
        SignOffSnapshot.ComputeHash("{\"a\":2}").ShouldNotBe(hash);
    }

    // ---- status -----------------------------------------------------------------------------------------

    [Fact]
    public void A_New_Report_Is_Pending_And_Carries_The_Frozen_Gate_Values_And_Hash()
    {
        var report = NewReport();

        report.Status.ShouldBe(SignOffStatus.Pending);
        report.TestPlanId.ShouldBe(PlanId);
        report.MilestoneId.ShouldBeNull();
        (report.QualityGateName, report.MinPassRate, report.RequiredApprovals).ShouldBe(("Release", 95m, 2));
        report.SnapshotHash.ShouldBe(SignOffSnapshot.ComputeHash(report.SummaryStatsJson));
        report.Approvals.ShouldBeEmpty();
        report.ApprovedTime.ShouldBeNull();
    }

    [Fact]
    public void The_Report_Becomes_Approved_When_The_Required_Number_Of_Distinct_Users_Approved()
    {
        var report = NewReport(requiredApprovals: 2);

        Approve(report, minutes: 0, role: "QA Lead");
        report.Status.ShouldBe(SignOffStatus.Pending);
        report.ApprovedTime.ShouldBeNull();

        var second = Approve(report, minutes: 90, role: "Product Owner");
        report.Status.ShouldBe(SignOffStatus.Approved);
        report.ApprovedTime.ShouldBe(second.ApprovedTime);
        report.ApprovedTime.ShouldBe(T0.AddMinutes(90));
        report.Approvals.Select(a => a.ApproverRole).ShouldBe(new[] { "QA Lead", "Product Owner" });
    }

    [Fact]
    public void One_Approval_Is_Enough_When_The_Gate_Requires_One()
    {
        var report = NewReport(requiredApprovals: 1);

        Approve(report);

        report.Status.ShouldBe(SignOffStatus.Approved);
    }

    [Fact]
    public void A_User_Can_Approve_Only_Once()
    {
        var report = NewReport(requiredApprovals: 3);
        var user = Guid.NewGuid();
        Approve(report, user);

        Should.Throw<BusinessException>(() => Approve(report, user))
            .Code.ShouldBe(TestCaseManagementErrorCodes.DuplicateSignOffApproval);
        report.Approvals.Count.ShouldBe(1);
    }

    [Fact]
    public void An_Approved_Or_Superseded_Report_Accepts_No_More_Approvals()
    {
        var approved = NewReport(requiredApprovals: 1);
        Approve(approved);
        Should.Throw<BusinessException>(() => Approve(approved)).Code.ShouldBe(TestCaseManagementErrorCodes.SignOffNotPending);

        var superseded = NewReport();
        superseded.Supersede();
        superseded.Status.ShouldBe(SignOffStatus.Superseded);
        Should.Throw<BusinessException>(() => Approve(superseded)).Code.ShouldBe(TestCaseManagementErrorCodes.SignOffNotPending);
    }

    [Fact]
    public void Only_A_Pending_Report_Can_Be_Superseded()
    {
        var approved = NewReport(requiredApprovals: 1);
        Approve(approved);

        Should.Throw<BusinessException>(() => approved.Supersede())
            .Code.ShouldBe(TestCaseManagementErrorCodes.SignOffNotPending);
        approved.Status.ShouldBe(SignOffStatus.Approved);
    }

    [Fact]
    public void Settings_And_Scope_Are_Rebuilt_From_The_Frozen_Values()
    {
        var report = NewReport();

        var settings = report.ToSettings();
        (settings.Id, settings.Name, settings.MinPassRate, settings.RequiredApprovals, settings.IsBuiltIn)
            .ShouldBe((report.QualityGateId, "Release", 95m, 2, false));
        report.ToScope().ShouldBe(new QualityGateScope(PlanId, null));

        var baselineEvaluation = SampleEvaluation() with { Gate = QualityGateSettings.Baseline };
        var baselineReport = NewReport(evaluation: baselineEvaluation);
        baselineReport.QualityGateId.ShouldBeNull();
        baselineReport.ToSettings().IsBuiltIn.ShouldBeTrue();
    }

    // ---- approvals --------------------------------------------------------------------------------------

    [Fact]
    public void An_Approval_Normalizes_Its_Text_And_Truncates_The_Time_To_Milliseconds()
    {
        var report = NewReport();
        var sub = T0.AddTicks(1234); // 0.1234 ms

        var approval = report.AddApproval(Guid.NewGuid(), Guid.NewGuid(), "alice", "  QA Lead  ", "   ", sub);

        approval.ApproverRole.ShouldBe("QA Lead");
        approval.Comment.ShouldBeNull();
        approval.ApprovedTime.ShouldBe(T0);
        approval.ApprovedTime.Ticks.ShouldBe(T0.Ticks);
        approval.Signature.Length.ShouldBe(SignOffConsts.HashLength);
        approval.SignOffReportId.ShouldBe(report.Id);
    }

    [Fact]
    public void An_Approval_Rejects_Missing_Names_And_Oversized_Text()
    {
        var report = NewReport();

        Should.Throw<ArgumentException>(() => report.AddApproval(Guid.NewGuid(), Guid.NewGuid(), " ", null, null, T0));
        Should.Throw<ArgumentException>(() =>
            report.AddApproval(Guid.NewGuid(), Guid.NewGuid(), "u", new string('r', SignOffConsts.MaxRoleLength + 1), null, T0));
        Should.Throw<ArgumentException>(() =>
            report.AddApproval(Guid.NewGuid(), Guid.NewGuid(), "u", null, new string('c', SignOffConsts.MaxCommentLength + 1), T0));
        report.Approvals.ShouldBeEmpty();
    }

    // ---- integrity --------------------------------------------------------------------------------------

    [Fact]
    public void A_Report_With_Its_Approvals_Verifies()
    {
        var report = NewReport();
        Approve(report, role: "QA Lead", comment: "Looks good");
        Approve(report, minutes: 5, role: "Product Owner");

        report.VerifyIntegrity().ShouldBeTrue();
    }

    [Fact]
    public void Altering_The_Stored_Snapshot_Is_Detected()
    {
        var report = NewReport();
        Approve(report);

        Set(report, nameof(SignOffReport.SummaryStatsJson), report.SummaryStatsJson.Replace("\"passed\":true", "\"passed\":false"));

        report.VerifyIntegrity().ShouldBeFalse();
    }

    [Fact]
    public void Altering_An_Approval_Is_Detected()
    {
        var report = NewReport();
        var approval = Approve(report, role: "QA Lead", comment: "Looks good");
        report.VerifyIntegrity().ShouldBeTrue();

        Set(approval, nameof(SignOffApproval.Comment), "Edited afterwards");
        report.VerifyIntegrity().ShouldBeFalse();
    }

    [Fact]
    public void Re_Pointing_An_Approval_At_Another_Snapshot_Is_Detected()
    {
        var report = NewReport();
        var other = NewReport();
        var approval = Approve(report);
        Set(approval, nameof(SignOffApproval.Signature), Approve(other).Signature);

        report.VerifyIntegrity().ShouldBeFalse();
    }

    private static void Set(object target, string property, object? value)
    {
        target.GetType().GetProperty(property, BindingFlags.Public | BindingFlags.Instance)!
            .GetSetMethod(nonPublic: true)!
            .Invoke(target, new[] { value });
    }
}
