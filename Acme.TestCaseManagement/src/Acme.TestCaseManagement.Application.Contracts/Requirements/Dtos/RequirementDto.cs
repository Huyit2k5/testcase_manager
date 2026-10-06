using Acme.TestCaseManagement.Enums;
using Volo.Abp.Application.Dtos;

namespace Acme.TestCaseManagement.Requirements.Dtos;

public class RequirementDto : AuditedEntityDto<Guid>
{
    public string Code { get; set; } = string.Empty;

    public string Title { get; set; } = string.Empty;

    public string? Description { get; set; }

    public string? AcceptanceCriteria { get; set; }

    public PriorityLevel Priority { get; set; }

    public Guid? MilestoneId { get; set; }
}
