using System.ComponentModel.DataAnnotations;
using Acme.TestCaseManagement.Enums;

namespace Acme.TestCaseManagement.TestCases.Dtos;

public class ChangeTestCaseStatusDto
{
    public TestCaseStatus TargetStatus { get; set; }

    [StringLength(TestCaseConsts.MaxChangeSummaryLength)]
    public string? ChangeSummary { get; set; }
}
