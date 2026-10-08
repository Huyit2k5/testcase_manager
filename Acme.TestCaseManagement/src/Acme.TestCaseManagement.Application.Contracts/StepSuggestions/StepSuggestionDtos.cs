using System.ComponentModel.DataAnnotations;
using Volo.Abp.Auditing;

namespace Acme.TestCaseManagement.StepSuggestions;

public static class StepSuggestionLimits
{
    /// <summary>The longest requirement text that is sent to the model.</summary>
    public const int MaxRequirementLength = 4000;

    public const int MinRequirementLength = 10;

    public const int MaxTitleLength = 256;

    /// <summary>The most steps asked for, and kept from the answer, in one request.</summary>
    public const int MaxSteps = 20;

    public const int DefaultSteps = 8;
}

/// <summary>Whether suggestions are available in this application, and what limits apply to a request.</summary>
public class StepSuggestionStatusDto
{
    /// <summary>False when no AI model is configured: the screen then shows no suggestion button.</summary>
    public bool Enabled { get; set; }

    public int MaxRequirementLength { get; set; }

    public int MaxSteps { get; set; }

    public int DefaultSteps { get; set; }
}

public class SuggestStepsInput : IValidatableObject
{
    /// <summary>
    /// The requirement, user story or acceptance criteria the steps are written from. Left out of the audit log of the call: the log
    /// says who asked and when, and does not keep what may be confidential text.
    /// </summary>
    [DisableAuditing]
    [Required]
    [StringLength(StepSuggestionLimits.MaxRequirementLength, MinimumLength = StepSuggestionLimits.MinRequirementLength)]
    public string RequirementText { get; set; } = string.Empty;

    /// <summary>The title of the test case, when there is one: it tells the model what is being tested. Not kept in the audit log either.</summary>
    [DisableAuditing]
    [StringLength(StepSuggestionLimits.MaxTitleLength)]
    public string? Title { get; set; }

    /// <summary>How many steps to ask for; the default is 8 and the most is 20.</summary>
    [Range(1, StepSuggestionLimits.MaxSteps)]
    public int? MaxSteps { get; set; }

    /// <summary>The language the steps are written in ("en" or "vi"); the language of the caller when omitted.</summary>
    [StringLength(16)]
    public string? Language { get; set; }

    /// <summary>The minimum length counts the text without the spaces around it, which is what is sent to the model.</summary>
    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (RequirementText.Trim().Length < StepSuggestionLimits.MinRequirementLength)
        {
            yield return new ValidationResult(
                $"The requirement must have at least {StepSuggestionLimits.MinRequirementLength} characters, not counting the spaces around it.",
                new[] { nameof(RequirementText) });
        }
    }
}

/// <summary>A step the model proposed. It is a proposal only: nothing is saved until a person adds it to a test case.</summary>
public class SuggestedStepDto
{
    public string Action { get; set; } = string.Empty;

    public string ExpectedResult { get; set; } = string.Empty;

    public string? TestData { get; set; }
}

public class StepSuggestionResultDto
{
    public List<SuggestedStepDto> Steps { get; set; } = new();
}
