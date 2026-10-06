using Acme.TestCaseManagement.Rtm;
using Acme.TestCaseManagement.Rtm.Dtos;
using Microsoft.AspNetCore.Mvc;
using Volo.Abp;

namespace Acme.TestCaseManagement.Controllers;

[RemoteService(Name = "TestCaseManagement")]
[Area("test-case-management")]
[Route("api/test-case-management/rtm")]
public class RtmController : TestCaseManagementController, IRtmAppService
{
    private readonly IRtmAppService _rtmAppService;

    public RtmController(IRtmAppService rtmAppService)
    {
        _rtmAppService = rtmAppService;
    }

    /// <inheritdoc />
    [HttpGet]
    public virtual Task<RtmMatrixDto> GetMatrixAsync([FromQuery] GetRtmInput input)
    {
        return _rtmAppService.GetMatrixAsync(input);
    }
}
