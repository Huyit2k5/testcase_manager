using Acme.TestCaseManagement.Enums;
using Acme.TestCaseManagement.Quality;

namespace Acme.TestCaseManagement.Insights;

/// <summary>One recorded attempt of a test case, whatever run it was made in.</summary>
public record InsightAttempt(Guid RunItemId, Guid TestCaseId, TestResultStatus Status, int AttemptNumber, DateTime Time);

public class FlakinessSettings
{
    /// <summary>How many of the latest Passed or Failed outcomes are looked at.</summary>
    public int WindowSize { get; set; } = 20;

    /// <summary>Fewer outcomes than this give <see cref="FlakinessLevel.Insufficient"/>.</summary>
    public int MinimumObservations { get; set; } = 5;

    /// <summary>A score at or above this is <see cref="FlakinessLevel.Watch"/>.</summary>
    public decimal WatchScore { get; set; } = 0.15m;

    /// <summary>A score at or above this is <see cref="FlakinessLevel.Flaky"/>.</summary>
    public decimal FlakyScore { get; set; } = 0.30m;
}

/// <param name="Observations">Passed and Failed outcomes in the window.</param>
/// <param name="Flips">Times two neighbouring outcomes differ.</param>
/// <param name="Score">Flips divided by (Observations - 1), from 0 to 1, rounded down to two decimals.</param>
public record FlakinessResult(
    Guid TestCaseId,
    int Observations,
    int Passes,
    int Failures,
    int Flips,
    decimal Score,
    FlakinessLevel Level,
    DateTime? LastResultAt,
    DateTime? LastFailedAt);

/// <summary>
/// The transition score of a test: take its latest Passed or Failed outcomes in time order, and count how often the outcome
/// changes from one to the next. Buildkite Test Engine's transition score counts the same changes (PPPPP and FFFFF have none,
/// PPFFF one, PFPFF three) and flip rate is the usual name of it in other tools; here the count is divided by the number of
/// neighbouring pairs, so 1 means that every pair changed. Why this and not the pass rate: a test that always fails
/// has a pass rate of 0 and is broken, not flaky, and a test that fixed itself once has a middling pass rate and no
/// instability; only changes back and forth show intermittence. A retry that passes after a failure is one attempt after
/// another, so it counts as a flip without a special rule. Blocked and Skipped say nothing about the test and are left out.
/// </summary>
public static class FlakinessCalculator
{
    public static FlakinessResult Calculate(Guid testCaseId, IEnumerable<InsightAttempt> attempts, FlakinessSettings settings)
    {
        var window = attempts
            .Where(a => a.Status is TestResultStatus.Passed or TestResultStatus.Failed)
            .OrderBy(a => a.Time)
            .ThenBy(a => a.AttemptNumber)
            .TakeLast(Math.Max(2, settings.WindowSize))
            .ToList();

        var flips = 0;
        for (var i = 1; i < window.Count; i++)
        {
            if (window[i].Status != window[i - 1].Status)
            {
                flips++;
            }
        }

        var score = window.Count < 2 ? 0m : QualityMetricsCalculator.RoundDown(flips / (decimal)(window.Count - 1));
        var level = window.Count < Math.Max(2, settings.MinimumObservations)
            ? FlakinessLevel.Insufficient
            : score >= settings.FlakyScore ? FlakinessLevel.Flaky
            : score >= settings.WatchScore ? FlakinessLevel.Watch
            : FlakinessLevel.Stable;

        return new FlakinessResult(
            testCaseId,
            window.Count,
            window.Count(a => a.Status == TestResultStatus.Passed),
            window.Count(a => a.Status == TestResultStatus.Failed),
            flips,
            score,
            level,
            window.Count == 0 ? null : window[^1].Time,
            window.LastOrDefault(a => a.Status == TestResultStatus.Failed)?.Time);
    }

    /// <summary>
    /// The result of every test case that has attempts, counting only the attempts made at or after <paramref name="since"/> when it is
    /// given. The dashboard reads attempts far enough back for its velocity chart; the flakiness it shows must use the same window as the
    /// list of flaky tests (the lookback of the options), whatever the chart covers.
    /// </summary>
    public static List<FlakinessResult> CalculateAll(IEnumerable<InsightAttempt> attempts, FlakinessSettings settings, DateTime? since = null)
    {
        return attempts
            .Where(a => since == null || a.Time >= since)
            .GroupBy(a => a.TestCaseId)
            .Select(g => Calculate(g.Key, g, settings))
            .ToList();
    }
}
