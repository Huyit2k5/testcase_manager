using System.ComponentModel.DataAnnotations;

namespace Acme.TestCaseManagement.Suites.Dtos;

public class CreateTestSuiteDto
{
    [Required]
    [StringLength(TestSuiteConsts.MaxNameLength)]
    public string Name { get; set; } = string.Empty;

    [StringLength(TestSuiteConsts.MaxDescriptionLength)]
    public string? Description { get; set; }

    /// <summary>Null creates a root suite.</summary>
    public Guid? ParentId { get; set; }
}
