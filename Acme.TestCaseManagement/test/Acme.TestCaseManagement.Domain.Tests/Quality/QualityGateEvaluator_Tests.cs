using Acme.TestCaseManagement.Enums;
using Shouldly;
using Xunit;

namespace Acme.TestCaseManagement.Quality;

/// <summary>Executable form of the gate criteria table in plan.md, section 4.5.</summary>
public class QualityGateEvaluator_Tests
{
    private static readonly QualityGateSettings Gate95 = new(null, "Release", 95m, 2, false);

    private static QualityMetrics Metrics(
        int passed = 20,
        int failed = 0,
        int blocked = 0,
        int skipped = 0,
        int untested = 0,
        int p1Total = 0,
        int p1Executed = 0,
        int critical = 0,
        int high = 0,
        int medium = 0,
        int low = 0)
    {
        var total = passed + failed + blocked + skipped + untested;
        var applicable = total - skipped;

        return new QualityMetrics(
            RunCount: 1,
            TotalItems: total,
            Passed: passed,
            Failed: failed,
            Blocked: blocked,
            Skipped: skipped,
            Untested: untested,
            CompletionPercentage: 0,
            PassRate: applicable == 0 ? null : QualityMetricsCalculator.RoundDown(passed * 100m / applicable),
            FirstTimePassRate: null,
            P1Total: p1Total,
            P1Executed: p1Executed,
            P1ExecutionRate: p1Total == 0 ? 100m : QualityMetricsCalculator.RoundDown(p1Executed * 100m / p1Total),
            OpenDefects: new OpenDefectCounts(critical, high, medium, low, critical + high + medium + low),
            OpenDefectIssues: Array.Empty<OpenDefectSummary>());
    }

    private static GateCriterionResult Criterion(IReadOnlyList<GateCriterionResult> results, string code) =>
        results.Single(r => r.Code == code);

    [Fact]
    public void Four_Criteria_Are_Evaluated_In_A_Stable_Order()
    {
        var results = QualityGateEvaluator.Evaluate(Gate95, Metrics());

        results.Select(r => r.Code).ShouldBe(new[]
        {
            QualityGateCriteria.PassRate,
            QualityGateCriteria.P1Executed,
            QualityGateCriteria.OpenCriticalDefects,
            QualityGateCriteria.OpenHighDefects,
        });
        results.ShouldAllBe(r => r.Passed);
    }

    [Theory]
    [InlineData(19, 1, true)]    // 95.00% passes: the threshold is inclusive
    [InlineData(18, 2, false)]   // 90%: the spec example that must be blocked
    [InlineData(95, 5, true)]
    [InlineData(94, 6, false)]
    public void Pass_Rate_Must_Reach_The_Threshold(int passed, int failed, bool expected)
    {
        var results = QualityGateEvaluator.Evaluate(Gate95, Metrics(passed: passed, failed: failed));

        var criterion = Criterion(results, QualityGateCriteria.PassRate);
        criterion.Passed.ShouldBe(expected);
        criterion.Threshold.ShouldBe(95m);
        criterion.Operator.ShouldBe(">=");
    }

    [Fact]
    public void The_Gate_Compares_The_Exact_Ratio_Not_The_Rounded_Down_Display_Value()
    {
        // 94996 / 100000 = 94.996%: shown as 94.99 and it must fail a 95% gate.
        var results = QualityGateEvaluator.Evaluate(Gate95, Metrics(passed: 94996, failed: 5004));
        var criterion = Criterion(results, QualityGateCriteria.PassRate);

        criterion.Actual.ShouldBe(94.99m);
        criterion.Passed.ShouldBeFalse();

        // 95000 / 100000 = exactly 95%.
        Criterion(QualityGateEvaluator.Evaluate(Gate95, Metrics(passed: 95000, failed: 5000)), QualityGateCriteria.PassRate)
            .Passed.ShouldBeTrue();
    }

    [Fact]
    public void A_Scope_Without_Applicable_Items_Fails_The_Pass_Rate_Criterion()
    {
        var empty = Criterion(QualityGateEvaluator.Evaluate(Gate95, Metrics(passed: 0)), QualityGateCriteria.PassRate);
        empty.Passed.ShouldBeFalse();
        empty.Actual.ShouldBeNull();

        Criterion(QualityGateEvaluator.Evaluate(Gate95, Metrics(passed: 0, skipped: 5)), QualityGateCriteria.PassRate)
            .Passed.ShouldBeFalse();
    }

    [Fact]
    public void Untested_And_Blocked_Items_Lower_The_Pass_Rate_Below_The_Gate()
    {
        var results = QualityGateEvaluator.Evaluate(Gate95, Metrics(passed: 90, untested: 5, blocked: 5));

        Criterion(results, QualityGateCriteria.PassRate).Passed.ShouldBeFalse();
    }

    [Fact]
    public void The_Configured_Threshold_Is_Used()
    {
        var lenient = new QualityGateSettings(null, "Hotfix", 80m, 1, false);

        Criterion(QualityGateEvaluator.Evaluate(lenient, Metrics(passed: 16, failed: 4)), QualityGateCriteria.PassRate)
            .Passed.ShouldBeTrue();
        Criterion(QualityGateEvaluator.Evaluate(lenient, Metrics(passed: 15, failed: 5)), QualityGateCriteria.PassRate)
            .Passed.ShouldBeFalse();
    }

    [Theory]
    [InlineData(10, 10, true)]
    [InlineData(10, 9, false)]
    [InlineData(0, 0, true)]   // no P1 tests in scope: nothing to execute
    public void Every_P1_Item_Must_Be_Executed(int p1Total, int p1Executed, bool expected)
    {
        var criterion = Criterion(
            QualityGateEvaluator.Evaluate(Gate95, Metrics(p1Total: p1Total, p1Executed: p1Executed)),
            QualityGateCriteria.P1Executed);

        criterion.Passed.ShouldBe(expected);
        criterion.Threshold.ShouldBe(100m);
    }

    [Fact]
    public void One_Open_Critical_Defect_Fails_The_Gate_Even_When_Everything_Else_Is_Perfect()
    {
        var results = QualityGateEvaluator.Evaluate(Gate95, Metrics(critical: 1));

        var criterion = Criterion(results, QualityGateCriteria.OpenCriticalDefects);
        criterion.Passed.ShouldBeFalse();
        criterion.Actual.ShouldBe(1m);
        criterion.Threshold.ShouldBe(0m);
        criterion.Operator.ShouldBe("<=");
        results.Where(r => !r.Passed).Select(r => r.Code).ShouldBe(new[] { QualityGateCriteria.OpenCriticalDefects });
    }

    [Fact]
    public void One_Open_High_Defect_Fails_The_Gate_Per_Constitution_V()
    {
        Criterion(QualityGateEvaluator.Evaluate(Gate95, Metrics(high: 1)), QualityGateCriteria.OpenHighDefects)
            .Passed.ShouldBeFalse();
    }

    [Fact]
    public void Open_Medium_And_Low_Defects_Do_Not_Fail_The_Gate()
    {
        QualityGateEvaluator.Evaluate(Gate95, Metrics(medium: 7, low: 12)).ShouldAllBe(r => r.Passed);
    }

    [Fact]
    public void Describe_Failures_Lists_Only_The_Failed_Criteria_With_Culture_Neutral_Numbers()
    {
        var metrics = Metrics(passed: 92, failed: 8, critical: 2);
        var evaluation = new QualityGateEvaluation(
            false,
            Gate95,
            new QualityGateScopeInfo(Guid.NewGuid(), null, Array.Empty<ScopePlan>()),
            metrics,
            QualityGateEvaluator.Evaluate(Gate95, metrics),
            DateTime.UtcNow);

        evaluation.DescribeFailures().ShouldBe(
            "PassRate 92 (required >= 95); OpenCriticalDefects 2 (required <= 0)");
    }
}
