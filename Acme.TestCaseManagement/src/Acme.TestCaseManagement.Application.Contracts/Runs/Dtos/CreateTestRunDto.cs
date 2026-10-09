using System.ComponentModel.DataAnnotations;

namespace Acme.TestCaseManagement.Runs.Dtos;

public class CreateTestRunDto
{
    /// <summary>The project of a run without a plan (leave it out for the default project). A run of a plan is in the plan's project.</summary>
    public Guid? ProjectId { get; set; }

    public Guid? TestPlanId { get; set; }

    [Required]
    [StringLength(TestRunConsts.MaxTitleLength)]
    public string Title { get; set; } = string.Empty;

    /// <summary>For example "Staging", "Production" or "iOS 17".</summary>
    [Required]
    [StringLength(TestRunConsts.MaxEnvironmentLength)]
    public string Environment { get; set; } = string.Empty;

    public Guid? AssignedToUserId { get; set; }

    /// <summary>
    /// Approved library test cases to schedule. Each item is bound to the version that is current at this moment.
    /// Duplicates are ignored.
    /// </summary>
    public List<Guid> TestCaseIds { get; set; } = new();
}
