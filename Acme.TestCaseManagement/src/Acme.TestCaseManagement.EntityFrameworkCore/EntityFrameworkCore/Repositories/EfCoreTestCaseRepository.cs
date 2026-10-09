using System.Linq.Dynamic.Core;
using Acme.TestCaseManagement.Repositories;
using Acme.TestCaseManagement.TestCases;
using Microsoft.EntityFrameworkCore;
using Volo.Abp.Domain.Repositories.EntityFrameworkCore;
using Volo.Abp.EntityFrameworkCore;

namespace Acme.TestCaseManagement.EntityFrameworkCore.Repositories;

public class EfCoreTestCaseRepository
    : EfCoreRepository<ITestCaseManagementDbContext, TestCase, Guid>, ITestCaseRepository
{
    public EfCoreTestCaseRepository(IDbContextProvider<ITestCaseManagementDbContext> dbContextProvider)
        : base(dbContextProvider)
    {
    }

    public virtual async Task<TestCase?> FindByCodeAsync(
        string code, bool includeDetails = false, CancellationToken cancellationToken = default)
    {
        return await (await GetQueryableAsync())
            .IncludeDetails(includeDetails)
            .FirstOrDefaultAsync(x => x.Code == code, GetCancellationToken(cancellationToken));
    }

    public virtual async Task<TestCase?> FindByAutomationIdAsync(string automationId, CancellationToken cancellationToken = default)
    {
        // Lower-casing both sides makes the match behave the same on SQL Server, PostgreSQL and SQLite.
        var lowered = automationId.Trim().ToLowerInvariant();
        return await (await GetQueryableAsync())
            .FirstOrDefaultAsync(x => x.AutomationId != null && x.AutomationId.ToLower() == lowered, GetCancellationToken(cancellationToken));
    }

    public virtual async Task<List<TestCase>> GetListByAutomationIdsAsync(
        IReadOnlyCollection<string> automationIds, CancellationToken cancellationToken = default)
    {
        var lowered = automationIds.Select(id => id.Trim().ToLowerInvariant()).Distinct().ToList();
        return await (await GetQueryableAsync())
            .Where(x => x.AutomationId != null && lowered.Contains(x.AutomationId.ToLower()))
            .ToListAsync(GetCancellationToken(cancellationToken));
    }

    public virtual async Task<List<TestCase>> GetFilteredListAsync(
        TestCaseFilter filter,
        string? sorting = null,
        int skipCount = 0,
        int maxResultCount = int.MaxValue,
        bool includeDetails = false,
        CancellationToken cancellationToken = default)
    {
        var query = ApplyFilter(await GetQueryableAsync(), filter);

        return await query
            .IncludeDetails(includeDetails)
            .Include(x => x.Tags)
            .OrderBy(string.IsNullOrWhiteSpace(sorting) ? nameof(TestCase.Code) : sorting)
            .Skip(skipCount)
            .Take(maxResultCount)
            .ToListAsync(GetCancellationToken(cancellationToken));
    }

    public virtual async Task<long> GetFilteredCountAsync(
        TestCaseFilter filter, CancellationToken cancellationToken = default)
    {
        return await ApplyFilter(await GetQueryableAsync(), filter)
            .LongCountAsync(GetCancellationToken(cancellationToken));
    }

    protected virtual IQueryable<TestCase> ApplyFilter(IQueryable<TestCase> query, TestCaseFilter filter)
    {
        if (!string.IsNullOrWhiteSpace(filter.SearchText))
        {
            // Lower-casing both sides makes the search behave the same on SQL Server, PostgreSQL and SQLite.
            var text = filter.SearchText.Trim().ToLowerInvariant();
            query = query.Where(x =>
                x.Code.ToLower().Contains(text) ||
                x.Title.ToLower().Contains(text) ||
                (x.Description != null && x.Description.ToLower().Contains(text)));
        }

        if (filter.SuiteIds != null)
        {
            var suiteIds = filter.SuiteIds.ToList();
            query = query.Where(x => suiteIds.Contains(x.SuiteId));
        }

        foreach (var tag in (filter.Tags ?? Array.Empty<string>()).Select(TagNames.Normalize).Where(t => t.Length > 0).Distinct())
        {
            var key = tag;
            query = query.Where(x => x.Tags.Any(t => t.NormalizedName == key));
        }

        return query
            .WhereIf(filter.HasAutomationId == true, x => x.AutomationId != null && x.AutomationId != string.Empty)
            .WhereIf(filter.HasAutomationId == false, x => x.AutomationId == null || x.AutomationId == string.Empty)
            .WhereIf(filter.Status.HasValue, x => x.Status == filter.Status)
            .WhereIf(filter.Priority.HasValue, x => x.Priority == filter.Priority)
            .WhereIf(filter.Severity.HasValue, x => x.Severity == filter.Severity)
            .WhereIf(filter.ExecutionType.HasValue, x => x.ExecutionType == filter.ExecutionType)
            .WhereIf(filter.Kind.HasValue, x => x.Kind == filter.Kind)
            .WhereIf(filter.Layer.HasValue, x => x.Layer == filter.Layer);
    }

    public virtual async Task<List<TestCase>> GetListBySharedStepGroupAsync(Guid groupId, CancellationToken cancellationToken = default)
    {
        return await (await GetQueryableAsync())
            .IncludeDetails()
            .Where(x => x.Steps.Any(s => s.SharedStepGroupId == groupId))
            .OrderBy(x => x.Code)
            .ToListAsync(GetCancellationToken(cancellationToken));
    }

    public virtual async Task<Dictionary<Guid, int>> GetSharedStepUsageCountsAsync(CancellationToken cancellationToken = default)
    {
        var dbContext = await GetDbContextAsync();
        var testCases = await GetQueryableAsync();
        var rows = await (
            from step in dbContext.Set<TestStep>()
            join testCase in testCases on step.TestCaseId equals testCase.Id
            where step.SharedStepGroupId != null
            select new { GroupId = step.SharedStepGroupId!.Value, step.TestCaseId })
            .Distinct()
            .ToListAsync(GetCancellationToken(cancellationToken));

        return rows.GroupBy(r => r.GroupId).ToDictionary(g => g.Key, g => g.Count());
    }

    public virtual async Task<List<TagSummary>> GetTagSummariesAsync(IReadOnlyCollection<Guid>? suiteIds = null, CancellationToken cancellationToken = default)
    {
        // Joined with the (not deleted) test cases, so the tags of a deleted test case are not counted.
        var dbContext = await GetDbContextAsync();
        var testCases = await GetQueryableAsync();
        if (suiteIds != null)
        {
            var ids = suiteIds.ToList();
            testCases = testCases.Where(t => ids.Contains(t.SuiteId));
        }

        var rows = await (
            from tag in dbContext.Set<TestCaseTag>()
            join testCase in testCases on tag.TestCaseId equals testCase.Id
            select new { tag.NormalizedName, tag.Name })
            .ToListAsync(GetCancellationToken(cancellationToken));

        return rows
            .GroupBy(r => r.NormalizedName)
            .Select(g => new TagSummary(g.GroupBy(r => r.Name).OrderByDescending(n => n.Count()).ThenBy(n => n.Key, StringComparer.Ordinal).First().Key, g.Count()))
            .OrderByDescending(t => t.Count)
            .ThenBy(t => t.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }
}
