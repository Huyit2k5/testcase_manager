using Acme.TestCaseManagement.Enums;
using Acme.TestCaseManagement.SignOff;
using Acme.TestCaseManagement.SignOff.Dtos;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Volo.Abp;
using Volo.Abp.Application.Dtos;
using Volo.Abp.Content;

namespace Acme.TestCaseManagement.Controllers;

[RemoteService(Name = "TestCaseManagement")]
[Area("test-case-management")]
[Route("api/test-case-management/sign-off")]
public class SignOffController : TestCaseManagementController, ISignOffAppService
{
    private readonly ISignOffAppService _signOffAppService;

    public SignOffController(ISignOffAppService signOffAppService)
    {
        _signOffAppService = signOffAppService;
    }

    /// <inheritdoc />
    [HttpGet("{id:guid}")]
    public virtual Task<SignOffReportDto> GetAsync(Guid id)
    {
        return _signOffAppService.GetAsync(id);
    }

    /// <inheritdoc />
    [HttpGet]
    public virtual Task<PagedResultDto<SignOffReportDto>> GetListAsync([FromQuery] GetSignOffListInput input)
    {
        return _signOffAppService.GetListAsync(input);
    }

    /// <inheritdoc />
    [HttpPost]
    public virtual Task<SignOffReportDto> SignOffAsync(StartSignOffDto input)
    {
        return _signOffAppService.SignOffAsync(input);
    }

    /// <inheritdoc />
    [HttpPost("{id:guid}/approvals")]
    public virtual Task<SignOffReportDto> ApproveAsync(Guid id, ApproveSignOffDto input)
    {
        return _signOffAppService.ApproveAsync(id, input);
    }

    /// <inheritdoc />
    [HttpGet("{id:guid}/export")]
    [ProducesResponseType(typeof(IRemoteStreamContent), StatusCodes.Status200OK, TableContentTypes.Pdf, TableContentTypes.Xlsx)]
    public virtual Task<IRemoteStreamContent> ExportAsync(Guid id, [FromQuery] ReportFormat format = ReportFormat.Pdf)
    {
        return _signOffAppService.ExportAsync(id, format);
    }
}
