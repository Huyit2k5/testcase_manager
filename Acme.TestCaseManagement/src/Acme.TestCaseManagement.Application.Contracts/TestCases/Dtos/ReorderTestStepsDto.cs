using System.ComponentModel.DataAnnotations;

namespace Acme.TestCaseManagement.TestCases.Dtos;

public class ReorderTestStepsDto
{
    /// <summary>Every step Id of the test case, in the desired order.</summary>
    [Required]
    public List<Guid> StepIds { get; set; } = new();

    [StringLength(TestCaseConsts.MaxChangeSummaryLength)]
    public string? ChangeSummary { get; set; }
}
