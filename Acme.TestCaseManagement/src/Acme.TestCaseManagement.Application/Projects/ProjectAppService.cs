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
    private readonly IRepository<ProjectMember, Guid> _members;

    public ProjectAppService(
        IRepository<Project, Guid> projects,
        IRepository<TestSuite, Guid> suites,
        IRepository<TestPlan, Guid> plans,
        IRepository<Requirement, Guid> requirements,
        IRepository<TestRun, Guid> runs,
        ITestCaseRepository testCases,
        ProjectManager manager,
        IRepository<ProjectMember, Guid> members)
    {
        _projects = projects;
        _suites = suites;
        _plans = plans;
        _requirements = requirements;
        _runs = runs;
        _testCases = testCases;
        _manager = manager;
        _members = members;
    }

    public virtual async Task<List<ProjectDto>> GetListAsync(GetProjectListInput input)
    {
        // Seen with every project in reach, whoever asks: whether there is a project at all is not a question about what the caller may see.
        using (ProjectAccessScope.Unrestrict())
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

            // Test cases made before they kept their project take it from their suite, once.
            await _manager.BackfillTestCaseProjectsAsync();
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
    [Authorize(TestCaseManagementPermissions.Projects.Manage)]
    public virtual async Task<ProjectAccessDto> GetAccessAsync(Guid id)
    {
        var project = await _manager.GetAsync(id);
        return await AccessOfAsync(project);
    }

    [Authorize(TestCaseManagementPermissions.Projects.Manage)]
    public virtual async Task<ProjectAccessDto> UpdateAccessAsync(Guid id, ProjectAccessDto input)
    {
        var project = await _manager.GetAsync(id);
        var wanted = input.UserIds.Where(u => u != Guid.Empty).Distinct().ToHashSet();

        var current = await _members.GetListAsync(m => m.ProjectId == id);
        var removed = current.Where(m => !wanted.Contains(m.UserId)).ToList();
        if (removed.Count > 0)
        {
            await _members.DeleteManyAsync(removed);
        }

        var known = current.Select(m => m.UserId).ToHashSet();
        var added = wanted.Where(u => !known.Contains(u))
            .Select(u => new ProjectMember(GuidGenerator.Create(), CurrentTenant.Id, id, u))
            .ToList();
        if (added.Count > 0)
        {
            await _members.InsertManyAsync(added);
        }

        project.SetRestricted(input.IsRestricted);
        await _projects.UpdateAsync(project, autoSave: true);
        return await AccessOfAsync(project);
    }

    private async Task<ProjectAccessDto> AccessOfAsync(Project project)
    {
        var members = await _members.GetListAsync(m => m.ProjectId == project.Id);
        return new ProjectAccessDto { IsRestricted = project.IsRestricted, UserIds = members.Select(m => m.UserId).OrderBy(u => u).ToList() };
    }

    private async Task<List<ProjectDto>> ToDtosAsync(List<Project> projects)
    {
        var memberCounts = (await _members.GetListAsync()).GroupBy(m => m.ProjectId).ToDictionary(g => g.Key, g => g.Count());
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
                dto.MemberCount = memberCounts.GetValueOrDefault(project.Id);
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
