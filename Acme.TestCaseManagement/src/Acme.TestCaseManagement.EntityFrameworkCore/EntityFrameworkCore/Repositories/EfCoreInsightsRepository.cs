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
        Guid? projectId = null,
        CancellationToken cancellationToken = default)
    {
        var dbContext = await _dbContextProvider.GetDbContextAsync();
        var data = new InsightsScopeData();

        var runs = dbContext.TestRuns.Where(r => (testPlanId == null || r.TestPlanId == testPlanId) && (projectId == null || r.ProjectId == projectId));
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

    public virtual async Task<List<PlanInsightData>> GetPlansDataAsync(
        IReadOnlyCollection<Guid> planIds, CancellationToken cancellationToken = default)
    {
        var dbContext = await _dbContextProvider.GetDbContextAsync();
        // A List (not an array): the Oracle MySQL provider cannot take an array as a parameter.
        var ids = planIds.ToList();
        var data = ids.ToDictionary(id => id, id => new PlanInsightData { PlanId = id });

        var runs = dbContext.TestRuns.Where(r => r.TestPlanId != null && ids.Contains(r.TestPlanId.Value));

        foreach (var group in await runs.GroupBy(r => r.TestPlanId!.Value).Select(g => new { PlanId = g.Key, Count = g.Count() }).ToListAsync(cancellationToken))
        {
            data[group.PlanId].RunCount = group.Count;
        }

        var items = await (
            from item in dbContext.TestRunItems
            join run in runs on item.TestRunId equals run.Id
            join version in dbContext.TestCaseVersions on item.TestCaseVersionId equals version.Id
            select new
            {
                PlanId = run.TestPlanId!.Value,
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
        foreach (var i in items)
        {
            data[i.PlanId].Items.Add(new InsightItem(i.RunItemId, i.TestCaseId, i.Status, i.FirstAttemptStatus, i.FirstExecutedAt));
        }

        var defects = await (
            from link in dbContext.DefectLinks
            join execution in dbContext.TestExecutions on link.TestExecutionId equals execution.Id
            join item in dbContext.TestRunItems on execution.TestRunItemId equals item.Id
            join run in runs on item.TestRunId equals run.Id
            join version in dbContext.TestCaseVersions on item.TestCaseVersionId equals version.Id
            select new { PlanId = run.TestPlanId!.Value, version.TestCaseId, link.ExternalSystem, link.IssueKey, link.Severity, link.IsResolved })
            .ToListAsync(cancellationToken);
        foreach (var d in defects)
        {
            data[d.PlanId].Defects.Add(new InsightDefect(d.TestCaseId, d.ExternalSystem, d.IssueKey, d.Severity, d.IsResolved));
        }

        return ids.Select(id => data[id]).ToList();
    }

    public virtual async Task<WorkloadScopeData> GetWorkloadDataAsync(
        Guid? testPlanId, DateTime attemptsSince, Guid? projectId = null, CancellationToken cancellationToken = default)
    {
        var dbContext = await _dbContextProvider.GetDbContextAsync();
        var runs = dbContext.TestRuns.Where(r => (testPlanId == null || r.TestPlanId == testPlanId) && (projectId == null || r.ProjectId == projectId));

        var data = new WorkloadScopeData();

        var items = await (
            from item in dbContext.TestRunItems
            join run in runs on item.TestRunId equals run.Id
            select new { item.Id, item.AssignedUserId, item.CurrentStatus }).ToListAsync(cancellationToken);
        data.Items = items.Select(i => new WorkloadItem(i.Id, i.AssignedUserId, i.CurrentStatus)).ToList();

        var attempts = await (
            from execution in dbContext.TestExecutions
            join item in dbContext.TestRunItems on execution.TestRunItemId equals item.Id
            join run in runs on item.TestRunId equals run.Id
            where execution.CreationTime >= attemptsSince
            select new { item.Id, execution.CreatorId, execution.Status, execution.DurationSeconds, execution.CreationTime })
            .ToListAsync(cancellationToken);
        data.Attempts = attempts.Select(a => new WorkloadAttempt(a.Id, a.CreatorId, a.Status, a.DurationSeconds, a.CreationTime)).ToList();

        return data;
    }
}
