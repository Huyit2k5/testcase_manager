using System.ComponentModel.DataAnnotations;
using Volo.Abp.Application.Dtos;

namespace Acme.TestCaseManagement.QualityGates.Dtos;

public class QualityGateDto : AuditedEntityDto<Guid>
{
    public string Name { get; set; } = string.Empty;

    public string? Description { get; set; }

    /// <summary>Minimum pass rate in percent.</summary>
    public decimal MinPassRate { get; set; }

    /// <summary>Distinct approvers a sign-off needs.</summary>
    public int RequiredApprovals { get; set; }

    public bool IsDefault { get; set; }
}

public class CreateUpdateQualityGateDto
{
    /// <summary>Unique within the tenant (ignoring case), e.g. "Release".</summary>
    [Required]
    [StringLength(QualityGateConsts.MaxNameLength)]
    public string Name { get; set; } = string.Empty;

    [StringLength(QualityGateConsts.MaxDescriptionLength)]
    public string? Description { get; set; }

    /// <summary>Greater than 0 and at most 100. The open Critical/High defect and P1 rules are fixed and not configurable.</summary>
    [Range(typeof(decimal), "0.01", "100")]
    public decimal MinPassRate { get; set; } = QualityGateConsts.DefaultMinPassRate;

    [Range(1, QualityGateConsts.MaxRequiredApprovals)]
    public int RequiredApprovals { get; set; } = QualityGateConsts.DefaultRequiredApprovals;

    /// <summary>
    /// Makes this the gate used when none is named. Only one gate can be the default; setting it moves the flag.
    /// Without a default gate the built-in baseline (95%, 2 approvals) applies.
    /// </summary>
    public bool IsDefault { get; set; }
}
