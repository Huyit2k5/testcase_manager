using Acme.TestCaseManagement.Enums;
using Volo.Abp.Application.Dtos;

namespace Acme.TestCaseManagement.Plans.Dtos;

public class GetTestPlanListInput : PagedAndSortedResultRequestDto
{
    /// <summary>Matches the plan name (case-insensitive).</summary>
    public string? Filter { get; set; }

    public PlanStatus? Status { get; set; }

    public Guid? MilestoneId { get; set; }
}
