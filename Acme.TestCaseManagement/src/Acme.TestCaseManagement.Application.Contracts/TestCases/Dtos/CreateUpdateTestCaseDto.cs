using System.ComponentModel.DataAnnotations;
using Acme.TestCaseManagement.Enums;

namespace Acme.TestCaseManagement.TestCases.Dtos;

public class CreateUpdateTestCaseDto
{
    public Guid SuiteId { get; set; }

    [Required]
    [StringLength(TestCaseConsts.MaxCodeLength)]
    public string Code { get; set; } = string.Empty;

    [Required]
    [StringLength(TestCaseConsts.MaxTitleLength)]
    public string Title { get; set; } = string.Empty;

    [StringLength(TestCaseConsts.MaxTextLength)]
    public string? Description { get; set; }

    [StringLength(TestCaseConsts.MaxTextLength)]
    public string? Preconditions { get; set; }

    [StringLength(TestCaseConsts.MaxTextLength)]
    public string? Postconditions { get; set; }

    public PriorityLevel Priority { get; set; } = PriorityLevel.Medium;

    public SeverityLevel Severity { get; set; } = SeverityLevel.Medium;

    public ExecutionType ExecutionType { get; set; } = ExecutionType.Manual;

    public TestKind Kind { get; set; } = TestKind.Functional;

    public TestLayer Layer { get; set; } = TestLayer.Acceptance;

    [StringLength(TestCaseConsts.MaxAutomationIdLength)]
    public string? AutomationId { get; set; }

    public bool IsFlaky { get; set; }

    /// <summary>The complete ordered step list. On update, steps that are omitted are removed.</summary>
    public List<TestStepDto> Steps { get; set; } = new();

    /// <summary>Recorded on the new <c>TestCaseVersion</c> when an approved test case is modified.</summary>
    [StringLength(TestCaseConsts.MaxChangeSummaryLength)]
    public string? ChangeSummary { get; set; }
}
