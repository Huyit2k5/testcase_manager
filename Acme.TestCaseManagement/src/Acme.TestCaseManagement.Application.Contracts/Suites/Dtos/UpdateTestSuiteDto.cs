using System.ComponentModel.DataAnnotations;

namespace Acme.TestCaseManagement.Suites.Dtos;

public class UpdateTestSuiteDto
{
    [Required]
    [StringLength(TestSuiteConsts.MaxNameLength)]
    public string Name { get; set; } = string.Empty;

    [StringLength(TestSuiteConsts.MaxDescriptionLength)]
    public string? Description { get; set; }
}
