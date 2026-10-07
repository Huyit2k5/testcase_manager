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

    /// <summary>Tags a test case must have, all of them; compared ignoring case.</summary>
    public IReadOnlyCollection<string>? Tags { get; set; }

    /// <summary>True: only test cases with an Automation ID; false: only those without one.</summary>
    public bool? HasAutomationId { get; set; }
}

/// <summary>A tag and the number of test cases (not deleted) that have it.</summary>
public record TagSummary(string Name, int Count);

public interface ITestCaseRepository : IRepository<TestCase, Guid>
{
    Task<TestCase?> FindByCodeAsync(string code, bool includeDetails = false, CancellationToken cancellationToken = default);

    /// <summary>The test case with this Automation ID (ignoring case), or null.</summary>
    Task<TestCase?> FindByAutomationIdAsync(string automationId, CancellationToken cancellationToken = default);

    /// <summary>Every test case whose Automation ID is one of these (ignoring case), in one query.</summary>
    Task<List<TestCase>> GetListByAutomationIdsAsync(IReadOnlyCollection<string> automationIds, CancellationToken cancellationToken = default);

    Task<List<TestCase>> GetFilteredListAsync(
        TestCaseFilter filter,
        string? sorting = null,
        int skipCount = 0,
        int maxResultCount = int.MaxValue,
        bool includeDetails = false,
        CancellationToken cancellationToken = default);

    Task<long> GetFilteredCountAsync(TestCaseFilter filter, CancellationToken cancellationToken = default);

    /// <summary>The test cases (not deleted) with at least one step copied from the group, with their steps.</summary>
    Task<List<TestCase>> GetListBySharedStepGroupAsync(Guid groupId, CancellationToken cancellationToken = default);

    /// <summary>For each group of shared steps, the number of test cases (not deleted) that use it.</summary>
    Task<Dictionary<Guid, int>> GetSharedStepUsageCountsAsync(CancellationToken cancellationToken = default);

    /// <summary>Every tag in use, with the number of test cases that have it, the most used first.</summary>
    Task<List<TagSummary>> GetTagSummariesAsync(CancellationToken cancellationToken = default);
}
