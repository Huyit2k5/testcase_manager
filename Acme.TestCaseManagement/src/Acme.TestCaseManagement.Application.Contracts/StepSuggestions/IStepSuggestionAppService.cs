using Volo.Abp.Application.Services;

namespace Acme.TestCaseManagement.StepSuggestions;

/// <summary>
/// AI assistance for writing test steps (FR-027). A person gives a requirement text and gets proposed steps back, which they
/// review and add to a test case themselves: the service saves nothing. It works only when the host has configured an AI model
/// (see <c>TestCaseManagementAiOptions</c>) or has provided its own <see cref="IStepSuggestionProvider"/>.
/// </summary>
public interface IStepSuggestionAppService : IApplicationService
{
    /// <summary>Whether a model is configured. The screen shows the suggestion button only when it is.</summary>
    Task<StepSuggestionStatusDto> GetStatusAsync();

    /// <summary>
    /// Sends the requirement text (and the title) to the configured model and returns the steps it proposes, cleaned: empty
    /// steps are dropped and lengths and counts are capped. The text leaves the application, so this has its own permission.
    /// </summary>
    Task<StepSuggestionResultDto> SuggestAsync(SuggestStepsInput input);
}
