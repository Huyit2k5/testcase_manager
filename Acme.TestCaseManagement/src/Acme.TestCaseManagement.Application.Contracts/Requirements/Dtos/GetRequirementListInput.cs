using Volo.Abp.Application.Dtos;

namespace Acme.TestCaseManagement.Requirements.Dtos;

public class GetRequirementListInput : PagedAndSortedResultRequestDto
{
    /// <summary>Only what belongs to this project. Leave it out to see every project.</summary>
    public Guid? ProjectId { get; set; }

    /// <summary>Matches code or title (case-insensitive).</summary>
    public string? Filter { get; set; }

    public Guid? MilestoneId { get; set; }
}
