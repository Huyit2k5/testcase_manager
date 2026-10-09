using Acme.TestCaseManagement.Permissions;
using Acme.TestCaseManagement.Plans;
using Acme.TestCaseManagement.Repositories;
using Acme.TestCaseManagement.Requirements;
using Acme.TestCaseManagement.Runs;
using Acme.TestCaseManagement.Suites;
using Microsoft.AspNetCore.Authorization;
using Volo.Abp.Domain.Repositories;

namespace Acme.TestCaseManagement.Projects;

/// <summary>Everyone who is signed in can read the projects (the pages need them to choose one); managing them needs its own permission.</summary>
[Authorize]
public class ProjectAppService : TestCaseManagementAppService, IProjectAppService
{
    private readonly IRepository<Project, Guid> _projects;
    private readonly IRepository<TestSuite, Guid> _suites;
    private readonly IRepository<TestPlan, Guid> _plans;
    private readonly IRepository<Requirement, Guid> _requirements;
    private readonly IRepository<TestRun, Guid> _runs;
    private readonly ITestCaseRepository _testCases;
    private readonly ProjectManager _manager;

    public ProjectAppService(
        IRepository<Project, Guid> projects,
        IRepository<TestSuite, Guid> suites,
        IRepository<TestPlan, Guid> plans,
        IRepository<Requirement, Guid> requirements,
        IRepository<TestRun, Guid> runs,
        ITestCaseRepository testCases,
        ProjectManager manager)
    {
        _projects = projects;
        _suites = suites;
        _plans = plans;
        _requirements = requirements;
        _runs = runs;
        _testCases = testCases;
        _manager = manager;
    }

    public virtual async Task<List<ProjectDto>> GetListAsync(GetProjectListInput input)
    {
        if (!await _projects.AnyAsync())
        {
            // A library that was used before projects existed: its data goes to the default project, once.
            await LockUntilTheRequestEndsAsync("projects-bootstrap");
            if (!await _projects.AnyAsync())
            {
                await _manager.GetOrCreateDefaultAsync();
                await _manager.AssignUnassignedToDefaultAsync();
            }
        }

        var projects = (await _projects.GetListAsync(x => input.IncludeArchived || !x.IsArchived))
            .OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return await ToDtosAsync(projects);
    }

    public virtual async Task<ProjectDto> GetAsync(Guid id)
    {
        return (await ToDtosAsync(new List<Project> { await _manager.GetAsync(id) })).Single();
    }

    [Authorize(TestCaseManagementPermissions.Projects.Manage)]
    public virtual async Task<ProjectDto> CreateAsync(CreateProjectDto input)
    {
        await LockUntilTheRequestEndsAsync($"project-key:{ProjectManager.NormalizeKey(input.Key)}");
        var project = await _manager.CreateAsync(input.Key, input.Name, input.Description);
        await _projects.InsertAsync(project, autoSave: true);

        return (await ToDtosAsync(new List<Project> { project })).Single();
    }

    [Authorize(TestCaseManagementPermissions.Projects.Manage)]
    public virtual async Task<ProjectDto> UpdateAsync(Guid id, UpdateProjectDto input)
    {
        var project = await _manager.GetAsync(id);
        project.SetName(input.Name);
        project.SetDescription(input.Description);
        await _projects.UpdateAsync(project, autoSave: true);

        return (await ToDtosAsync(new List<Project> { project })).Single();
    }

    [Authorize(TestCaseManagementPermissions.Projects.Manage)]
    public virtual async Task<ProjectDto> ArchiveAsync(Guid id)
    {
        var project = await _manager.GetAsync(id);
        project.Archive();
        await _projects.UpdateAsync(project, autoSave: true);

        return (await ToDtosAsync(new List<Project> { project })).Single();
    }

    [Authorize(TestCaseManagementPermissions.Projects.Manage)]
    public virtual async Task<ProjectDto> RestoreAsync(Guid id)
    {
        var project = await _manager.GetAsync(id);
        project.Restore();
        await _projects.UpdateAsync(project, autoSave: true);

        return (await ToDtosAsync(new List<Project> { project })).Single();
    }

    [Authorize(TestCaseManagementPermissions.Projects.Manage)]
    public virtual async Task DeleteAsync(Guid id)
    {
        var project = await _manager.GetAsync(id);
        await _manager.EnsureEmptyAsync(project);
        await _projects.DeleteAsync(project);
    }

    /// <summary>Adds what is in each project: one query per kind, however many projects there are.</summary>
    private async Task<List<ProjectDto>> ToDtosAsync(List<Project> projects)
    {
        var suiteQuery = await _suites.GetQueryableAsync();
        var suiteProject = (await AsyncExecuter.ToListAsync(suiteQuery.Select(s => new { s.Id, s.ProjectId })))
            .ToDictionary(s => s.Id, s => s.ProjectId);
        var suiteCounts = suiteProject.Values.GroupBy(p => p).ToDictionary(g => g.Key, g => g.Count());

        var testCaseQuery = await _testCases.GetQueryableAsync();
        var casesPerSuite = await AsyncExecuter.ToListAsync(
            testCaseQuery.GroupBy(x => x.SuiteId).Select(g => new { SuiteId = g.Key, Count = g.Count() }));
        var caseCounts = new Dictionary<Guid, int>();
        foreach (var entry in casesPerSuite)
        {
            if (suiteProject.TryGetValue(entry.SuiteId, out var projectId))
            {
                caseCounts[projectId] = caseCounts.GetValueOrDefault(projectId) + entry.Count;
            }
        }

        var planCounts = await CountByProjectAsync(_plans, x => x.ProjectId);
        var requirementCounts = await CountByProjectAsync(_requirements, x => x.ProjectId);
        var runCounts = await CountByProjectAsync(_runs, x => x.ProjectId);

        return projects
            .Select(project =>
            {
                var dto = ObjectMapper.Map<Project, ProjectDto>(project);
                dto.SuiteCount = suiteCounts.GetValueOrDefault(project.Id);
                dto.TestCaseCount = caseCounts.GetValueOrDefault(project.Id);
                dto.PlanCount = planCounts.GetValueOrDefault(project.Id);
                dto.RequirementCount = requirementCounts.GetValueOrDefault(project.Id);
                dto.RunCount = runCounts.GetValueOrDefault(project.Id);
                return dto;
            })
            .ToList();
    }

    private async Task<Dictionary<Guid, int>> CountByProjectAsync<TEntity>(
        IRepository<TEntity, Guid> repository, System.Linq.Expressions.Expression<Func<TEntity, Guid>> projectOf)
        where TEntity : class, Volo.Abp.Domain.Entities.IEntity<Guid>
    {
        var query = await repository.GetQueryableAsync();
        var rows = await AsyncExecuter.ToListAsync(query.GroupBy(projectOf).Select(g => new { ProjectId = g.Key, Count = g.Count() }));
        return rows.ToDictionary(r => r.ProjectId, r => r.Count);
    }
}
