using Acme.TestCaseManagement.Enums;
using Acme.TestCaseManagement.Insights.Dtos;
using Acme.TestCaseManagement.Permissions;
using Acme.TestCaseManagement.Repositories;
using Acme.TestCaseManagement.TestCases;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;
using Volo.Abp.Domain.Repositories;

namespace Acme.TestCaseManagement.Insights;

[Authorize(TestCaseManagementPermissions.TestCases.Default)]
public class FlakyTestAppService : TestCaseManagementAppService, IFlakyTestAppService
{
    private readonly IInsightsRepository _insights;
    private readonly IRepository<TestCase, Guid> _testCases;
    private readonly TestCaseManagementInsightsOptions _options;

    public FlakyTestAppService(
        IInsightsRepository insights,
        IRepository<TestCase, Guid> testCases,
        IOptions<TestCaseManagementInsightsOptions> options)
    {
        _insights = insights;
        _testCases = testCases;
        _options = options.Value;
    }

    public virtual async Task<FlakyTestListDto> GetListAsync(GetFlakyTestsInput input)
    {
        var scored = await ScoreAsync();

        var filter = input.Filter?.Trim();
        var matching = scored
            .Where(s => s.Result.Level >= input.MinimumLevel)
            .Where(s => string.IsNullOrEmpty(filter)
                || s.TestCase.Code.Contains(filter, StringComparison.OrdinalIgnoreCase)
                || s.TestCase.Title.Contains(filter, StringComparison.OrdinalIgnoreCase)
                || (s.TestCase.AutomationId?.Contains(filter, StringComparison.OrdinalIgnoreCase) ?? false))
            .OrderByDescending(s => s.Result.Level)
            .ThenByDescending(s => s.Result.Score)
            .ThenByDescending(s => s.Result.Observations)
            .ThenBy(s => s.TestCase.Code, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return new FlakyTestListDto
        {
            Items = matching.Take(input.MaxResultCount).Select(s => ToDto(s.TestCase, s.Result)).ToList(),
            TotalCount = matching.Count,
            FlakyCount = scored.Count(s => s.Result.Level == FlakinessLevel.Flaky),
            WatchCount = scored.Count(s => s.Result.Level == FlakinessLevel.Watch),
            Settings = new FlakySettingsDto
            {
                WindowSize = _options.WindowSize,
                MinimumObservations = _options.MinimumObservations,
                WatchScore = _options.WatchScore,
                FlakyScore = _options.FlakyScore,
                LookbackDays = _options.LookbackDays,
            },
        };
    }

    [Authorize(TestCaseManagementPermissions.TestCases.Update)]
    public virtual async Task<ApplyFlakyFlagsResultDto> ApplyAsync(ApplyFlakyFlagsInput input)
    {
        var scored = await ScoreAsync();
        var result = new ApplyFlakyFlagsResultDto();

        foreach (var item in scored.OrderBy(s => s.TestCase.Code, StringComparer.OrdinalIgnoreCase))
        {
            if (item.Result.Level == FlakinessLevel.Flaky && !item.TestCase.IsFlaky)
            {
                item.TestCase.SetFlaky(true);
                await _testCases.UpdateAsync(item.TestCase);
                result.FlaggedCodes.Add(item.TestCase.Code);
            }
            else if (input.ClearRecovered && item.Result.Level == FlakinessLevel.Stable && item.TestCase.IsFlaky)
            {
                item.TestCase.SetFlaky(false);
                await _testCases.UpdateAsync(item.TestCase);
                result.ClearedCodes.Add(item.TestCase.Code);
            }
        }

        result.Flagged = result.FlaggedCodes.Count;
        result.Cleared = result.ClearedCodes.Count;
        return result;
    }

    /// <summary>The score of every test case that still exists and has attempts in the lookback window.</summary>
    private async Task<List<(TestCase TestCase, FlakinessResult Result)>> ScoreAsync()
    {
        var since = Clock.Now.AddDays(-_options.LookbackDays);
        var data = await _insights.GetScopeDataAsync(null, since);
        var results = FlakinessCalculator.CalculateAll(data.Attempts, _options.ToSettings());

        var ids = results.Select(r => r.TestCaseId).ToList();
        var testCases = (await _testCases.GetListAsync(t => ids.Contains(t.Id))).ToDictionary(t => t.Id);

        // A deleted test case has no row here (soft delete), so it drops out of the list.
        return results
            .Where(r => testCases.ContainsKey(r.TestCaseId))
            .Select(r => (testCases[r.TestCaseId], r))
            .ToList();
    }

    private static FlakyTestDto ToDto(TestCase testCase, FlakinessResult result) => new()
    {
        TestCaseId = testCase.Id,
        Code = testCase.Code,
        Title = testCase.Title,
        SuiteId = testCase.SuiteId,
        AutomationId = testCase.AutomationId,
        Observations = result.Observations,
        Passes = result.Passes,
        Failures = result.Failures,
        Flips = result.Flips,
        Score = result.Score,
        Level = result.Level,
        IsFlagged = testCase.IsFlaky,
        LastResultAt = result.LastResultAt,
        LastFailedAt = result.LastFailedAt,
    };
}
