using Acme.TestCaseManagement.Projects;
using Microsoft.AspNetCore.Mvc;
using Volo.Abp;

namespace Acme.TestCaseManagement.Controllers;

[RemoteService(Name = "TestCaseManagement")]
[Area("test-case-management")]
[Route("api/test-case-management/projects")]
public class ProjectController : TestCaseManagementController, IProjectAppService
{
    private readonly IProjectAppService _projectAppService;

    public ProjectController(IProjectAppService projectAppService)
    {
        _projectAppService = projectAppService;
    }

    /// <inheritdoc />
    [HttpGet]
    public virtual Task<List<ProjectDto>> GetListAsync([FromQuery] GetProjectListInput input)
    {
        return _projectAppService.GetListAsync(input);
    }

    /// <inheritdoc />
    [HttpGet("{id:guid}")]
    public virtual Task<ProjectDto> GetAsync(Guid id)
    {
        return _projectAppService.GetAsync(id);
    }

    /// <inheritdoc />
    [HttpPost]
    public virtual Task<ProjectDto> CreateAsync(CreateProjectDto input)
    {
        return _projectAppService.CreateAsync(input);
    }

    /// <inheritdoc />
    [HttpPut("{id:guid}")]
    public virtual Task<ProjectDto> UpdateAsync(Guid id, UpdateProjectDto input)
    {
        return _projectAppService.UpdateAsync(id, input);
    }

    /// <inheritdoc />
    [HttpPost("{id:guid}/archive")]
    public virtual Task<ProjectDto> ArchiveAsync(Guid id)
    {
        return _projectAppService.ArchiveAsync(id);
    }

    /// <inheritdoc />
    [HttpPost("{id:guid}/restore")]
    public virtual Task<ProjectDto> RestoreAsync(Guid id)
    {
        return _projectAppService.RestoreAsync(id);
    }

    /// <inheritdoc />
    [HttpDelete("{id:guid}")]
    public virtual Task DeleteAsync(Guid id)
    {
        return _projectAppService.DeleteAsync(id);
    }
}
