using Acme.TestCaseManagement.Automation;
using Acme.TestCaseManagement.Automation.Dtos;
using Microsoft.AspNetCore.Mvc;
using Volo.Abp;

namespace Acme.TestCaseManagement.Controllers;

[RemoteService(Name = "TestCaseManagement")]
[Area("test-case-management")]
[Route("api/test-case-management/automation/results")]
public class AutomationResultsController : TestCaseManagementController, IAutomationResultsAppService
{
    private readonly IAutomationResultsAppService _automationResultsAppService;

    /// <summary>The header a pipeline sends the idempotency key in.</summary>
    public const string IdempotencyKeyHeader = "Idempotency-Key";

    public AutomationResultsController(IAutomationResultsAppService automationResultsAppService)
    {
        _automationResultsAppService = automationResultsAppService;
    }

    /// <inheritdoc />
    [HttpPost]
    public virtual Task<PublishAutomationResultsDto> PublishAsync(PublishAutomationResultsInput input)
    {
        // The documented way to send the key is the Idempotency-Key header (a pipeline sets it next to X-Api-Key); a key in the body
        // is read as well, and wins when both are there.
        if (string.IsNullOrWhiteSpace(input.IdempotencyKey) && Request.Headers.TryGetValue(IdempotencyKeyHeader, out var header))
        {
            input.IdempotencyKey = header.ToString();
        }

        return _automationResultsAppService.PublishAsync(input);
    }
}
