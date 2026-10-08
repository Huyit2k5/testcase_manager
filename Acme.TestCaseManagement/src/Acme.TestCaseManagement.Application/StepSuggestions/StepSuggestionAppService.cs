using System.Globalization;
using Acme.TestCaseManagement.Permissions;
using Microsoft.AspNetCore.Authorization;
using Volo.Abp;
using Volo.Abp.Threading;

namespace Acme.TestCaseManagement.StepSuggestions;

[Authorize(TestCaseManagementPermissions.TestCases.SuggestSteps)]
public class StepSuggestionAppService : TestCaseManagementAppService, IStepSuggestionAppService
{
    private readonly IStepSuggestionProvider _provider;
    private readonly ICancellationTokenProvider _cancellation;

    public StepSuggestionAppService(IStepSuggestionProvider provider, ICancellationTokenProvider cancellation)
    {
        _provider = provider;
        _cancellation = cancellation;
    }

    public virtual Task<StepSuggestionStatusDto> GetStatusAsync()
    {
        return Task.FromResult(new StepSuggestionStatusDto
        {
            Enabled = _provider.IsEnabled,
            MaxRequirementLength = StepSuggestionLimits.MaxRequirementLength,
            MaxSteps = StepSuggestionLimits.MaxSteps,
            DefaultSteps = StepSuggestionLimits.DefaultSteps,
        });
    }

    public virtual async Task<StepSuggestionResultDto> SuggestAsync(SuggestStepsInput input)
    {
        if (!_provider.IsEnabled)
        {
            throw new BusinessException(TestCaseManagementErrorCodes.StepSuggestionNotConfigured);
        }

        var maxSteps = Math.Clamp(input.MaxSteps ?? StepSuggestionLimits.DefaultSteps, 1, StepSuggestionLimits.MaxSteps);
        var request = new StepSuggestionRequest(
            input.RequirementText.Trim(),
            string.IsNullOrWhiteSpace(input.Title) ? null : input.Title.Trim(),
            maxSteps,
            ResolveLanguage(input.Language));

        var proposed = await _provider.SuggestAsync(request, _cancellation.Token);

        // Whatever the provider returned is cleaned here, so that a host's own provider gets the same protection.
        var steps = StepSuggestionResponseParser.Sanitize(proposed, maxSteps);
        if (steps.Count == 0)
        {
            throw new BusinessException(TestCaseManagementErrorCodes.StepSuggestionNoUsableSteps);
        }

        return new StepSuggestionResultDto { Steps = steps };
    }

    private static string ResolveLanguage(string? requested)
    {
        var language = string.IsNullOrWhiteSpace(requested) ? CultureInfo.CurrentUICulture.TwoLetterISOLanguageName : requested.Trim();
        return language.Length is >= 2 and <= 16 && language.All(c => char.IsLetter(c) || c == '-') ? language : "en";
    }
}
