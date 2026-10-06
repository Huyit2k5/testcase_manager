using Acme.TestCaseManagement.Plans;
using Acme.TestCaseManagement.Plans.Dtos;
using Microsoft.AspNetCore.Mvc;
using Volo.Abp;
using Volo.Abp.Application.Dtos;

namespace Acme.TestCaseManagement.Controllers;

[RemoteService(Name = "TestCaseManagement")]
[Area("test-case-management")]
[Route("api/test-case-management/plans")]
public class TestPlanController : TestCaseManagementController, ITestPlanAppService
{
    private readonly ITestPlanAppService _planAppService;

    public TestPlanController(ITestPlanAppService planAppService)
    {
        _planAppService = planAppService;
    }

    /// <inheritdoc />
    [HttpGet("{id:guid}")]
    public virtual Task<TestPlanDto> GetAsync(Guid id)
    {
        return _planAppService.GetAsync(id);
    }

    /// <inheritdoc />
    [HttpGet]
    public virtual Task<PagedResultDto<TestPlanDto>> GetListAsync([FromQuery] GetTestPlanListInput input)
    {
        return _planAppService.GetListAsync(input);
    }

    /// <inheritdoc />
    [HttpPost]
    public virtual Task<TestPlanDto> CreateAsync(CreateTestPlanDto input)
    {
        return _planAppService.CreateAsync(input);
    }

    /// <inheritdoc />
    [HttpPut("{id:guid}")]
    public virtual Task<TestPlanDto> UpdateAsync(Guid id, UpdateTestPlanDto input)
    {
        return _planAppService.UpdateAsync(id, input);
    }

    /// <inheritdoc />
    [HttpDelete("{id:guid}")]
    public virtual Task DeleteAsync(Guid id)
    {
        return _planAppService.DeleteAsync(id);
    }

    /// <inheritdoc />
    [HttpPost("{id:guid}/status")]
    public virtual Task<TestPlanDto> ChangeStatusAsync(Guid id, ChangeTestPlanStatusDto input)
    {
        return _planAppService.ChangeStatusAsync(id, input);
    }
}
