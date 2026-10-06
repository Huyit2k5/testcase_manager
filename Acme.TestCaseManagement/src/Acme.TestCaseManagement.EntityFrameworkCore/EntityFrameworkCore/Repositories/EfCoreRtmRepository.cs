using Acme.TestCaseManagement.Repositories;
using Acme.TestCaseManagement.Requirements;
using Microsoft.EntityFrameworkCore;
using Volo.Abp.DependencyInjection;
using Volo.Abp.EntityFrameworkCore;

namespace Acme.TestCaseManagement.EntityFrameworkCore.Repositories;

public class EfCoreRtmRepository : IRtmRepository, ITransientDependency
{
    private readonly IDbContextProvider<ITestCaseManagementDbContext> _dbContextProvider;

    public EfCoreRtmRepository(IDbContextProvider<ITestCaseManagementDbContext> dbContextProvider)
    {
        _dbContextProvider = dbContextProvider;
    }

    public virtual async Task<List<LatestTestResult>> GetLatestResultsAsync(
        IReadOnlyCollection<Guid> testCaseIds,
        Guid? testPlanId = null,
        string? environment = null,
        CancellationToken cancellationToken = default)
    {
        if (testCaseIds.Count == 0)
        {
            return new List<LatestTestResult>();
        }

        var ids = testCaseIds.ToList();
        var dbContext = await _dbContextProvider.GetDbContextAsync();

        // execution -> run item -> version (carries the test case id) -> run (carries environment and plan).
        // Only the highest attempt of each run item is its current result.
        var query =
            from execution in dbContext.TestExecutions
            join item in dbContext.TestRunItems on execution.TestRunItemId equals item.Id
            join version in dbContext.TestCaseVersions on item.TestCaseVersionId equals version.Id
            join run in dbContext.TestRuns on item.TestRunId equals run.Id
            where ids.Contains(version.TestCaseId)
                  && execution.AttemptNumber == dbContext.TestExecutions
                      .Where(x => x.TestRunItemId == execution.TestRunItemId)
                      .Max(x => x.AttemptNumber)
            select new
            {
                run.TestPlanId,
                run.Environment,
                Result = new LatestTestResult(
                    version.TestCaseId,
                    run.Environment,
                    execution.Status,
                    execution.CreationTime,
                    run.Id,
                    execution.Id,
                    version.VersionNumber),
            };

        if (testPlanId.HasValue)
        {
            query = query.Where(x => x.TestPlanId == testPlanId);
        }

        if (!string.IsNullOrWhiteSpace(environment))
        {
            var lowered = environment.Trim().ToLowerInvariant();
            query = query.Where(x => x.Environment.ToLower() == lowered);
        }

        return await query.Select(x => x.Result).ToListAsync(cancellationToken);
    }
}
