using Acme.TestCaseManagement.Enums;
using Volo.Abp.Application.Dtos;

namespace Acme.TestCaseManagement.TestCases.Dtos;

public class GetTestCaseListInput : PagedAndSortedResultRequestDto
{
    /// <summary>Matches Code, Title or Description.</summary>
    public string? Filter { get; set; }

    public Guid? SuiteId { get; set; }

    /// <summary>When a suite is selected, also include test cases of its descendant suites.</summary>
    public bool IncludeDescendantSuites { get; set; } = true;

    public TestCaseStatus? Status { get; set; }

    public PriorityLevel? Priority { get; set; }

    public SeverityLevel? Severity { get; set; }

    public ExecutionType? ExecutionType { get; set; }

    public TestKind? Kind { get; set; }

    public TestLayer? Layer { get; set; }
}
