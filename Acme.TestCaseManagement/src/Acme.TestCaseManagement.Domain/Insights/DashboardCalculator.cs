using Acme.TestCaseManagement.Enums;
using Acme.TestCaseManagement.Quality;

namespace Acme.TestCaseManagement.Insights;

/// <summary>One run item of the scope. <c>FirstExecutedAt</c> is the time of its first attempt, null while it is untested.</summary>
public record InsightItem(Guid RunItemId, Guid TestCaseId, TestResultStatus Status, TestResultStatus? FirstAttemptStatus, DateTime? FirstExecutedAt);

/// <summary>A defect link on an execution of the scope.</summary>
public record InsightDefect(Guid TestCaseId, string ExternalSystem, string IssueKey, SeverityLevel Severity, bool IsResolved);

public class InsightsScopeData
{
    public int RunCount { get; set; }

    public List<InsightItem> Items { get; set; } = new();

    /// <summary>Attempts made since the start of the lookback window, newest included.</summary>
    public List<InsightAttempt> Attempts { get; set; } = new();

    public List<InsightDefect> Defects { get; set; } = new();
}

public record ProgressSummary(
    int TotalItems, int Passed, int Failed, int Blocked, int Skipped, int Untested,
    decimal CompletionPercentage, decimal? PassRate, decimal? FirstTimePassRate);

public record VelocityPoint(DateTime Date, int Attempts, int ItemsCompleted, int Passed, int Failed);

/// <param name="TrendPercent">Change of the average of the last 7 days against the 7 days before, when the window has both.</param>
public record VelocityReport(
    IReadOnlyList<VelocityPoint> Points, int TotalAttempts, decimal AveragePerDay, decimal Last7DaysAverage, decimal? TrendPercent);

/// <param name="Remaining">Items without any attempt at the end of the day; null for a day that has not come.</param>
/// <param name="Ideal">The straight line from the items left at the start to zero at the end date.</param>
public record BurnDownPoint(DateTime Date, int? Remaining, decimal Ideal);

/// <param name="ItemsPerDay">Items first executed per day over the last 7 days (or since the start, if shorter).</param>
/// <param name="ProjectedFinish">Today plus the days that the remaining items need at that pace; null when nothing is left or the pace is 0.</param>
/// <param name="OnTrack">Remaining today is not above the ideal line; null when there is nothing to burn.</param>
public record BurnDownReport(
    DateTime Start, DateTime End, int TotalItems, int RemainingAtStart, int RemainingNow,
    IReadOnlyList<BurnDownPoint> Points, decimal ItemsPerDay, DateTime? ProjectedFinish, bool? OnTrack);

public record DefectDensityReport(
    int Defects, int OpenDefects, int ResolvedDefects, int ExecutedTests, decimal? DefectsPer100Executed,
    int TestsWithDefects, decimal? TestsWithDefectsPercent, OpenDefectCounts OpenBySeverity);

/// <summary>
/// The figures of the dashboard (FR-025). All days are UTC dates. Definitions are in the plan (ADR 4.10): the pass rate is the
/// one of the quality gate, velocity counts attempts and items first executed per day, the burn-down is the number of items
/// not yet executed against an ideal straight line, and defect density is distinct defects per 100 executed test cases.
/// </summary>
public static class DashboardCalculator
{
    private const int BurnDownMaxDays = 120;
    private const int PaceDays = 7;

    public static ProgressSummary Progress(IReadOnlyCollection<InsightItem> items)
    {
        int Count(TestResultStatus status) => items.Count(i => i.Status == status);

        var total = items.Count;
        var passed = Count(TestResultStatus.Passed);
        var skipped = Count(TestResultStatus.Skipped);
        var untested = Count(TestResultStatus.Untested);
        var applicable = total - skipped;
        var firstAttempts = items.Where(i => i.FirstAttemptStatus.HasValue).ToList();

        return new ProgressSummary(
            total, passed, Count(TestResultStatus.Failed), Count(TestResultStatus.Blocked), skipped, untested,
            total == 0 ? 0 : QualityMetricsCalculator.RoundDown((total - untested) * 100m / total),
            applicable == 0 ? null : QualityMetricsCalculator.RoundDown(passed * 100m / applicable),
            firstAttempts.Count == 0
                ? null
                : QualityMetricsCalculator.RoundDown(firstAttempts.Count(i => i.FirstAttemptStatus == TestResultStatus.Passed) * 100m / firstAttempts.Count));
    }

    /// <summary>Attempts and newly executed items for each of the last <paramref name="days"/> days, ending today.</summary>
    public static VelocityReport Velocity(
        IReadOnlyCollection<InsightAttempt> attempts, IReadOnlyCollection<InsightItem> items, DateTime today, int days)
    {
        today = today.Date;
        var start = today.AddDays(-(days - 1));

        var attemptsByDay = attempts.Where(a => a.Time.Date >= start && a.Time.Date <= today).GroupBy(a => a.Time.Date).ToDictionary(g => g.Key);
        var completedByDay = items.Where(i => i.FirstExecutedAt.HasValue).GroupBy(i => i.FirstExecutedAt!.Value.Date).ToDictionary(g => g.Key, g => g.Count());

        var points = new List<VelocityPoint>(days);
        for (var day = start; day <= today; day = day.AddDays(1))
        {
            attemptsByDay.TryGetValue(day, out var dayAttempts);
            points.Add(new VelocityPoint(
                day,
                dayAttempts?.Count() ?? 0,
                completedByDay.GetValueOrDefault(day),
                dayAttempts?.Count(a => a.Status == TestResultStatus.Passed) ?? 0,
                dayAttempts?.Count(a => a.Status == TestResultStatus.Failed) ?? 0));
        }

        var total = points.Sum(p => p.Attempts);
        var recent = points.TakeLast(Math.Min(PaceDays, points.Count)).ToList();
        var last7 = Average(recent.Sum(p => p.Attempts), recent.Count);

        decimal? trend = null;
        if (points.Count >= PaceDays * 2)
        {
            var before = Average(points.Skip(points.Count - PaceDays * 2).Take(PaceDays).Sum(p => p.Attempts), PaceDays);
            if (before > 0)
            {
                trend = Math.Round((last7 - before) * 100m / before, 1);
            }
        }

        return new VelocityReport(points, total, Average(total, days), last7, trend);
    }

    /// <summary>
    /// Items not yet executed, day by day, from <paramref name="start"/> to the later of <paramref name="end"/> and today (at most
    /// 120 days, the latest ones). The start is the start of the plan or, without one, the first day of the window.
    /// </summary>
    public static BurnDownReport BurnDown(IReadOnlyCollection<InsightItem> items, DateTime start, DateTime? end, DateTime today)
    {
        today = today.Date;
        start = start.Date;
        var finish = end.HasValue && end.Value.Date >= start ? end.Value.Date : today;
        var last = finish > today ? finish : today;
        if ((last - start).Days >= BurnDownMaxDays)
        {
            start = last.AddDays(-(BurnDownMaxDays - 1));
        }

        var total = items.Count;
        var executedDays = items.Where(i => i.FirstExecutedAt.HasValue).Select(i => i.FirstExecutedAt!.Value.Date).ToList();
        int RemainingAt(DateTime day) => total - executedDays.Count(d => d <= day);

        var atStart = RemainingAt(start.AddDays(-1));
        var span = (finish - start).Days;

        var points = new List<BurnDownPoint>();
        for (var day = start; day <= last; day = day.AddDays(1))
        {
            // Ideal: the items left before the first day, burned evenly so that nothing is left at the end of the end day.
            var elapsed = (day - start).Days + 1;
            var ideal = day >= finish ? 0m : Math.Round(atStart * (1m - elapsed / (decimal)(span + 1)), 2);
            points.Add(new BurnDownPoint(day, day <= today ? RemainingAt(day) : null, ideal));
        }

        var now = RemainingAt(today);

        var paceStart = today.AddDays(-(PaceDays - 1)) < start ? start : today.AddDays(-(PaceDays - 1));
        var paceDays = (today - paceStart).Days + 1;
        var pace = Average(executedDays.Count(d => d >= paceStart && d <= today), paceDays);
        DateTime? projected = now == 0 || pace == 0 ? null : today.AddDays((int)Math.Ceiling(now / pace));

        var todayPoint = points.FirstOrDefault(p => p.Date == today);
        bool? onTrack = total == 0 || now == 0 && atStart == 0 ? null : todayPoint == null ? null : now <= todayPoint.Ideal;

        return new BurnDownReport(start, finish, total, atStart, now, points, pace, projected, onTrack);
    }

    public static DefectDensityReport DefectDensity(IReadOnlyCollection<InsightItem> items, IReadOnlyCollection<InsightDefect> defects)
    {
        var executedTests = items.Where(i => i.FirstExecutedAt.HasValue).Select(i => i.TestCaseId).Distinct().Count();

        // One ticket linked from many failing tests is one defect; it is open while any of its links is.
        var issues = defects
            .GroupBy(d => (System: d.ExternalSystem.Trim().ToLowerInvariant(), Key: d.IssueKey.Trim().ToLowerInvariant()))
            .Select(g => (
                Open: g.Any(d => !d.IsResolved),
                Severity: (g.Any(d => !d.IsResolved) ? g.Where(d => !d.IsResolved) : g).Max(d => d.Severity)))
            .ToList();

        var open = issues.Where(i => i.Open).ToList();
        var testsWithDefects = defects.Select(d => d.TestCaseId).Distinct().Count();

        return new DefectDensityReport(
            issues.Count,
            open.Count,
            issues.Count - open.Count,
            executedTests,
            executedTests == 0 ? null : QualityMetricsCalculator.RoundDown(issues.Count * 100m / executedTests),
            testsWithDefects,
            executedTests == 0 ? null : QualityMetricsCalculator.RoundDown(Math.Min(testsWithDefects, executedTests) * 100m / executedTests),
            new OpenDefectCounts(
                open.Count(i => i.Severity == SeverityLevel.Critical),
                open.Count(i => i.Severity == SeverityLevel.High),
                open.Count(i => i.Severity == SeverityLevel.Medium),
                open.Count(i => i.Severity == SeverityLevel.Low),
                open.Count));
    }

    private static decimal Average(int sum, int days) => days <= 0 ? 0 : Math.Round(sum / (decimal)days, 2);
}
