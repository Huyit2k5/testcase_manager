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

    public AutomationResultsController(IAutomationResultsAppService automationResultsAppService)
    {
        _automationResultsAppService = automationResultsAppService;
    }

    /// <inheritdoc />
    [HttpPost]
    public virtual Task<PublishAutomationResultsDto> PublishAsync(PublishAutomationResultsInput input)
    {
        return _automationResultsAppService.PublishAsync(input);
    }
}
