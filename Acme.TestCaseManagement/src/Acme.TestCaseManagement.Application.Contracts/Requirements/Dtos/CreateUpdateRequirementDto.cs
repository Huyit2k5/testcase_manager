using System.ComponentModel.DataAnnotations;
using Acme.TestCaseManagement.Enums;

namespace Acme.TestCaseManagement.Requirements.Dtos;

public class CreateUpdateRequirementDto
{
    /// <summary>Unique within the tenant (ignoring case), e.g. "REQ-AUTH-01".</summary>
    [Required]
    [StringLength(RequirementConsts.MaxCodeLength)]
    public string Code { get; set; } = string.Empty;

    [Required]
    [StringLength(RequirementConsts.MaxTitleLength)]
    public string Title { get; set; } = string.Empty;

    [StringLength(RequirementConsts.MaxTextLength)]
    public string? Description { get; set; }

    [StringLength(RequirementConsts.MaxTextLength)]
    public string? AcceptanceCriteria { get; set; }

    public PriorityLevel Priority { get; set; } = PriorityLevel.Medium;

    /// <summary>Optional milestone of the host application; lets the RTM be limited to one milestone.</summary>
    public Guid? MilestoneId { get; set; }
}

public class LinkTestCasesDto
{
    [Required]
    public List<Guid> TestCaseIds { get; set; } = new();
}
