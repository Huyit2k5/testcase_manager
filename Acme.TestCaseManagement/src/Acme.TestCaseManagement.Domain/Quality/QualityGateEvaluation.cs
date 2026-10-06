using System.Globalization;
using Volo.Abp;

namespace Acme.TestCaseManagement.Quality;

public static class QualityGateCriteria
{
    public const string PassRate = "PassRate";
    public const string P1Executed = "P1Executed";
    public const string OpenCriticalDefects = "OpenCriticalDefects";
    public const string OpenHighDefects = "OpenHighDefects";
}

/// <summary>Outcome of one criterion. <paramref name="Actual"/> is null when it cannot be computed (no applicable items).</summary>
/// <param name="Code">One of the <see cref="QualityGateCriteria"/> constants.</param>
/// <param name="Threshold">The limit the actual value is compared with.</param>
/// <param name="Actual">The measured value; null when it cannot be computed.</param>
/// <param name="Passed">Whether the criterion is satisfied.</param>
/// <param name="Operator">"&gt;=" when the actual value must reach the threshold, "&lt;=" when it must not exceed it.</param>
public record GateCriterionResult(string Code, string Operator, decimal Threshold, decimal? Actual, bool Passed);

public record ScopePlan(Guid Id, string Name);

/// <summary>What was evaluated: exactly one of the plan or the milestone, and the plans that resulted from it.</summary>
public record QualityGateScopeInfo(Guid? TestPlanId, Guid? MilestoneId, IReadOnlyList<ScopePlan> Plans);

/// <summary>The complete result of evaluating a gate; this is what a sign-off report freezes.</summary>
public record QualityGateEvaluation(
    bool Passed,
    QualityGateSettings Gate,
    QualityGateScopeInfo Scope,
    QualityMetrics Metrics,
    IReadOnlyList<GateCriterionResult> Criteria,
    DateTime EvaluatedTime)
{
    /// <summary>Culture-neutral one-line description of the failed criteria, e.g. "PassRate 92 (required >= 95)".</summary>
    public string DescribeFailures()
    {
        return string.Join("; ", Criteria.Where(c => !c.Passed).Select(c =>
            $"{c.Code} {Format(c.Actual)} (required {c.Operator} {Format(c.Threshold)})"));
    }

    private static string Format(decimal? value)
    {
        return value.HasValue ? value.Value.ToString("0.##", CultureInfo.InvariantCulture) : "n/a";
    }
}

/// <summary>
/// Thrown when a sign-off is attempted (or approved) while the gate does not pass. Carries the full breakdown.
/// </summary>
public class QualityGateNotPassedException : BusinessException
{
    public QualityGateEvaluation Evaluation { get; }

    public QualityGateNotPassedException(QualityGateEvaluation evaluation)
        : base(TestCaseManagementErrorCodes.QualityGateNotPassed, details: evaluation.DescribeFailures())
    {
        Evaluation = evaluation;
        WithData("GateName", evaluation.Gate.Name);
        WithData("FailedCriteria", evaluation.DescribeFailures());
    }
}

/// <summary>
/// The gate rules of the plan (ADR 4.5), as a pure function of the settings and the metrics. All criteria
/// must pass. Only the pass rate threshold is configurable; the others are mandated by constitution V.
/// </summary>
public static class QualityGateEvaluator
{
    public static IReadOnlyList<GateCriterionResult> Evaluate(QualityGateSettings gate, QualityMetrics metrics)
    {
        // Compare the exact ratio (passed / applicable >= threshold / 100), never the rounded-down display value.
        var applicable = metrics.TotalItems - metrics.Skipped;
        var passRateReached = applicable > 0 && metrics.Passed * 100m >= gate.MinPassRate * applicable;

        return new[]
        {
            new GateCriterionResult(
                QualityGateCriteria.PassRate, ">=", gate.MinPassRate, metrics.PassRate, passRateReached),

            new GateCriterionResult(
                QualityGateCriteria.P1Executed, ">=", 100m, metrics.P1ExecutionRate, metrics.P1Executed == metrics.P1Total),

            new GateCriterionResult(
                QualityGateCriteria.OpenCriticalDefects, "<=", 0m, metrics.OpenDefects.Critical, metrics.OpenDefects.Critical == 0),

            new GateCriterionResult(
                QualityGateCriteria.OpenHighDefects, "<=", 0m, metrics.OpenDefects.High, metrics.OpenDefects.High == 0),
        };
    }
}
