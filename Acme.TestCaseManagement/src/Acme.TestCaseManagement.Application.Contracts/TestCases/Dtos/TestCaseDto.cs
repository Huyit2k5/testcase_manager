using Acme.TestCaseManagement.Enums;
using Volo.Abp.Application.Dtos;

namespace Acme.TestCaseManagement.TestCases.Dtos;

public class TestCaseDto : AuditedEntityDto<Guid>
{
    public Guid SuiteId { get; set; }

    public string Code { get; set; } = string.Empty;

    public string Title { get; set; } = string.Empty;

    public string? Description { get; set; }

    public string? Preconditions { get; set; }

    public string? Postconditions { get; set; }

    public PriorityLevel Priority { get; set; }

    public SeverityLevel Severity { get; set; }

    public TestCaseStatus Status { get; set; }

    public ExecutionType ExecutionType { get; set; }

    public TestKind Kind { get; set; }

    public TestLayer Layer { get; set; }

    public string? AutomationId { get; set; }

    public bool IsFlaky { get; set; }

    public int CurrentVersion { get; set; }

    /// <summary>Ordered by <see cref="TestStepDto.StepOrder"/>. Empty in list results; use GetAsync for the full test case.</summary>
    public List<TestStepDto> Steps { get; set; } = new();
}
