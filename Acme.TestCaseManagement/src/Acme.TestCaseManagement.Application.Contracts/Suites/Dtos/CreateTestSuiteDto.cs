using System.ComponentModel.DataAnnotations;

namespace Acme.TestCaseManagement.Suites.Dtos;

public class CreateTestSuiteDto
{
    /// <summary>The project of a root suite (leave it out for the default project). A suite below another one is always in its parent's project.</summary>
    public Guid? ProjectId { get; set; }

    [Required]
    [StringLength(TestSuiteConsts.MaxNameLength)]
    public string Name { get; set; } = string.Empty;

    [StringLength(TestSuiteConsts.MaxDescriptionLength)]
    public string? Description { get; set; }

    /// <summary>Null creates a root suite.</summary>
    public Guid? ParentId { get; set; }
}
