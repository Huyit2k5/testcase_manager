using System.ComponentModel.DataAnnotations;
using Acme.TestCaseManagement.Enums;

namespace Acme.TestCaseManagement.Runs.Dtos;

/// <summary>The result of one step, as it is sent when an attempt is recorded.</summary>
public class StepResultInputDto
{
    /// <summary>The id of a step of the version the item executes (the <c>id</c> of a step in the version).</summary>
    public Guid StepId { get; set; }

    /// <summary>Passed, Failed, Blocked or Skipped. Untested is not a result: leave the step out instead.</summary>
    public TestResultStatus Status { get; set; }

    [StringLength(TestExecutionConsts.MaxActualResultLength)]
    public string? ActualResult { get; set; }
}

/// <summary>The result of one step of an attempt, as it was recorded.</summary>
public class TestStepResultDto
{
    public Guid StepId { get; set; }

    public int StepOrder { get; set; }

    public TestResultStatus Status { get; set; }

    public string? ActualResult { get; set; }
}
