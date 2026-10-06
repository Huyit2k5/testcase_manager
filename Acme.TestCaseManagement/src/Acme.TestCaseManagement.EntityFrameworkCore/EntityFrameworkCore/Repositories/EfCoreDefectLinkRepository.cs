using Acme.TestCaseManagement.Quality;
using Acme.TestCaseManagement.Repositories;
using Microsoft.EntityFrameworkCore;
using Volo.Abp.Domain.Repositories.EntityFrameworkCore;
using Volo.Abp.EntityFrameworkCore;

namespace Acme.TestCaseManagement.EntityFrameworkCore.Repositories;

public class EfCoreDefectLinkRepository
    : EfCoreRepository<ITestCaseManagementDbContext, DefectLink, Guid>, IDefectLinkRepository
{
    public EfCoreDefectLinkRepository(IDbContextProvider<ITestCaseManagementDbContext> dbContextProvider)
        : base(dbContextProvider)
    {
    }

    public virtual async Task<List<DefectLinkWithContext>> GetListByTestCaseAsync(
        Guid testCaseId, CancellationToken cancellationToken = default)
    {
        var query = (await BuildContextQueryAsync())
            .Where(x => x.TestCaseId == testCaseId);

        var rows = await Order(query).ToListAsync(GetCancellationToken(cancellationToken));
        return rows.Select(ToContext).ToList();
    }

    public virtual async Task<List<DefectLinkWithContext>> GetOpenListByTestCasesAsync(
        IReadOnlyCollection<Guid> testCaseIds,
        Guid? testPlanId = null,
        string? environment = null,
        CancellationToken cancellationToken = default)
    {
        if (testCaseIds.Count == 0)
        {
            return new List<DefectLinkWithContext>();
        }

        var ids = testCaseIds.ToList();
        var query = (await BuildContextQueryAsync())
            .Where(x => !x.Link.IsResolved && ids.Contains(x.TestCaseId));

        if (testPlanId.HasValue)
        {
            query = query.Where(x => x.TestPlanId == testPlanId);
        }

        if (!string.IsNullOrWhiteSpace(environment))
        {
            var lowered = environment.Trim().ToLowerInvariant();
            query = query.Where(x => x.Environment.ToLower() == lowered);
        }

        var rows = await Order(query).ToListAsync(GetCancellationToken(cancellationToken));
        return rows.Select(ToContext).ToList();
    }

    private sealed class Row
    {
        public DefectLink Link { get; init; } = default!;
        public Guid TestCaseId { get; init; }
        public Guid TestRunId { get; init; }
        public Guid? TestPlanId { get; init; }
        public string TestRunTitle { get; init; } = string.Empty;
        public string Environment { get; init; } = string.Empty;
        public int AttemptNumber { get; init; }
        public int VersionNumber { get; init; }
        public DateTime ExecutionTime { get; init; }
    }

    /// <summary>
    /// Link -> execution -> run item -> version (carries the test case id) -> run.
    /// ABP's soft-delete and tenant filters apply to every joined set.
    /// </summary>
    private async Task<IQueryable<Row>> BuildContextQueryAsync()
    {
        var dbContext = await GetDbContextAsync();

        return
            from link in dbContext.DefectLinks
            join execution in dbContext.TestExecutions on link.TestExecutionId equals execution.Id
            join item in dbContext.TestRunItems on execution.TestRunItemId equals item.Id
            join version in dbContext.TestCaseVersions on item.TestCaseVersionId equals version.Id
            join run in dbContext.TestRuns on item.TestRunId equals run.Id
            select new Row
            {
                Link = link,
                TestCaseId = version.TestCaseId,
                TestRunId = run.Id,
                TestPlanId = run.TestPlanId,
                TestRunTitle = run.Title,
                Environment = run.Environment,
                AttemptNumber = execution.AttemptNumber,
                VersionNumber = version.VersionNumber,
                ExecutionTime = execution.CreationTime,
            };
    }

    private static IOrderedQueryable<Row> Order(IQueryable<Row> query)
    {
        return query.OrderBy(x => x.ExecutionTime).ThenBy(x => x.AttemptNumber).ThenBy(x => x.Link.CreationTime);
    }

    private static DefectLinkWithContext ToContext(Row row)
    {
        return new DefectLinkWithContext
        {
            Link = row.Link,
            TestCaseId = row.TestCaseId,
            TestRunId = row.TestRunId,
            TestRunTitle = row.TestRunTitle,
            Environment = row.Environment,
            AttemptNumber = row.AttemptNumber,
            VersionNumber = row.VersionNumber,
            ExecutionTime = row.ExecutionTime,
        };
    }
}
