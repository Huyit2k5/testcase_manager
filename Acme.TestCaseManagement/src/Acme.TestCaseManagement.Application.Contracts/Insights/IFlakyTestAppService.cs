using Acme.TestCaseManagement.Insights.Dtos;
using Volo.Abp.Application.Services;

namespace Acme.TestCaseManagement.Insights;

/// <summary>Finds the tests whose results flip between Passed and Failed (FR-012).</summary>
public interface IFlakyTestAppService : IApplicationService
{
    /// <summary>
    /// Scores every test case that has attempts in the lookback window, from the changes between its latest Passed and Failed
    /// outcomes, most unstable first. The answer also holds the settings that the scores were made with.
    /// </summary>
    Task<FlakyTestListDto> GetListAsync(GetFlakyTestsInput input);

    /// <summary>
    /// Writes the findings into the library: test cases that score Flaky get the Flaky flag, and with
    /// <c>ClearRecovered</c> the flagged ones that now score Stable lose it. Needs permission to update test cases.
    /// </summary>
    Task<ApplyFlakyFlagsResultDto> ApplyAsync(ApplyFlakyFlagsInput input);
}
