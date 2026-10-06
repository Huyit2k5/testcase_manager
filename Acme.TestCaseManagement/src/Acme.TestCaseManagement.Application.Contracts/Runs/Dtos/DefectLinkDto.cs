using System.ComponentModel.DataAnnotations;
using Acme.TestCaseManagement.Enums;
using Volo.Abp.Application.Dtos;

namespace Acme.TestCaseManagement.Runs.Dtos;

public class DefectLinkDto : CreationAuditedEntityDto<Guid>
{
    public Guid TestExecutionId { get; set; }

    public string ExternalSystem { get; set; } = string.Empty;

    public string IssueKey { get; set; } = string.Empty;

    public string? IssueUrl { get; set; }

    public SeverityLevel Severity { get; set; }

    /// <summary>False while the defect is open. Quality gates count open Critical and High defects.</summary>
    public bool IsResolved { get; set; }

    public DateTime? ResolvedTime { get; set; }
}

public class AddDefectLinkDto
{
    /// <summary>Tracker name, for example "Jira" or "GitHub".</summary>
    [Required]
    [StringLength(DefectLinkConsts.MaxExternalSystemLength)]
    public string ExternalSystem { get; set; } = string.Empty;

    /// <summary>Ticket identifier, for example "BUG-88" or "#42".</summary>
    [Required]
    [StringLength(DefectLinkConsts.MaxIssueKeyLength)]
    public string IssueKey { get; set; } = string.Empty;

    /// <summary>Optional absolute http(s) address of the ticket.</summary>
    [StringLength(DefectLinkConsts.MaxIssueUrlLength)]
    public string? IssueUrl { get; set; }

    /// <summary>Impact of the defect. When omitted it defaults to the severity of the failing test case.</summary>
    public SeverityLevel? Severity { get; set; }
}

public class UpdateDefectLinkDto
{
    public SeverityLevel Severity { get; set; }

    /// <summary>Set to true when the defect is fixed or closed in the tracker; false reopens it.</summary>
    public bool IsResolved { get; set; }
}
