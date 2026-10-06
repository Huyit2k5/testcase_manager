using Acme.TestCaseManagement.Enums;
using Acme.TestCaseManagement.Transfer;
using Acme.TestCaseManagement.Transfer.Dtos;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Volo.Abp;
using Volo.Abp.Content;

namespace Acme.TestCaseManagement.Controllers;

[RemoteService(Name = "TestCaseManagement")]
[Area("test-case-management")]
[Route("api/test-case-management/runs/{runId:guid}/results")]
public class TestResultTransferController : TestCaseManagementController, ITestResultTransferAppService
{
    private readonly ITestResultTransferAppService _transferAppService;

    public TestResultTransferController(ITestResultTransferAppService transferAppService)
    {
        _transferAppService = transferAppService;
    }

    /// <inheritdoc />
    [HttpGet("export")]
    [ProducesResponseType(typeof(IRemoteStreamContent), StatusCodes.Status200OK, TableContentTypes.Xlsx, TableContentTypes.Csv)]
    public virtual Task<IRemoteStreamContent> ExportAsync(Guid runId, [FromQuery] TransferFormat format = TransferFormat.Xlsx)
    {
        return _transferAppService.ExportAsync(runId, format);
    }

    /// <inheritdoc />
    [HttpPost("import")]
    [Consumes("multipart/form-data")]
    public virtual Task<ImportReportDto> ImportAsync(Guid runId, [FromForm] ImportTestResultsInput input)
    {
        return _transferAppService.ImportAsync(runId, input);
    }
}
