using Acme.TestCaseManagement.Enums;
using Acme.TestCaseManagement.TestCases;
using Volo.Abp.Domain.Repositories;

namespace Acme.TestCaseManagement.Repositories;

public class TestCaseFilter
{
    /// <summary>Matches Code, Title or Description.</summary>
    public string? SearchText { get; set; }

    /// <summary>When set, only test cases in one of these suites are returned.</summary>
    public IReadOnlyCollection<Guid>? SuiteIds { get; set; }

    public TestCaseStatus? Status { get; set; }

    public PriorityLevel? Priority { get; set; }

    public SeverityLevel? Severity { get; set; }

    public ExecutionType? ExecutionType { get; set; }

    public TestKind? Kind { get; set; }

    public TestLayer? Layer { get; set; }
}

public interface ITestCaseRepository : IRepository<TestCase, Guid>
{
    Task<TestCase?> FindByCodeAsync(string code, bool includeDetails = false, CancellationToken cancellationToken = default);

    Task<List<TestCase>> GetFilteredListAsync(
        TestCaseFilter filter,
        string? sorting = null,
        int skipCount = 0,
        int maxResultCount = int.MaxValue,
        bool includeDetails = false,
        CancellationToken cancellationToken = default);

    Task<long> GetFilteredCountAsync(TestCaseFilter filter, CancellationToken cancellationToken = default);
}
