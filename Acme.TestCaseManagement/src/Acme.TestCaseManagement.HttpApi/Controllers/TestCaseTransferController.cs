using Acme.TestCaseManagement.Transfer;
using Acme.TestCaseManagement.Transfer.Dtos;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Volo.Abp;
using Volo.Abp.Content;

namespace Acme.TestCaseManagement.Controllers;

[RemoteService(Name = "TestCaseManagement")]
[Area("test-case-management")]
[Route("api/test-case-management/test-cases")]
public class TestCaseTransferController : TestCaseManagementController, ITestCaseTransferAppService
{
    private readonly ITestCaseTransferAppService _transferAppService;

    public TestCaseTransferController(ITestCaseTransferAppService transferAppService)
    {
        _transferAppService = transferAppService;
    }

    /// <inheritdoc />
    [HttpGet("export")]
    [ProducesResponseType(typeof(IRemoteStreamContent), StatusCodes.Status200OK, TableContentTypes.Xlsx, TableContentTypes.Csv)]
    public virtual Task<IRemoteStreamContent> ExportAsync([FromQuery] ExportTestCasesInput input)
    {
        return _transferAppService.ExportAsync(input);
    }

    /// <inheritdoc />
    [HttpPost("import")]
    [Consumes("multipart/form-data")]
    public virtual Task<ImportReportDto> ImportAsync([FromForm] ImportTestCasesInput input)
    {
        return _transferAppService.ImportAsync(input);
    }
}
