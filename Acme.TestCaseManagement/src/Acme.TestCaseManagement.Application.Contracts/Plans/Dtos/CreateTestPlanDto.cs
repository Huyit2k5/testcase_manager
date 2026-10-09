using System.ComponentModel.DataAnnotations;

namespace Acme.TestCaseManagement.Plans.Dtos;

public class CreateTestPlanDto
{
    /// <summary>The project it is created in. Leave it out for the default project.</summary>
    public Guid? ProjectId { get; set; }

    [Required]
    [StringLength(TestPlanConsts.MaxNameLength)]
    public string Name { get; set; } = string.Empty;

    [StringLength(TestPlanConsts.MaxDescriptionLength)]
    public string? Description { get; set; }

    public Guid? MilestoneId { get; set; }

    public DateTime? StartDate { get; set; }

    /// <summary>Must not be before <see cref="StartDate"/>.</summary>
    public DateTime? EndDate { get; set; }
}
