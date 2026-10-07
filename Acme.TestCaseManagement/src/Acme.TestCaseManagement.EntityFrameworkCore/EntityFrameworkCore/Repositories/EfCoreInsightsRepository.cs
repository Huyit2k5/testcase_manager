using Acme.TestCaseManagement.Enums;
using Acme.TestCaseManagement.Insights;
using Acme.TestCaseManagement.Repositories;
using Microsoft.EntityFrameworkCore;
using Volo.Abp.DependencyInjection;
using Volo.Abp.EntityFrameworkCore;

namespace Acme.TestCaseManagement.EntityFrameworkCore.Repositories;

public class EfCoreInsightsRepository : IInsightsRepository, ITransientDependency
{
    private readonly IDbContextProvider<ITestCaseManagementDbContext> _dbContextProvider;

    public EfCoreInsightsRepository(IDbContextProvider<ITestCaseManagementDbContext> dbContextProvider)
    {
        _dbContextProvider = dbContextProvider;
    }

    public virtual async Task<InsightsScopeData> GetScopeDataAsync(
        Guid? testPlanId,
        DateTime attemptsSince,
        CancellationToken cancellationToken = default)
    {
        var dbContext = await _dbContextProvider.GetDbContextAsync();
        var data = new InsightsScopeData();

        var runs = dbContext.TestRuns.Where(r => testPlanId == null || r.TestPlanId == testPlanId);
        data.RunCount = await runs.CountAsync(cancellationToken);

        var items = await (
            from item in dbContext.TestRunItems
            join run in runs on item.TestRunId equals run.Id
            join version in dbContext.TestCaseVersions on item.TestCaseVersionId equals version.Id
            select new
            {
                RunItemId = item.Id,
                version.TestCaseId,
                Status = item.CurrentStatus,
                FirstAttemptStatus = dbContext.TestExecutions
                    .Where(e => e.TestRunItemId == item.Id && e.AttemptNumber == 1)
                    .Select(e => (TestResultStatus?)e.Status)
                    .FirstOrDefault(),
                FirstExecutedAt = dbContext.TestExecutions
                    .Where(e => e.TestRunItemId == item.Id)
                    .OrderBy(e => e.CreationTime)
                    .Select(e => (DateTime?)e.CreationTime)
                    .FirstOrDefault(),
            }).ToListAsync(cancellationToken);

        data.Items = items
            .Select(i => new InsightItem(i.RunItemId, i.TestCaseId, i.Status, i.FirstAttemptStatus, i.FirstExecutedAt))
            .ToList();

        data.Attempts = await (
            from execution in dbContext.TestExecutions
            join item in dbContext.TestRunItems on execution.TestRunItemId equals item.Id
            join run in runs on item.TestRunId equals run.Id
            join version in dbContext.TestCaseVersions on item.TestCaseVersionId equals version.Id
            where execution.CreationTime >= attemptsSince
            select new InsightAttempt(item.Id, version.TestCaseId, execution.Status, execution.AttemptNumber, execution.CreationTime))
            .ToListAsync(cancellationToken);

        data.Defects = await (
            from link in dbContext.DefectLinks
            join execution in dbContext.TestExecutions on link.TestExecutionId equals execution.Id
            join item in dbContext.TestRunItems on execution.TestRunItemId equals item.Id
            join run in runs on item.TestRunId equals run.Id
            join version in dbContext.TestCaseVersions on item.TestCaseVersionId equals version.Id
            select new InsightDefect(version.TestCaseId, link.ExternalSystem, link.IssueKey, link.Severity, link.IsResolved))
            .ToListAsync(cancellationToken);

        return data;
    }
}
