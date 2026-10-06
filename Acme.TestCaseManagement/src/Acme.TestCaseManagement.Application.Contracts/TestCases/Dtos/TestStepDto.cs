using System.ComponentModel.DataAnnotations;

namespace Acme.TestCaseManagement.TestCases.Dtos;

public class TestStepDto
{
    /// <summary>Null when adding a new step; set to keep the identity of an existing step.</summary>
    public Guid? Id { get; set; }

    /// <summary>One-based position. Ignored on input: the order of the list is authoritative.</summary>
    public int StepOrder { get; set; }

    [Required]
    [StringLength(TestStepConsts.MaxTextLength)]
    public string Action { get; set; } = string.Empty;

    [Required]
    [StringLength(TestStepConsts.MaxTextLength)]
    public string ExpectedResult { get; set; } = string.Empty;

    [StringLength(TestStepConsts.MaxTextLength)]
    public string? TestData { get; set; }
}
