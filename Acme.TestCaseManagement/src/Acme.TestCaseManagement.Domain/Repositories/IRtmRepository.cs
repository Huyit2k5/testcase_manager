using Acme.TestCaseManagement.Requirements;

namespace Acme.TestCaseManagement.Repositories;

public interface IRtmRepository
{
    /// <summary>
    /// For every run item of the given test cases (any version) that has at least one attempt, returns the
    /// item's latest attempt. Items that were never executed produce no row.
    /// </summary>
    /// <param name="testCaseIds">The test cases whose run items are searched.</param>
    /// <param name="testPlanId">When set, only runs of that plan are considered.</param>
    /// <param name="environment">When set, only runs on that environment (ignoring case) are considered.</param>
    /// <param name="cancellationToken">Token to cancel the query.</param>
    Task<List<LatestTestResult>> GetLatestResultsAsync(
        IReadOnlyCollection<Guid> testCaseIds,
        Guid? testPlanId = null,
        string? environment = null,
        CancellationToken cancellationToken = default);
}
