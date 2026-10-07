using System.ComponentModel.DataAnnotations;
using Acme.TestCaseManagement.Enums;
using Acme.TestCaseManagement.Runs.Dtos;

namespace Acme.TestCaseManagement.Automation.Dtos;

/// <summary>A batch of results from an automated test run, to be recorded in a test run.</summary>
public class PublishAutomationResultsInput
{
    /// <summary>The run to record into. Give this or <see cref="Run"/>, not both.</summary>
    public Guid? RunId { get; set; }

    /// <summary>
    /// A new run to create for these results (for example one per pipeline run). The answer returns its id, which the
    /// other shards of the same pipeline use as <see cref="RunId"/>.
    /// </summary>
    public AutomationRunInput? Run { get; set; }

    /// <summary>
    /// Makes a retry safe. If a request with the same key was already processed, the same answer is returned and nothing is
    /// recorded again; if the key was used for a different request, the request is refused. Use something unique per
    /// pipeline run and shard, such as the build id.
    /// </summary>
    [StringLength(AutomationConsts.MaxIdempotencyKeyLength)]
    public string? IdempotencyKey { get; set; }

    /// <summary>The results. Several results with the same AutomationId are the retries of one test, recorded in order.</summary>
    [Required]
    [MinLength(1)]
    public List<AutomationResultInput> Results { get; set; } = new();

    /// <summary>
    /// A test case that is Approved but not yet in the run is added to it, bound to its current version. When false such a
    /// result gets the outcome NotInRun. Default true.
    /// </summary>
    public bool AddMissingToRun { get; set; } = true;

    /// <summary>
    /// When true, nothing is recorded (and no run is created) unless every result finds its test case. The answer then lists
    /// what did not match. Default false: results of tests that the library does not track are normal in CI and are
    /// reported but do not stop the others.
    /// </summary>
    public bool FailOnUnmatched { get; set; }

    /// <summary>Completes the run after recording, which closes it to further results.</summary>
    public bool CompleteRun { get; set; }
}

public class AutomationRunInput
{
    [Required]
    [StringLength(AutomationConsts.MaxRunTitleLength)]
    public string Title { get; set; } = string.Empty;

    /// <summary>For example "Staging", "Production" or "iOS 17".</summary>
    [Required]
    [StringLength(TestRunConsts.MaxEnvironmentLength)]
    public string Environment { get; set; } = string.Empty;

    public Guid? TestPlanId { get; set; }
}

public class AutomationResultInput
{
    /// <summary>The Automation ID of the test case: the key that links it to its automated script. Matching ignores case.</summary>
    [Required]
    [StringLength(TestCaseConsts.MaxAutomationIdLength)]
    public string AutomationId { get; set; } = string.Empty;

    /// <summary>Passed, Failed, Blocked or Skipped. Untested is not a result.</summary>
    public TestResultStatus Status { get; set; }

    [StringLength(TestExecutionConsts.MaxActualResultLength)]
    public string? ActualResult { get; set; }

    [Range(0, int.MaxValue)]
    public int DurationSeconds { get; set; }

    /// <summary>
    /// The position of this result among the retries of the same test (1 = first run). When given, the retries of one
    /// AutomationId are recorded in this order; the attempt number that is stored is allocated by the module.
    /// </summary>
    [Range(1, 1000)]
    public int? AttemptNumber { get; set; }

    /// <summary>
    /// The runner saw the test pass and fail intermittently. The test case is then flagged flaky (the flag is never cleared
    /// by publishing). A test that fails and then passes within the same request is flagged as well.
    /// </summary>
    public bool IsFlaky { get; set; }

    /// <summary>Issues to link to this result. Only a Failed result can have defects.</summary>
    public List<AddDefectLinkDto> Defects { get; set; } = new();
}

public class PublishAutomationResultsDto
{
    /// <summary>False when <see cref="PublishAutomationResultsInput.FailOnUnmatched"/> stopped the request: nothing was recorded.</summary>
    public bool Accepted { get; set; }

    /// <summary>True when this is the stored answer of an earlier request with the same idempotency key.</summary>
    public bool Replayed { get; set; }

    /// <summary>The run the results went into. Null when nothing was recorded and no run existed.</summary>
    public Guid? RunId { get; set; }

    public bool RunCreated { get; set; }

    public RunStatus? RunStatus { get; set; }

    public int Received { get; set; }

    public int Recorded { get; set; }

    public int Unmatched { get; set; }

    public int Ambiguous { get; set; }

    public int NotApproved { get; set; }

    public int NotInRun { get; set; }

    /// <summary>Test cases that were added to the run by this request.</summary>
    public int Scheduled { get; set; }

    /// <summary>Codes of the test cases that this request flagged as flaky.</summary>
    public List<string> FlaggedFlaky { get; set; } = new();

    /// <summary>One entry per result, in the order they were sent.</summary>
    public List<AutomationResultOutcomeDto> Results { get; set; } = new();
}

public class AutomationResultOutcomeDto
{
    /// <summary>The position of the result in the request, from 0.</summary>
    public int Index { get; set; }

    public string AutomationId { get; set; } = string.Empty;

    public AutomationOutcome Outcome { get; set; }

    /// <summary>The code of the matching test case, when there is exactly one.</summary>
    public string? TestCaseCode { get; set; }

    /// <summary>The attempt number that was stored, for a recorded result.</summary>
    public int? AttemptNumber { get; set; }

    /// <summary>Why the result was not recorded, in the language of the caller.</summary>
    public string? Message { get; set; }
}
