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

        return query
            .WhereIf(filter.Status.HasValue, x => x.Status == filter.Status)
            .WhereIf(filter.Priority.HasValue, x => x.Priority == filter.Priority)
            .WhereIf(filter.Severity.HasValue, x => x.Severity == filter.Severity)
            .WhereIf(filter.ExecutionType.HasValue, x => x.ExecutionType == filter.ExecutionType)
            .WhereIf(filter.Kind.HasValue, x => x.Kind == filter.Kind)
            .WhereIf(filter.Layer.HasValue, x => x.Layer == filter.Layer);
    }
}
