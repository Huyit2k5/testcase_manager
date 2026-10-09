using Acme.TestCaseManagement.Enums;
using Volo.Abp.Application.Dtos;

namespace Acme.TestCaseManagement.Runs.Dtos;

public class GetTestRunListInput : PagedAndSortedResultRequestDto
{
    /// <summary>Only what belongs to this project. Leave it out to see every project.</summary>
    public Guid? ProjectId { get; set; }

    /// <summary>Matches the run title (case-insensitive).</summary>
    public string? Filter { get; set; }

    public Guid? TestPlanId { get; set; }

    public RunStatus? Status { get; set; }

    public string? Environment { get; set; }
}
