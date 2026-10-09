using Acme.TestCaseManagement.Enums;
using Volo.Abp.Application.Dtos;

namespace Acme.TestCaseManagement.Plans.Dtos;

public class GetTestPlanListInput : PagedAndSortedResultRequestDto
{
    /// <summary>Only what belongs to this project. Leave it out to see every project.</summary>
    public Guid? ProjectId { get; set; }

    /// <summary>Matches the plan name (case-insensitive).</summary>
    public string? Filter { get; set; }

    public PlanStatus? Status { get; set; }

    public Guid? MilestoneId { get; set; }
}
