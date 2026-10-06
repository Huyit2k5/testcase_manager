using Acme.TestCaseManagement.Quality;

namespace Acme.TestCaseManagement.Repositories;

/// <summary>Everything the quality gate needs about a set of test plans, loaded in one go.</summary>
public class QualityScopeData
{
    /// <summary>Number of test runs that belong to the plans (including runs without items).</summary>
    public int RunCount { get; set; }

    /// <summary>Every run item of those runs with its current status and the priority of its test case.</summary>
    public List<QualityItemInfo> Items { get; set; } = new();

    /// <summary>Unresolved defect links on executions of those runs.</summary>
    public List<QualityDefectInfo> OpenDefects { get; set; } = new();
}

public interface IQualityRepository
{
    /// <summary>
    /// Loads the items and open defects of the runs of the given plans. Items stay in scope when their library
    /// test case is later deleted, so deleting a failing test case can never improve a pass rate.
    /// </summary>
    Task<QualityScopeData> GetScopeDataAsync(
        IReadOnlyCollection<Guid> testPlanIds,
        CancellationToken cancellationToken = default);
}
