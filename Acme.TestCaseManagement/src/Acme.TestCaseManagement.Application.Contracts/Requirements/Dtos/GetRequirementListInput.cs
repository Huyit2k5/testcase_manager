using Volo.Abp.Application.Dtos;

namespace Acme.TestCaseManagement.Requirements.Dtos;

public class GetRequirementListInput : PagedAndSortedResultRequestDto
{
    /// <summary>Matches code or title (case-insensitive).</summary>
    public string? Filter { get; set; }

    public Guid? MilestoneId { get; set; }
}
