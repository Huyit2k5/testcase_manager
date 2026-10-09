using Acme.TestCaseManagement.Suites;
using Acme.TestCaseManagement.Suites.Dtos;
using Microsoft.AspNetCore.Mvc;
using Volo.Abp;

namespace Acme.TestCaseManagement.Controllers;

[RemoteService(Name = "TestCaseManagement")]
[Area("test-case-management")]
[Route("api/test-case-management/suites")]
public class TestSuiteController : TestCaseManagementController, ITestSuiteAppService
{
    private readonly ITestSuiteAppService _suiteAppService;

    public TestSuiteController(ITestSuiteAppService suiteAppService)
    {
        _suiteAppService = suiteAppService;
    }

    /// <inheritdoc />
    [HttpGet("{id:guid}")]
    public virtual Task<TestSuiteDto> GetAsync(Guid id)
    {
        return _suiteAppService.GetAsync(id);
    }

    /// <inheritdoc />
    [HttpGet("tree")]
    public virtual Task<List<TestSuiteTreeDto>> GetTreeAsync([FromQuery] Guid? projectId = null)
    {
        return _suiteAppService.GetTreeAsync(projectId);
    }

    /// <inheritdoc />
    [HttpPost]
    public virtual Task<TestSuiteDto> CreateAsync(CreateTestSuiteDto input)
    {
        return _suiteAppService.CreateAsync(input);
    }

    /// <inheritdoc />
    [HttpPut("{id:guid}")]
    public virtual Task<TestSuiteDto> UpdateAsync(Guid id, UpdateTestSuiteDto input)
    {
        return _suiteAppService.UpdateAsync(id, input);
    }

    /// <inheritdoc />
    [HttpDelete("{id:guid}")]
    public virtual Task DeleteAsync(Guid id)
    {
        return _suiteAppService.DeleteAsync(id);
    }

    /// <inheritdoc />
    [HttpPost("{id:guid}/move")]
    public virtual Task<TestSuiteDto> MoveAsync(Guid id, MoveTestSuiteDto input)
    {
        return _suiteAppService.MoveAsync(id, input);
    }
}
