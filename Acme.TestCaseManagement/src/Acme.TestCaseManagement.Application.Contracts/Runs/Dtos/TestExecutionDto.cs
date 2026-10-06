using Acme.TestCaseManagement.Enums;
using Volo.Abp.Application.Dtos;

namespace Acme.TestCaseManagement.Runs.Dtos;

/// <summary>One immutable execution attempt. <c>CreatorId</c> is the tester who recorded it.</summary>
public class TestExecutionDto : CreationAuditedEntityDto<Guid>
{
    public Guid TestRunItemId { get; set; }

    public int AttemptNumber { get; set; }

    public TestResultStatus Status { get; set; }

    public string? ActualResult { get; set; }

    public int DurationSeconds { get; set; }

    /// <summary>External issues linked to this attempt, oldest first.</summary>
    public List<DefectLinkDto> DefectLinks { get; set; } = new();
}
