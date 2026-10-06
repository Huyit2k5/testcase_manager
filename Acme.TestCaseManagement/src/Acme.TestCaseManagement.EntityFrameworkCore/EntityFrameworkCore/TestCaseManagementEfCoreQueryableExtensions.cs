using Acme.TestCaseManagement.Quality;
using Acme.TestCaseManagement.Runs;
using Acme.TestCaseManagement.TestCases;
using Microsoft.EntityFrameworkCore;

namespace Acme.TestCaseManagement.EntityFrameworkCore;

public static class TestCaseManagementEfCoreQueryableExtensions
{
    public static IQueryable<TestCase> IncludeDetails(this IQueryable<TestCase> queryable, bool include = true)
    {
        return include ? queryable.Include(x => x.Steps) : queryable;
    }

    public static IQueryable<SignOffReport> IncludeDetails(this IQueryable<SignOffReport> queryable, bool include = true)
    {
        return include ? queryable.Include(x => x.Approvals) : queryable;
    }

    public static IQueryable<TestRun> IncludeDetails(this IQueryable<TestRun> queryable, bool include = true)
    {
        return include ? queryable.Include(x => x.Items) : queryable;
    }
}
