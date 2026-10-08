using Acme.TestCaseManagement.StepSuggestions;
using Microsoft.AspNetCore.Mvc;
using Volo.Abp;

namespace Acme.TestCaseManagement.Controllers;

[RemoteService(Name = "TestCaseManagement")]
[Area("test-case-management")]
[Route("api/test-case-management/step-suggestions")]
public class StepSuggestionController : TestCaseManagementController, IStepSuggestionAppService
{
    private readonly IStepSuggestionAppService _stepSuggestionAppService;

    public StepSuggestionController(IStepSuggestionAppService stepSuggestionAppService)
    {
        _stepSuggestionAppService = stepSuggestionAppService;
    }

    /// <inheritdoc />
    [HttpGet("status")]
    public virtual Task<StepSuggestionStatusDto> GetStatusAsync()
    {
        return _stepSuggestionAppService.GetStatusAsync();
    }

    /// <inheritdoc />
    [HttpPost]
    public virtual Task<StepSuggestionResultDto> SuggestAsync(SuggestStepsInput input)
    {
        return _stepSuggestionAppService.SuggestAsync(input);
    }
}
