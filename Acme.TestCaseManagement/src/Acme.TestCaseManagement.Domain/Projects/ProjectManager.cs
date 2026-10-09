using System.Text.RegularExpressions;
using Acme.TestCaseManagement.Plans;
using Acme.TestCaseManagement.Quality;
using Acme.TestCaseManagement.Requirements;
using Acme.TestCaseManagement.Runs;
using Acme.TestCaseManagement.Suites;
using Volo.Abp;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Domain.Services;

namespace Acme.TestCaseManagement.Projects;

public class ProjectManager : DomainService
{
    private static readonly Regex KeyPattern = new("^[A-Z][A-Z0-9]*$", RegexOptions.Compiled);

    private readonly IRepository<Project, Guid> _projects;
    private readonly IRepository<TestSuite, Guid> _suites;
    private readonly IRepository<TestPlan, Guid> _plans;
    private readonly IRepository<Requirement, Guid> _requirements;
    private readonly IRepository<TestRun, Guid> _runs;
    private readonly IRepository<SignOffReport, Guid> _reports;

    public ProjectManager(
        IRepository<Project, Guid> projects,
        IRepository<TestSuite, Guid> suites,
        IRepository<TestPlan, Guid> plans,
        IRepository<Requirement, Guid> requirements,
        IRepository<TestRun, Guid> runs,
        IRepository<SignOffReport, Guid> reports)
    {
        _projects = projects;
        _suites = suites;
        _plans = plans;
        _requirements = requirements;
        _runs = runs;
        _reports = reports;
    }

    /// <summary>A key as it is kept: capitals, no spaces. Throws when it is not 2 to 10 capital letters or digits starting with a letter.</summary>
    public static string NormalizeKey(string? key)
    {
        var normalized = key?.Trim().ToUpperInvariant() ?? string.Empty;
        if (normalized.Length < ProjectConsts.MinKeyLength || normalized.Length > ProjectConsts.MaxKeyLength || !KeyPattern.IsMatch(normalized))
        {
            throw new BusinessException(TestCaseManagementErrorCodes.InvalidProjectKey)
                .WithData("Min", ProjectConsts.MinKeyLength)
                .WithData("Max", ProjectConsts.MaxKeyLength);
        }

        return normalized;
    }

    public virtual async Task<Project> CreateAsync(string key, string name, string? description)
    {
        var normalized = NormalizeKey(key);
        if (await _projects.AnyAsync(x => x.Key == normalized))
        {
            throw new BusinessException(TestCaseManagementErrorCodes.DuplicateProjectKey).WithData("Key", normalized);
        }

        return new Project(GuidGenerator.Create(), CurrentTenant.Id, normalized, name, description);
    }

    public virtual async Task<Project> GetAsync(Guid id)
    {
        return await _projects.FindAsync(id)
               ?? throw new BusinessException(TestCaseManagementErrorCodes.ProjectNotFound).WithData("ProjectId", id);
    }

    /// <summary>
    /// The project a new suite, plan, requirement or run goes to: the one named, or the default project when none is. Throws when it
    /// does not exist or is archived. The default project is made the first time it is needed.
    /// </summary>
    public virtual async Task<Project> ResolveForNewAsync(Guid? projectId)
    {
        var project = projectId.HasValue ? await GetAsync(projectId.Value) : await GetOrCreateDefaultAsync();
        EnsureNotArchived(project);
        return project;
    }

    public virtual async Task<Project> GetOrCreateDefaultAsync()
    {
        var existing = await _projects.FindAsync(x => x.Key == ProjectConsts.DefaultKey);
        if (existing != null)
        {
            return existing;
        }

        var created = new Project(GuidGenerator.Create(), CurrentTenant.Id, ProjectConsts.DefaultKey, ProjectConsts.DefaultName, null);
        return await _projects.InsertAsync(created, autoSave: true);
    }

    public virtual void EnsureNotArchived(Project project)
    {
        if (project.IsArchived)
        {
            throw new BusinessException(TestCaseManagementErrorCodes.ProjectArchived).WithData("Name", project.Name);
        }
    }

    /// <summary>The project of a suite; Guid.Empty for a suite that was made before projects existed and is not assigned yet.</summary>
    public virtual async Task<Guid> GetProjectOfSuiteAsync(Guid suiteId)
    {
        var suite = await _suites.FindAsync(suiteId)
                    ?? throw new BusinessException(TestCaseManagementErrorCodes.SuiteNotFound).WithData("SuiteId", suiteId);
        return suite.ProjectId;
    }

    /// <summary>Throws when the suite is in an archived project: nothing is added to a project that is archived.</summary>
    public virtual async Task EnsureSuiteIsOpenAsync(Guid suiteId)
    {
        var projectId = await GetProjectOfSuiteAsync(suiteId);
        var project = projectId == Guid.Empty ? null : await _projects.FindAsync(projectId);
        if (project != null)
        {
            EnsureNotArchived(project);
        }
    }

    /// <summary>Throws when two things of different projects are put together. <paramref name="what"/> names them in the message ("A test case and a run").</summary>
    public virtual async Task EnsureSameProjectAsync(string what, Guid first, Guid second)
    {
        if (first == second)
        {
            return;
        }

        var projects = (await _projects.GetListAsync(x => x.Id == first || x.Id == second)).ToDictionary(x => x.Id);
        string NameOf(Guid id) => projects.TryGetValue(id, out var p) ? p.Key : "-";
        throw new BusinessException(TestCaseManagementErrorCodes.DifferentProject)
            .WithData("What", what)
            .WithData("First", NameOf(first))
            .WithData("Second", NameOf(second));
    }

    /// <summary>The ids of every suite of a project, for filtering test cases.</summary>
    public virtual async Task<List<Guid>> GetSuiteIdsAsync(Guid projectId)
    {
        return (await _suites.GetListAsync(x => x.ProjectId == projectId)).Select(x => x.Id).ToList();
    }

    /// <summary>A project can be deleted only while nothing is in it; otherwise it is archived.</summary>
    public virtual async Task EnsureEmptyAsync(Project project)
    {
        var id = project.Id;
        if (await _suites.AnyAsync(x => x.ProjectId == id) || await _plans.AnyAsync(x => x.ProjectId == id)
            || await _requirements.AnyAsync(x => x.ProjectId == id) || await _runs.AnyAsync(x => x.ProjectId == id))
        {
            throw new BusinessException(TestCaseManagementErrorCodes.ProjectNotEmpty).WithData("Name", project.Name);
        }
    }

    /// <summary>
    /// Gives what was made before projects existed (no project, Guid.Empty) to the default project. Safe to run again: it does nothing when
    /// everything has a project. Returns how many rows it assigned.
    /// </summary>
    public virtual async Task<int> AssignUnassignedToDefaultAsync()
    {
        var unassigned = Guid.Empty;
        var suites = await _suites.GetListAsync(x => x.ProjectId == unassigned);
        var plans = await _plans.GetListAsync(x => x.ProjectId == unassigned);
        var requirements = await _requirements.GetListAsync(x => x.ProjectId == unassigned);
        var runs = await _runs.GetListAsync(x => x.ProjectId == unassigned);
        var reports = await _reports.GetListAsync(x => x.ProjectId == unassigned);

        var total = suites.Count + plans.Count + requirements.Count + runs.Count + reports.Count;
        if (total == 0)
        {
            return 0;
        }

        var project = await GetOrCreateDefaultAsync();
        foreach (var suite in suites) { suite.SetProject(project.Id); }
        foreach (var plan in plans) { plan.SetProject(project.Id); }
        foreach (var requirement in requirements) { requirement.SetProject(project.Id); }
        foreach (var run in runs) { run.SetProject(project.Id); }
        foreach (var report in reports) { report.SetProject(project.Id); }

        // Saved at once, and only for what there is: an empty list saves nothing, and the caller reads the counts right after.
        if (suites.Count > 0) { await _suites.UpdateManyAsync(suites, autoSave: true); }
        if (plans.Count > 0) { await _plans.UpdateManyAsync(plans, autoSave: true); }
        if (requirements.Count > 0) { await _requirements.UpdateManyAsync(requirements, autoSave: true); }
        if (runs.Count > 0) { await _runs.UpdateManyAsync(runs, autoSave: true); }
        if (reports.Count > 0) { await _reports.UpdateManyAsync(reports, autoSave: true); }
        return total;
    }
}
