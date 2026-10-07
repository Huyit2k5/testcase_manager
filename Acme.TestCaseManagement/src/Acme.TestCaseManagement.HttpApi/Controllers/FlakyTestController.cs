using Acme.TestCaseManagement.Insights;
using Acme.TestCaseManagement.Insights.Dtos;
using Microsoft.AspNetCore.Mvc;
using Volo.Abp;

namespace Acme.TestCaseManagement.Controllers;

[RemoteService(Name = "TestCaseManagement")]
[Area("test-case-management")]
[Route("api/test-case-management/flaky-tests")]
public class FlakyTestController : TestCaseManagementController, IFlakyTestAppService
{
    private readonly IFlakyTestAppService _flakyTestAppService;

    public FlakyTestController(IFlakyTestAppService flakyTestAppService)
    {
        _flakyTestAppService = flakyTestAppService;
    }

    /// <inheritdoc />
    [HttpGet]
    public virtual Task<FlakyTestListDto> GetListAsync([FromQuery] GetFlakyTestsInput input)
    {
        return _flakyTestAppService.GetListAsync(input);
    }

    /// <inheritdoc />
    [HttpPost("apply")]
    public virtual Task<ApplyFlakyFlagsResultDto> ApplyAsync(ApplyFlakyFlagsInput input)
    {
        return _flakyTestAppService.ApplyAsync(input);
    }
}
