using Volo.Abp.Application.Dtos;

namespace Acme.TestCaseManagement.TestCases.Dtos;

/// <summary>Read-only view of an immutable <c>TestCaseVersion</c> snapshot.</summary>
public class TestCaseVersionDto : CreationAuditedEntityDto<Guid>
{
    public Guid TestCaseId { get; set; }

    public int VersionNumber { get; set; }

    public string Title { get; set; } = string.Empty;

    public string? Preconditions { get; set; }

    public string? Postconditions { get; set; }

    public string? ChangeSummary { get; set; }

    public List<TestStepDto> Steps { get; set; } = new();
}
