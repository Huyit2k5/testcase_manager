using Acme.TestCaseManagement.Enums;
using Acme.TestCaseManagement.Quality;
using Acme.TestCaseManagement.Repositories;
using Microsoft.EntityFrameworkCore;
using Volo.Abp;
using Volo.Abp.Data;
using Volo.Abp.DependencyInjection;
using Volo.Abp.EntityFrameworkCore;

namespace Acme.TestCaseManagement.EntityFrameworkCore.Repositories;

public class EfCoreQualityRepository : IQualityRepository, ITransientDependency
{
    private readonly IDbContextProvider<ITestCaseManagementDbContext> _dbContextProvider;
    private readonly IDataFilter _dataFilter;

    public EfCoreQualityRepository(
        IDbContextProvider<ITestCaseManagementDbContext> dbContextProvider,
        IDataFilter dataFilter)
    {
        _dbContextProvider = dbContextProvider;
        _dataFilter = dataFilter;
    }

    public virtual async Task<QualityScopeData> GetScopeDataAsync(
        IReadOnlyCollection<Guid> testPlanIds,
        CancellationToken cancellationToken = default)
    {
        var data = new QualityScopeData();
        if (testPlanIds.Count == 0)
        {
            return data;
        }

        var planIds = testPlanIds.ToList();
        var dbContext = await _dbContextProvider.GetDbContextAsync();

        data.RunCount = await dbContext.TestRuns
            .CountAsync(r => r.TestPlanId != null && planIds.Contains(r.TestPlanId.Value), cancellationToken);

        var items = await (
            from item in dbContext.TestRunItems
            join run in dbContext.TestRuns on item.TestRunId equals run.Id
            join version in dbContext.TestCaseVersions on item.TestCaseVersionId equals version.Id
            where run.TestPlanId != null && planIds.Contains(run.TestPlanId.Value)
            select new
            {
                RunItemId = item.Id,
                RunId = run.Id,
                version.TestCaseId,
                Status = item.CurrentStatus,
                FirstAttemptStatus = dbContext.TestExecutions
                    .Where(e => e.TestRunItemId == item.Id && e.AttemptNumber == 1)
                    .Select(e => (TestResultStatus?)e.Status)
                    .FirstOrDefault(),
            }).ToListAsync(cancellationToken);

        // The priority comes from the library test case. A soft-deleted test case still gives its priority, otherwise
        // its items would silently leave the scope.
        var testCaseIds = items.Select(i => i.TestCaseId).Distinct().ToList();
        Dictionary<Guid, PriorityLevel> priorities;
        using (_dataFilter.Disable<ISoftDelete>())
        {
            priorities = await dbContext.TestCases
                .Where(t => testCaseIds.Contains(t.Id))
                .Select(t => new { t.Id, t.Priority })
                .ToDictionaryAsync(t => t.Id, t => t.Priority, cancellationToken);
        }

        data.Items = items
            .Select(i => new QualityItemInfo(
                i.RunItemId,
                i.RunId,
                i.TestCaseId,
                priorities.GetValueOrDefault(i.TestCaseId, PriorityLevel.Medium),
                i.Status,
                i.FirstAttemptStatus))
            .ToList();

        data.OpenDefects = await (
            from link in dbContext.DefectLinks
            join execution in dbContext.TestExecutions on link.TestExecutionId equals execution.Id
            join item in dbContext.TestRunItems on execution.TestRunItemId equals item.Id
            join run in dbContext.TestRuns on item.TestRunId equals run.Id
            where !link.IsResolved && run.TestPlanId != null && planIds.Contains(run.TestPlanId.Value)
            select new QualityDefectInfo(link.Id, link.ExternalSystem, link.IssueKey, link.IssueUrl, link.Severity))
            .ToListAsync(cancellationToken);

        return data;
    }
}
