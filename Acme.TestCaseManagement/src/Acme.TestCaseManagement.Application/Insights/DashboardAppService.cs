using Acme.TestCaseManagement.Enums;
using Acme.TestCaseManagement.Insights.Dtos;
using Acme.TestCaseManagement.Permissions;
using Acme.TestCaseManagement.Plans;
using Acme.TestCaseManagement.Repositories;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;
using Volo.Abp.Domain.Repositories;

namespace Acme.TestCaseManagement.Insights;

[Authorize(TestCaseManagementPermissions.TestRuns.Default)]
public class DashboardAppService : TestCaseManagementAppService, IDashboardAppService
{
    private readonly IInsightsRepository _insights;
    private readonly IRepository<TestPlan, Guid> _plans;
    private readonly TestCaseManagementInsightsOptions _options;

    public DashboardAppService(
        IInsightsRepository insights,
        IRepository<TestPlan, Guid> plans,
        IOptions<TestCaseManagementInsightsOptions> options)
    {
        _insights = insights;
        _plans = plans;
        _options = options.Value;
    }

    /// <summary>The most plans looked at to find the latest ones that have run items.</summary>
    private const int PlansExamined = 60;

    [Authorize(TestCaseManagementPermissions.TestPlans.Manage)]
    public virtual async Task<WorkloadDto> GetWorkloadAsync(GetWorkloadInput input)
    {
        var now = Clock.Now;
        var plan = input.TestPlanId.HasValue ? await _plans.GetAsync(input.TestPlanId.Value) : null;

        // The same window as the velocity chart: today and the days before it.
        var since = now.Date.AddDays(-(input.Days - 1));
        var data = await _insights.GetWorkloadDataAsync(input.TestPlanId, since, input.ProjectId);
        var report = WorkloadCalculator.Calculate(data, input.Days);

        return new WorkloadDto
        {
            TestPlanId = plan?.Id,
            TestPlanName = plan?.Name,
            Days = input.Days,
            GeneratedAt = now,
            UnassignedItems = report.UnassignedItems,
            UnassignedUntested = report.UnassignedUntested,
            TotalItems = report.TotalItems,
            TotalAttempts = report.TotalAttempts,
            Testers = report.Testers.Select(t => new TesterWorkloadDto
            {
                UserId = t.UserId,
                AssignedItems = t.AssignedItems,
                AssignedDone = t.AssignedDone,
                AssignedPassed = t.AssignedPassed,
                AssignedFailed = t.AssignedFailed,
                Attempts = t.Attempts,
                Passed = t.Passed,
                Failed = t.Failed,
                Blocked = t.Blocked,
                Skipped = t.Skipped,
                ItemsExecuted = t.ItemsExecuted,
                DurationSeconds = t.DurationSeconds,
                ActiveDays = t.ActiveDays,
                AttemptsPerDay = t.AttemptsPerDay,
                PassRate = t.PassRate,
            }).ToList(),
        };
    }

    public virtual async Task<SprintTrendDto> GetTrendAsync(GetSprintTrendInput input)
    {
        var plans = (await _plans.GetListAsync(p => input.ProjectId == null || p.ProjectId == input.ProjectId))
            .OrderByDescending(p => p.StartDate ?? p.CreationTime)
            .ThenByDescending(p => p.CreationTime)
            .Take(PlansExamined)
            .ToList();

        var data = await _insights.GetPlansDataAsync(plans.Select(p => p.Id).ToList());
        var figures = SprintTrendCalculator.Calculate(data).ToDictionary(f => f.PlanId);

        // The latest plans that have something to count, shown oldest first so that the line reads from left to right.
        var points = plans
            .Where(p => figures[p.Id].Progress.TotalItems > 0)
            .Take(input.Sprints)
            .Reverse()
            .Select(p =>
            {
                var f = figures[p.Id];
                return new SprintTrendPointDto
                {
                    PlanId = p.Id,
                    PlanName = p.Name,
                    Status = p.Status,
                    MilestoneId = p.MilestoneId,
                    StartDate = p.StartDate,
                    EndDate = p.EndDate,
                    RunCount = f.RunCount,
                    TotalItems = f.Progress.TotalItems,
                    Passed = f.Progress.Passed,
                    Failed = f.Progress.Failed,
                    Blocked = f.Progress.Blocked,
                    Skipped = f.Progress.Skipped,
                    Untested = f.Progress.Untested,
                    CompletionPercentage = f.Progress.CompletionPercentage,
                    PassRate = f.Progress.PassRate,
                    FirstTimePassRate = f.Progress.FirstTimePassRate,
                    Defects = f.Defects.Defects,
                    OpenDefects = f.Defects.OpenDefects,
                    DefectsPer100Executed = f.Defects.DefectsPer100Executed,
                };
            })
            .ToList();

        var rates = points.Where(p => p.PassRate.HasValue).Select(p => p.PassRate!.Value).ToList();
        return new SprintTrendDto
        {
            GeneratedAt = Clock.Now,
            Points = points,
            PassRateChange = SprintTrendCalculator.Change(points.Select(p => p.PassRate).ToList()),
            FirstTimePassRateChange = SprintTrendCalculator.Change(points.Select(p => p.FirstTimePassRate).ToList()),
            DefectDensityChange = SprintTrendCalculator.Change(points.Select(p => p.DefectsPer100Executed).ToList()),
            AveragePassRate = rates.Count == 0 ? null : Math.Round(rates.Average(), 2),
        };
    }

    public virtual async Task<DashboardDto> GetAsync(GetDashboardInput input)
    {
        var now = Clock.Now;
        var today = now.Date;

        var plan = input.TestPlanId.HasValue ? await _plans.GetAsync(input.TestPlanId.Value) : null;

        // Attempts are read far enough back for both the velocity chart and the flakiness window.
        var since = today.AddDays(-Math.Max(input.Days, _options.LookbackDays));
        var data = await _insights.GetScopeDataAsync(input.TestPlanId, since, input.ProjectId);

        var progress = DashboardCalculator.Progress(data.Items);
        var velocity = DashboardCalculator.Velocity(data.Attempts, data.Items, today, input.Days);

        // A plan burns down over its own dates; without a plan (or without a start date) the chart starts at the first day of the window.
        var windowStart = today.AddDays(-(input.Days - 1));
        var start = plan?.StartDate?.Date is { } planStart && planStart <= today ? planStart : windowStart;
        var burnDown = DashboardCalculator.BurnDown(data.Items, start, plan?.EndDate, today);

        var density = DashboardCalculator.DefectDensity(data.Items, data.Defects);
        // The same window as the list of flaky tests, not the (possibly longer) window of the velocity chart.
        var flaky = FlakinessCalculator.CalculateAll(data.Attempts, _options.ToSettings(), now.AddDays(-_options.LookbackDays));

        return new DashboardDto
        {
            TestPlanId = plan?.Id,
            TestPlanName = plan?.Name,
            Days = input.Days,
            GeneratedAt = now,
            RunCount = data.RunCount,
            Progress = new DashboardProgressDto
            {
                TotalItems = progress.TotalItems,
                Passed = progress.Passed,
                Failed = progress.Failed,
                Blocked = progress.Blocked,
                Skipped = progress.Skipped,
                Untested = progress.Untested,
                CompletionPercentage = progress.CompletionPercentage,
                PassRate = progress.PassRate,
                FirstTimePassRate = progress.FirstTimePassRate,
            },
            Velocity = new VelocityDto
            {
                Points = velocity.Points
                    .Select(p => new VelocityPointDto { Date = p.Date, Attempts = p.Attempts, ItemsCompleted = p.ItemsCompleted, Passed = p.Passed, Failed = p.Failed })
                    .ToList(),
                TotalAttempts = velocity.TotalAttempts,
                AveragePerDay = velocity.AveragePerDay,
                Last7DaysAverage = velocity.Last7DaysAverage,
                TrendPercent = velocity.TrendPercent,
            },
            BurnDown = new BurnDownDto
            {
                Start = burnDown.Start,
                End = burnDown.End,
                TotalItems = burnDown.TotalItems,
                RemainingAtStart = burnDown.RemainingAtStart,
                RemainingNow = burnDown.RemainingNow,
                Points = burnDown.Points.Select(p => new BurnDownPointDto { Date = p.Date, Remaining = p.Remaining, Ideal = p.Ideal }).ToList(),
                ItemsPerDay = burnDown.ItemsPerDay,
                ProjectedFinish = burnDown.ProjectedFinish,
                OnTrack = burnDown.OnTrack,
            },
            DefectDensity = new DefectDensityDto
            {
                Defects = density.Defects,
                OpenDefects = density.OpenDefects,
                ResolvedDefects = density.ResolvedDefects,
                ExecutedTests = density.ExecutedTests,
                DefectsPer100Executed = density.DefectsPer100Executed,
                TestsWithDefects = density.TestsWithDefects,
                TestsWithDefectsPercent = density.TestsWithDefectsPercent,
                OpenCritical = density.OpenBySeverity.Critical,
                OpenHigh = density.OpenBySeverity.High,
                OpenMedium = density.OpenBySeverity.Medium,
                OpenLow = density.OpenBySeverity.Low,
            },
            Flaky = new FlakySummaryDto
            {
                Flaky = flaky.Count(f => f.Level == FlakinessLevel.Flaky),
                Watch = flaky.Count(f => f.Level == FlakinessLevel.Watch),
                Scored = flaky.Count(f => f.Level != FlakinessLevel.Insufficient),
            },
        };
    }
}
