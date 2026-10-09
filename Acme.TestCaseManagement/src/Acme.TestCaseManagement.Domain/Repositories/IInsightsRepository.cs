using Acme.TestCaseManagement.Insights;

namespace Acme.TestCaseManagement.Repositories;

/// <summary>What flaky detection and the dashboard need about the runs of a scope, loaded in one go.</summary>
public interface IInsightsRepository
{
    /// <summary>
    /// Loads the run items, the attempts made at or after <paramref name="attemptsSince"/>, and the defect links of the runs of
    /// the scope: the runs of one test plan, or every run (also those that belong to no plan, such as CI runs) when
    /// <paramref name="testPlanId"/> is null. Items stay in scope when their library test case is deleted later.
    /// </summary>
    Task<InsightsScopeData> GetScopeDataAsync(
        Guid? testPlanId,
        DateTime attemptsSince,
        Guid? projectId = null,
        CancellationToken cancellationToken = default);
}
