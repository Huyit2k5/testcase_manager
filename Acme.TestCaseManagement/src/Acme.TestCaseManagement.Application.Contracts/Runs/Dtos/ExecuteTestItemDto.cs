using System.ComponentModel.DataAnnotations;
using Acme.TestCaseManagement.Enums;

namespace Acme.TestCaseManagement.Runs.Dtos;

public class ExecuteTestItemDto
{
    /// <summary>Passed, Failed, Blocked or Skipped. Untested is not a valid result.</summary>
    public TestResultStatus Status { get; set; }

    [StringLength(TestExecutionConsts.MaxActualResultLength)]
    public string? ActualResult { get; set; }

    [Range(0, int.MaxValue)]
    public int DurationSeconds { get; set; }

    /// <summary>Optional defects to link to this attempt. Only allowed when <see cref="Status"/> is Failed.</summary>
    public List<AddDefectLinkDto> Defects { get; set; } = new();
}

public class BatchExecuteTestItemDto : ExecuteTestItemDto
{
    public Guid TestRunItemId { get; set; }
}

public class BatchExecuteTestItemsDto
{
    /// <summary>
    /// Processed in order inside one transaction. An item listed twice gets consecutive attempts.
    /// If any entry is invalid, none are recorded.
    /// </summary>
    [Required]
    [MinLength(1)]
    public List<BatchExecuteTestItemDto> Items { get; set; } = new();
}
