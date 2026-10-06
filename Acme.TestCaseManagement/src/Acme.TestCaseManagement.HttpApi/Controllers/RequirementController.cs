using Acme.TestCaseManagement.Requirements;
using Acme.TestCaseManagement.Requirements.Dtos;
using Microsoft.AspNetCore.Mvc;
using Volo.Abp;
using Volo.Abp.Application.Dtos;

namespace Acme.TestCaseManagement.Controllers;

[RemoteService(Name = "TestCaseManagement")]
[Area("test-case-management")]
[Route("api/test-case-management/requirements")]
public class RequirementController : TestCaseManagementController, IRequirementAppService
{
    private readonly IRequirementAppService _requirementAppService;

    public RequirementController(IRequirementAppService requirementAppService)
    {
        _requirementAppService = requirementAppService;
    }

    /// <inheritdoc />
    [HttpGet("{id:guid}")]
    public virtual Task<RequirementDto> GetAsync(Guid id)
    {
        return _requirementAppService.GetAsync(id);
    }

    /// <inheritdoc />
    [HttpGet]
    public virtual Task<PagedResultDto<RequirementDto>> GetListAsync([FromQuery] GetRequirementListInput input)
    {
        return _requirementAppService.GetListAsync(input);
    }

    /// <inheritdoc />
    [HttpPost]
    public virtual Task<RequirementDto> CreateAsync(CreateUpdateRequirementDto input)
    {
        return _requirementAppService.CreateAsync(input);
    }

    /// <inheritdoc />
    [HttpPut("{id:guid}")]
    public virtual Task<RequirementDto> UpdateAsync(Guid id, CreateUpdateRequirementDto input)
    {
        return _requirementAppService.UpdateAsync(id, input);
    }

    /// <inheritdoc />
    [HttpDelete("{id:guid}")]
    public virtual Task DeleteAsync(Guid id)
    {
        return _requirementAppService.DeleteAsync(id);
    }

    /// <inheritdoc />
    [HttpPost("{id:guid}/test-cases")]
    public virtual Task LinkTestCasesAsync(Guid id, LinkTestCasesDto input)
    {
        return _requirementAppService.LinkTestCasesAsync(id, input);
    }

    /// <inheritdoc />
    [HttpDelete("{id:guid}/test-cases/{testCaseId:guid}")]
    public virtual Task UnlinkTestCaseAsync(Guid id, Guid testCaseId)
    {
        return _requirementAppService.UnlinkTestCaseAsync(id, testCaseId);
    }
}
