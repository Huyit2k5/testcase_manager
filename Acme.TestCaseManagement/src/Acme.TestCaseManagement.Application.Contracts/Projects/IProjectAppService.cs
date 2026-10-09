using Volo.Abp.Application.Services;

namespace Acme.TestCaseManagement.Projects;

public interface IProjectAppService : IApplicationService
{
    /// <summary>
    /// The projects, by name. The first time it is called in a library that has none, it makes the default project and gives it
    /// what was there before projects existed.
    /// </summary>
    Task<List<ProjectDto>> GetListAsync(GetProjectListInput input);

    Task<ProjectDto> GetAsync(Guid id);

    Task<ProjectDto> CreateAsync(CreateProjectDto input);

    /// <summary>Changes the name and the description. The key stays.</summary>
    Task<ProjectDto> UpdateAsync(Guid id, UpdateProjectDto input);

    /// <summary>An archived project is kept for reading, and nothing can be added to it.</summary>
    Task<ProjectDto> ArchiveAsync(Guid id);

    Task<ProjectDto> RestoreAsync(Guid id);

    /// <summary>Soft-deletes a project that has nothing in it; otherwise archive it.</summary>
    Task DeleteAsync(Guid id);
}
