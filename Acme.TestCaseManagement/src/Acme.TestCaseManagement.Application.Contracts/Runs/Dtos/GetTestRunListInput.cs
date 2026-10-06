using Acme.TestCaseManagement.Enums;
using Volo.Abp.Application.Dtos;

namespace Acme.TestCaseManagement.Runs.Dtos;

public class GetTestRunListInput : PagedAndSortedResultRequestDto
{
    /// <summary>Matches the run title (case-insensitive).</summary>
    public string? Filter { get; set; }

    public Guid? TestPlanId { get; set; }

    public RunStatus? Status { get; set; }

    public string? Environment { get; set; }
}
