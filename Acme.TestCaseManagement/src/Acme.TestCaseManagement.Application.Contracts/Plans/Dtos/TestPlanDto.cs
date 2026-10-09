using Acme.TestCaseManagement.Enums;
using Volo.Abp.Application.Dtos;

namespace Acme.TestCaseManagement.Plans.Dtos;

public class TestPlanDto : AuditedEntityDto<Guid>
{
    public Guid ProjectId { get; set; }

    public string Name { get; set; } = string.Empty;

    public string? Description { get; set; }

    public Guid? MilestoneId { get; set; }

    public DateTime? StartDate { get; set; }

    public DateTime? EndDate { get; set; }

    public PlanStatus Status { get; set; }

    /// <summary>How many runs the plan has (all of them, whatever their status).</summary>
    public int RunCount { get; set; }
}
