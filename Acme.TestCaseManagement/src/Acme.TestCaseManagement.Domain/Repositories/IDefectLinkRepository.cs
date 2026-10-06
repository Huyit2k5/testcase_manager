using Acme.TestCaseManagement.Quality;
using Volo.Abp.Domain.Repositories;

namespace Acme.TestCaseManagement.Repositories;

/// <summary>A defect link together with the test case and run context in which its execution happened.</summary>
public class DefectLinkWithContext
{
    public DefectLink Link { get; set; } = default!;

    public Guid TestCaseId { get; set; }

    public Guid TestRunId { get; set; }

    public string TestRunTitle { get; set; } = string.Empty;

    public string Environment { get; set; } = string.Empty;

    public int AttemptNumber { get; set; }

    /// <summary>Version of the test case that was executed.</summary>
    public int VersionNumber { get; set; }

    public DateTime ExecutionTime { get; set; }
}

public interface IDefectLinkRepository : IRepository<DefectLink, Guid>
{
    /// <summary>
    /// Every defect linked to any execution of any version of the test case, across all runs,
    /// oldest execution first.
    /// </summary>
    Task<List<DefectLinkWithContext>> GetListByTestCaseAsync(Guid testCaseId, CancellationToken cancellationToken = default);

    /// <summary>
    /// The unresolved defects linked to executions of the given test cases, oldest first.
    /// </summary>
    /// <param name="testCaseIds">The test cases whose executions are searched.</param>
    /// <param name="testPlanId">When set, only executions in runs of that plan are considered.</param>
    /// <param name="environment">When set, only executions in runs on that environment (ignoring case) are considered.</param>
    /// <param name="cancellationToken">Token to cancel the query.</param>
    Task<List<DefectLinkWithContext>> GetOpenListByTestCasesAsync(
        IReadOnlyCollection<Guid> testCaseIds,
        Guid? testPlanId = null,
        string? environment = null,
        CancellationToken cancellationToken = default);
}
