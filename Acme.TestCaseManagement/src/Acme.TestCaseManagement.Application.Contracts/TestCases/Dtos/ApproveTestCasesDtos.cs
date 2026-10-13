using System.ComponentModel.DataAnnotations;

namespace Acme.TestCaseManagement.TestCases.Dtos;

/// <summary>The test cases to approve together.</summary>
public class ApproveTestCasesInput
{
    /// <summary>The test cases, at most <see cref="TestCaseConsts.MaxBulkApprove"/>. Each one is approved on its own: it gets its own version.</summary>
    [Required]
    [MinLength(1)]
    [MaxLength(TestCaseConsts.MaxBulkApprove)]
    public List<Guid> TestCaseIds { get; set; } = new();

    /// <summary>The change summary written on every version that is published.</summary>
    [StringLength(TestCaseConsts.MaxChangeSummaryLength)]
    public string? ChangeSummary { get; set; }
}

/// <summary>What happened to each test case: the ones that were approved, and why the others were not.</summary>
public class ApproveTestCasesResultDto
{
    public int Approved { get; set; }

    /// <summary>The codes of the test cases that were approved.</summary>
    public List<string> ApprovedCodes { get; set; } = new();

    /// <summary>The test cases that were left as they were, each with the reason.</summary>
    public List<ApproveTestCaseFailureDto> Failed { get; set; } = new();
}

public class ApproveTestCaseFailureDto
{
    public Guid TestCaseId { get; set; }

    /// <summary>The code of the test case; empty when no test case has that id.</summary>
    public string Code { get; set; } = string.Empty;

    /// <summary>The error code, for example <c>TestCaseManagement:TestCaseHasNoSteps</c>.</summary>
    public string ErrorCode { get; set; } = string.Empty;

    /// <summary>The reason, in the language of the caller.</summary>
    public string Message { get; set; } = string.Empty;
}
