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

    public virtual async Task<DashboardDto> GetAsync(GetDashboardInput input)
    {
        var now = Clock.Now;
        var today = now.Date;

        var plan = input.TestPlanId.HasValue ? await _plans.GetAsync(input.TestPlanId.Value) : null;

        // Attempts are read far enough back for both the velocity chart and the flakiness window.
        var since = today.AddDays(-Math.Max(input.Days, _options.LookbackDays));
        var data = await _insights.GetScopeDataAsync(input.TestPlanId, since);

        var progress = DashboardCalculator.Progress(data.Items);
        var velocity = DashboardCalculator.Velocity(data.Attempts, data.Items, today, input.Days);

        // A plan burns down over its own dates; without a plan (or without a start date) the chart starts at the first day of the window.
        var windowStart = today.AddDays(-(input.Days - 1));
        var start = plan?.StartDate?.Date is { } planStart && planStart <= today ? planStart : windowStart;
        var burnDown = DashboardCalculator.BurnDown(data.Items, start, plan?.EndDate, today);

        var density = DashboardCalculator.DefectDensity(data.Items, data.Defects);
        var flaky = FlakinessCalculator.CalculateAll(data.Attempts, _options.ToSettings());

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
