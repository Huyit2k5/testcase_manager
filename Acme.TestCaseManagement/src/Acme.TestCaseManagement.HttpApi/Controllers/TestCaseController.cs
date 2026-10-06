using Acme.TestCaseManagement.TestCases;
using Acme.TestCaseManagement.TestCases.Dtos;
using Microsoft.AspNetCore.Mvc;
using Volo.Abp;
using Volo.Abp.Application.Dtos;

namespace Acme.TestCaseManagement.Controllers;

[RemoteService(Name = "TestCaseManagement")]
[Area("test-case-management")]
[Route("api/test-case-management/test-cases")]
public class TestCaseController : TestCaseManagementController, ITestCaseAppService
{
    private readonly ITestCaseAppService _testCaseAppService;

    public TestCaseController(ITestCaseAppService testCaseAppService)
    {
        _testCaseAppService = testCaseAppService;
    }

    /// <inheritdoc />
    [HttpGet("{id:guid}")]
    public virtual Task<TestCaseDto> GetAsync(Guid id)
    {
        return _testCaseAppService.GetAsync(id);
    }

    /// <inheritdoc />
    [HttpGet]
    public virtual Task<PagedResultDto<TestCaseDto>> GetListAsync([FromQuery] GetTestCaseListInput input)
    {
        return _testCaseAppService.GetListAsync(input);
    }

    /// <inheritdoc />
    [HttpPost]
    public virtual Task<TestCaseDto> CreateAsync(CreateUpdateTestCaseDto input)
    {
        return _testCaseAppService.CreateAsync(input);
    }

    /// <inheritdoc />
    [HttpPut("{id:guid}")]
    public virtual Task<TestCaseDto> UpdateAsync(Guid id, CreateUpdateTestCaseDto input)
    {
        return _testCaseAppService.UpdateAsync(id, input);
    }

    /// <inheritdoc />
    [HttpDelete("{id:guid}")]
    public virtual Task DeleteAsync(Guid id)
    {
        return _testCaseAppService.DeleteAsync(id);
    }

    /// <inheritdoc />
    [HttpPut("{id:guid}/steps/order")]
    public virtual Task<TestCaseDto> ReorderStepsAsync(Guid id, ReorderTestStepsDto input)
    {
        return _testCaseAppService.ReorderStepsAsync(id, input);
    }

    /// <inheritdoc />
    [HttpPost("{id:guid}/status")]
    public virtual Task<TestCaseDto> ChangeStatusAsync(Guid id, ChangeTestCaseStatusDto input)
    {
        return _testCaseAppService.ChangeStatusAsync(id, input);
    }

    /// <inheritdoc />
    [HttpGet("{id:guid}/versions")]
    public virtual Task<List<TestCaseVersionDto>> GetVersionsAsync(Guid id)
    {
        return _testCaseAppService.GetVersionsAsync(id);
    }

    /// <inheritdoc />
    [HttpGet("{id:guid}/defects")]
    public virtual Task<List<TestCaseDefectDto>> GetDefectLinksAsync(Guid id)
    {
        return _testCaseAppService.GetDefectLinksAsync(id);
    }

    /// <inheritdoc />
    [HttpGet("{id:guid}/versions/{versionNumber:int}")]
    public virtual Task<TestCaseVersionDto> GetVersionAsync(Guid id, int versionNumber)
    {
        return _testCaseAppService.GetVersionAsync(id, versionNumber);
    }
}
