using Acme.TestCaseManagement.Runs;
using Acme.TestCaseManagement.Runs.Dtos;
using Microsoft.AspNetCore.Mvc;
using Volo.Abp;
using Volo.Abp.Application.Dtos;

namespace Acme.TestCaseManagement.Controllers;

[RemoteService(Name = "TestCaseManagement")]
[Area("test-case-management")]
[Route("api/test-case-management/runs")]
public class TestRunController : TestCaseManagementController, ITestRunAppService
{
    private readonly ITestRunAppService _runAppService;

    public TestRunController(ITestRunAppService runAppService)
    {
        _runAppService = runAppService;
    }

    /// <inheritdoc />
    [HttpGet("{id:guid}")]
    public virtual Task<TestRunDto> GetAsync(Guid id)
    {
        return _runAppService.GetAsync(id);
    }

    /// <inheritdoc />
    [HttpGet]
    public virtual Task<PagedResultDto<TestRunDto>> GetListAsync([FromQuery] GetTestRunListInput input)
    {
        return _runAppService.GetListAsync(input);
    }

    /// <inheritdoc />
    [HttpPost]
    public virtual Task<TestRunDto> CreateAsync(CreateTestRunDto input)
    {
        return _runAppService.CreateAsync(input);
    }

    /// <inheritdoc />
    [HttpPost("{id:guid}/items")]
    public virtual Task<TestRunDto> AddItemsAsync(Guid id, AddTestRunItemsDto input)
    {
        return _runAppService.AddItemsAsync(id, input);
    }

    /// <inheritdoc />
    [HttpPut("{id:guid}/items/{itemId:guid}/assignee")]
    public virtual Task<TestRunDto> AssignTesterAsync(Guid id, Guid itemId, AssignTestRunItemDto input)
    {
        return _runAppService.AssignTesterAsync(id, itemId, input);
    }

    /// <inheritdoc />
    [HttpPost("{id:guid}/items/{itemId:guid}/executions")]
    public virtual Task<TestExecutionDto> ExecuteItemAsync(Guid id, Guid itemId, ExecuteTestItemDto input)
    {
        return _runAppService.ExecuteItemAsync(id, itemId, input);
    }

    /// <inheritdoc />
    [HttpGet("{id:guid}/items/{itemId:guid}/executions")]
    public virtual Task<List<TestExecutionDto>> GetExecutionsAsync(Guid id, Guid itemId)
    {
        return _runAppService.GetExecutionsAsync(id, itemId);
    }

    /// <inheritdoc />
    [HttpPost("{id:guid}/executions/batch")]
    public virtual Task<List<TestExecutionDto>> BatchExecuteAsync(Guid id, BatchExecuteTestItemsDto input)
    {
        return _runAppService.BatchExecuteAsync(id, input);
    }

    // The executions resource lives outside the "runs" prefix, so these routes are absolute.
    /// <inheritdoc />
    [HttpPost("~/api/test-case-management/executions/{executionId:guid}/defects")]
    public virtual Task<DefectLinkDto> AddDefectLinkAsync(Guid executionId, AddDefectLinkDto input)
    {
        return _runAppService.AddDefectLinkAsync(executionId, input);
    }

    /// <inheritdoc />
    [HttpGet("~/api/test-case-management/executions/{executionId:guid}/defects")]
    public virtual Task<List<DefectLinkDto>> GetDefectLinksAsync(Guid executionId)
    {
        return _runAppService.GetDefectLinksAsync(executionId);
    }

    /// <inheritdoc />
    [HttpPut("~/api/test-case-management/executions/{executionId:guid}/defects/{defectLinkId:guid}")]
    public virtual Task<DefectLinkDto> UpdateDefectLinkAsync(Guid executionId, Guid defectLinkId, UpdateDefectLinkDto input)
    {
        return _runAppService.UpdateDefectLinkAsync(executionId, defectLinkId, input);
    }

    /// <inheritdoc />
    [HttpDelete("~/api/test-case-management/executions/{executionId:guid}/defects/{defectLinkId:guid}")]
    public virtual Task RemoveDefectLinkAsync(Guid executionId, Guid defectLinkId)
    {
        return _runAppService.RemoveDefectLinkAsync(executionId, defectLinkId);
    }

    /// <inheritdoc />
    [HttpPost("{id:guid}/complete")]
    public virtual Task<TestRunDto> CompleteAsync(Guid id)
    {
        return _runAppService.CompleteAsync(id);
    }
}
