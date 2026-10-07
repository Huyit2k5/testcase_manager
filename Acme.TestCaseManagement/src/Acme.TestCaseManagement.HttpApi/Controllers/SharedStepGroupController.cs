using Acme.TestCaseManagement.SharedSteps;
using Acme.TestCaseManagement.SharedSteps.Dtos;
using Microsoft.AspNetCore.Mvc;
using Volo.Abp;

namespace Acme.TestCaseManagement.Controllers;

[RemoteService(Name = "TestCaseManagement")]
[Area("test-case-management")]
[Route("api/test-case-management/shared-step-groups")]
public class SharedStepGroupController : TestCaseManagementController, ISharedStepGroupAppService
{
    private readonly ISharedStepGroupAppService _sharedStepGroupAppService;

    public SharedStepGroupController(ISharedStepGroupAppService sharedStepGroupAppService)
    {
        _sharedStepGroupAppService = sharedStepGroupAppService;
    }

    /// <inheritdoc />
    [HttpGet]
    public virtual Task<List<SharedStepGroupSummaryDto>> GetListAsync([FromQuery] GetSharedStepGroupsInput input)
    {
        return _sharedStepGroupAppService.GetListAsync(input);
    }

    /// <inheritdoc />
    [HttpGet("{id:guid}")]
    public virtual Task<SharedStepGroupDto> GetAsync(Guid id)
    {
        return _sharedStepGroupAppService.GetAsync(id);
    }

    /// <inheritdoc />
    [HttpPost]
    public virtual Task<SharedStepGroupDto> CreateAsync(CreateUpdateSharedStepGroupDto input)
    {
        return _sharedStepGroupAppService.CreateAsync(input);
    }

    /// <inheritdoc />
    [HttpPut("{id:guid}")]
    public virtual Task<SharedStepGroupDto> UpdateAsync(Guid id, CreateUpdateSharedStepGroupDto input)
    {
        return _sharedStepGroupAppService.UpdateAsync(id, input);
    }

    /// <inheritdoc />
    [HttpDelete("{id:guid}")]
    public virtual Task DeleteAsync(Guid id)
    {
        return _sharedStepGroupAppService.DeleteAsync(id);
    }

    /// <inheritdoc />
    [HttpGet("{id:guid}/usage")]
    public virtual Task<List<SharedStepUsageDto>> GetUsageAsync(Guid id)
    {
        return _sharedStepGroupAppService.GetUsageAsync(id);
    }

    /// <inheritdoc />
    [HttpPost("{id:guid}/update-test-cases")]
    public virtual Task<UpdateSharedStepUsersResultDto> UpdateTestCasesAsync(Guid id, UpdateSharedStepUsersInput input)
    {
        return _sharedStepGroupAppService.UpdateTestCasesAsync(id, input);
    }
}
