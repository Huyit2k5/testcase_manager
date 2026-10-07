using Acme.TestCaseManagement.Automation.Dtos;
using Volo.Abp.Application.Services;

namespace Acme.TestCaseManagement.Automation;

/// <summary>The endpoint that CI/CD pipelines publish their test results to.</summary>
public interface IAutomationResultsAppService : IApplicationService
{
    /// <summary>
    /// Records the results of an automated test run. Each result is matched to a test case by its AutomationId and recorded
    /// as a new attempt, bound to the version of the test case that is current when it joins the run. Earlier attempts are
    /// never changed. Results without a matching test case are reported, not fatal, unless FailOnUnmatched is set. Send an
    /// idempotency key so that a retry does not record anything twice. Callable with an API key (header X-Api-Key) or by a
    /// signed-in user with the permission.
    /// </summary>
    Task<PublishAutomationResultsDto> PublishAsync(PublishAutomationResultsInput input);
}
