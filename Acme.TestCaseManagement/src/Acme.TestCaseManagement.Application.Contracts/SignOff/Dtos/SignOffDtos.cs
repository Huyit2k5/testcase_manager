using System.ComponentModel.DataAnnotations;
using Acme.TestCaseManagement.Enums;
using Acme.TestCaseManagement.QualityGates.Dtos;
using Volo.Abp.Application.Dtos;

namespace Acme.TestCaseManagement.SignOff.Dtos;

public class StartSignOffDto
{
    /// <summary>Keeps a milestone sign-off to the plans of this project. A plan sign-off is in the plan's project.</summary>
    public Guid? ProjectId { get; set; }

    /// <summary>Sign off one test plan. Give either this or <see cref="MilestoneId"/>, not both.</summary>
    public Guid? TestPlanId { get; set; }

    /// <summary>Sign off every test plan of this milestone.</summary>
    public Guid? MilestoneId { get; set; }

    /// <summary>The gate to use. When omitted the default gate is used, else the built-in baseline.</summary>
    public Guid? QualityGateId { get; set; }

    /// <summary>Optional title; defaults to "Sign-off: " followed by the plan names.</summary>
    [StringLength(SignOffConsts.MaxTitleLength)]
    public string? Title { get; set; }

    /// <summary>Role label of the caller, for example "QA Lead".</summary>
    [StringLength(SignOffConsts.MaxRoleLength)]
    public string? ApproverRole { get; set; }

    [StringLength(SignOffConsts.MaxCommentLength)]
    public string? Comment { get; set; }
}

public class ApproveSignOffDto
{
    /// <summary>Role label of the caller, for example "Product Owner".</summary>
    [StringLength(SignOffConsts.MaxRoleLength)]
    public string? ApproverRole { get; set; }

    [StringLength(SignOffConsts.MaxCommentLength)]
    public string? Comment { get; set; }
}

public class GetSignOffListInput : PagedAndSortedResultRequestDto
{
    /// <summary>Only what belongs to this project. Leave it out to see every project.</summary>
    public Guid? ProjectId { get; set; }

    public Guid? TestPlanId { get; set; }

    public Guid? MilestoneId { get; set; }

    public SignOffStatus? Status { get; set; }
}

public class SignOffApprovalDto
{
    public Guid Id { get; set; }

    public Guid ApproverUserId { get; set; }

    public string ApproverName { get; set; } = string.Empty;

    public string? ApproverRole { get; set; }

    public string? Comment { get; set; }

    public DateTime ApprovedTime { get; set; }

    /// <summary>SHA-256 digest of the report, snapshot hash, approver, role, comment and time.</summary>
    public string Signature { get; set; } = string.Empty;
}

public class SignOffReportDto : CreationAuditedEntityDto<Guid>
{
    public Guid ProjectId { get; set; }

    /// <summary>Set for a plan sign-off.</summary>
    public Guid? TestPlanId { get; set; }

    /// <summary>Set for a milestone sign-off.</summary>
    public Guid? MilestoneId { get; set; }

    public string Title { get; set; } = string.Empty;

    /// <summary>Null when the built-in baseline gate was used.</summary>
    public Guid? QualityGateId { get; set; }

    /// <summary>The gate thresholds frozen at sign-off time.</summary>
    public string QualityGateName { get; set; } = string.Empty;

    public decimal MinPassRate { get; set; }

    public int RequiredApprovals { get; set; }

    public SignOffStatus Status { get; set; }

    /// <summary>When the last required approval was given.</summary>
    public DateTime? ApprovedTime { get; set; }

    /// <summary>SHA-256 of <see cref="SummaryStatsJson"/>.</summary>
    public string SnapshotHash { get; set; } = string.Empty;

    /// <summary>True when the stored snapshot and every approval still match their digests.</summary>
    public bool IntegrityVerified { get; set; }

    /// <summary>The frozen evaluation (scope, thresholds, metrics, criteria), parsed from <see cref="SummaryStatsJson"/>.</summary>
    public QualityGateEvaluationDto? Summary { get; set; }

    /// <summary>The frozen snapshot exactly as stored; this is what the hash covers.</summary>
    public string SummaryStatsJson { get; set; } = string.Empty;

    /// <summary>Oldest first.</summary>
    public List<SignOffApprovalDto> Approvals { get; set; } = new();
}
