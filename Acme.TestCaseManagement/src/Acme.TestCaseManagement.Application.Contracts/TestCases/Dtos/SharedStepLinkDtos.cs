using System.ComponentModel.DataAnnotations;

namespace Acme.TestCaseManagement.TestCases.Dtos;

public class InsertSharedStepsDto
{
    public Guid SharedStepGroupId { get; set; }

    /// <summary>One-based place of the first inserted step. Leave it out (or go past the end) to add at the end.</summary>
    [Range(1, 10000)]
    public int? Position { get; set; }

    /// <summary>Recorded on the new version when the test case is approved.</summary>
    [StringLength(TestCaseConsts.MaxChangeSummaryLength)]
    public string? ChangeSummary { get; set; }
}

public class RefreshSharedStepsDto
{
    /// <summary>Recorded on the new version when the test case is approved.</summary>
    [StringLength(TestCaseConsts.MaxChangeSummaryLength)]
    public string? ChangeSummary { get; set; }
}
