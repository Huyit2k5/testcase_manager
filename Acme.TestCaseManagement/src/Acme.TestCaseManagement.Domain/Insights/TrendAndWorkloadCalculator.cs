using Acme.TestCaseManagement.Enums;
using Acme.TestCaseManagement.Quality;

namespace Acme.TestCaseManagement.Insights;

/// <summary>The run items and the defects of the runs of one test plan, for the trend over many plans.</summary>
public class PlanInsightData
{
    public Guid PlanId { get; set; }

    public int RunCount { get; set; }

    public List<InsightItem> Items { get; set; } = new();

    public List<InsightDefect> Defects { get; set; } = new();
}

/// <summary>One run item of the scope and the person it is given to (null: nobody).</summary>
public record WorkloadItem(Guid RunItemId, Guid? AssignedUserId, TestResultStatus Status);

/// <summary>One attempt of the scope: who made it (null when the creator is not known, as for results sent by a pipeline), the result, how long it took and when.</summary>
public record WorkloadAttempt(Guid RunItemId, Guid? UserId, TestResultStatus Status, int DurationSeconds, DateTime Time);

public class WorkloadScopeData
{
    public List<WorkloadItem> Items { get; set; } = new();

    /// <summary>Attempts made since the start of the window.</summary>
    public List<WorkloadAttempt> Attempts { get; set; } = new();
}

/// <summary>
/// What a tester has: <c>Assigned*</c> is the state of the run items given to them now, whenever they were run; the other figures are
/// the attempts they made inside the window.
/// </summary>
public record TesterWorkload(
    Guid? UserId,
    int AssignedItems, int AssignedDone, int AssignedPassed, int AssignedFailed,
    int Attempts, int Passed, int Failed, int Blocked, int Skipped,
    int ItemsExecuted, int DurationSeconds, int ActiveDays, decimal AttemptsPerDay, decimal? PassRate);

/// <param name="UnassignedItems">Run items given to nobody, and <paramref name="UnassignedUntested"/> how many of them have no attempt yet.</param>
public record WorkloadReport(IReadOnlyList<TesterWorkload> Testers, int UnassignedItems, int UnassignedUntested, int TotalAttempts, int TotalItems);

/// <summary>
/// The workload of the testers over a window (ADR 4.32). An attempt belongs to whoever made it (the creator the framework records),
/// an item to whoever it is assigned to; the two can be different people. The pass rate of a tester is the passed attempts over the
/// attempts that were not skipped.
/// </summary>
public static class WorkloadCalculator
{
    public static WorkloadReport Calculate(WorkloadScopeData data, int days)
    {
        var assigned = data.Items.Where(i => i.AssignedUserId.HasValue).ToLookup(i => i.AssignedUserId!.Value);
        var attempts = data.Attempts.ToLookup(a => a.UserId);

        var people = assigned.Select(g => (Guid?)g.Key).Concat(attempts.Select(g => g.Key)).Distinct().ToList();
        var testers = people.Select(user => One(user, user.HasValue ? assigned[user.Value].ToList() : new List<WorkloadItem>(), attempts[user].ToList(), days))
            .OrderByDescending(t => t.Attempts)
            .ThenByDescending(t => t.AssignedItems)
            .ThenBy(t => t.UserId.HasValue ? 0 : 1)
            .ThenBy(t => t.UserId)
            .ToList();

        var unassigned = data.Items.Where(i => !i.AssignedUserId.HasValue).ToList();
        return new WorkloadReport(
            testers,
            unassigned.Count,
            unassigned.Count(i => i.Status == TestResultStatus.Untested),
            data.Attempts.Count,
            data.Items.Count);
    }

    private static TesterWorkload One(Guid? user, List<WorkloadItem> items, List<WorkloadAttempt> attempts, int days)
    {
        int Count(TestResultStatus status) => attempts.Count(a => a.Status == status);

        var passed = Count(TestResultStatus.Passed);
        var skipped = Count(TestResultStatus.Skipped);
        var applicable = attempts.Count - skipped;

        return new TesterWorkload(
            user,
            items.Count,
            items.Count(i => i.Status != TestResultStatus.Untested),
            items.Count(i => i.Status == TestResultStatus.Passed),
            items.Count(i => i.Status == TestResultStatus.Failed),
            attempts.Count,
            passed,
            Count(TestResultStatus.Failed),
            Count(TestResultStatus.Blocked),
            skipped,
            attempts.Select(a => a.RunItemId).Distinct().Count(),
            attempts.Sum(a => a.DurationSeconds),
            attempts.Select(a => a.Time.Date).Distinct().Count(),
            days <= 0 ? 0 : Math.Round(attempts.Count / (decimal)days, 2),
            applicable <= 0 ? null : QualityMetricsCalculator.RoundDown(passed * 100m / applicable));
    }
}

/// <summary>One test plan ("sprint") in the trend: its figures, worked out the same way as the dashboard works out a plan.</summary>
public record SprintFigures(
    Guid PlanId, int RunCount, ProgressSummary Progress, DefectDensityReport Defects);

public static class SprintTrendCalculator
{
    /// <summary>The figures of each plan, in the order given.</summary>
    public static List<SprintFigures> Calculate(IEnumerable<PlanInsightData> plans) =>
        plans.Select(p => new SprintFigures(p.PlanId, p.RunCount, DashboardCalculator.Progress(p.Items), DashboardCalculator.DefectDensity(p.Items, p.Defects))).ToList();

    /// <summary>
    /// The change of a rate from the plan before to the last one, in points (+3.5 is 3.5 points better), when both have a value;
    /// otherwise null. <paramref name="rates"/> are in time order, oldest first.
    /// </summary>
    public static decimal? Change(IReadOnlyList<decimal?> rates)
    {
        if (rates.Count < 2 || rates[^1] is not { } last || rates[^2] is not { } before)
        {
            return null;
        }

        return Math.Round(last - before, 2);
    }
}
