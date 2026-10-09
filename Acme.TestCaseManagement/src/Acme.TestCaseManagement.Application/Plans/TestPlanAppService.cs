using Acme.TestCaseManagement.Permissions;
using Acme.TestCaseManagement.Plans.Dtos;
using Acme.TestCaseManagement.Projects;
using Acme.TestCaseManagement.Runs;
using Microsoft.AspNetCore.Authorization;
using Volo.Abp;
using Volo.Abp.Application.Dtos;
using Volo.Abp.Domain.Repositories;

namespace Acme.TestCaseManagement.Plans;

[Authorize(TestCaseManagementPermissions.TestPlans.Default)]
public class TestPlanAppService : TestCaseManagementAppService, ITestPlanAppService
{
    private readonly IRepository<TestPlan, Guid> _planRepository;
    private readonly IRepository<TestRun, Guid> _runRepository;
    private readonly ProjectManager _projectManager;

    public TestPlanAppService(IRepository<TestPlan, Guid> planRepository, IRepository<TestRun, Guid> runRepository, ProjectManager projectManager)
    {
        _planRepository = planRepository;
        _runRepository = runRepository;
        _projectManager = projectManager;
    }

    public virtual async Task<TestPlanDto> GetAsync(Guid id)
    {
        return ObjectMapper.Map<TestPlan, TestPlanDto>(await _planRepository.GetAsync(id));
    }

    public virtual async Task<PagedResultDto<TestPlanDto>> GetListAsync(GetTestPlanListInput input)
    {
        var query = (await _planRepository.GetQueryableAsync())
            .WhereIf(input.ProjectId.HasValue, x => x.ProjectId == input.ProjectId)
            .WhereIf(input.Status.HasValue, x => x.Status == input.Status)
            .WhereIf(input.MilestoneId.HasValue, x => x.MilestoneId == input.MilestoneId);

        if (!string.IsNullOrWhiteSpace(input.Filter))
        {
            var text = input.Filter.Trim().ToLowerInvariant();
            query = query.Where(x => x.Name.ToLower().Contains(text));
        }

        var totalCount = await AsyncExecuter.CountAsync(query);
        var items = await AsyncExecuter.ToListAsync(
            Sort(query, input.Sorting).Skip(input.SkipCount).Take(input.MaxResultCount));

        return new PagedResultDto<TestPlanDto>(totalCount, ObjectMapper.Map<List<TestPlan>, List<TestPlanDto>>(items));
    }

    [Authorize(TestCaseManagementPermissions.TestPlans.Manage)]
    public virtual async Task<TestPlanDto> CreateAsync(CreateTestPlanDto input)
    {
        var plan = new TestPlan(
            GuidGenerator.Create(),
            CurrentTenant.Id,
            input.Name,
            input.Description,
            input.MilestoneId,
            input.StartDate,
            input.EndDate);
        plan.SetProject((await _projectManager.ResolveForNewAsync(input.ProjectId)).Id);

        await _planRepository.InsertAsync(plan, autoSave: true);

        return ObjectMapper.Map<TestPlan, TestPlanDto>(plan);
    }

    [Authorize(TestCaseManagementPermissions.TestPlans.Manage)]
    public virtual async Task<TestPlanDto> UpdateAsync(Guid id, UpdateTestPlanDto input)
    {
        var plan = await _planRepository.GetAsync(id);

        plan.SetName(input.Name);
        plan.SetDescription(input.Description);
        plan.SetMilestone(input.MilestoneId);
        plan.SetSchedule(input.StartDate, input.EndDate);

        await _planRepository.UpdateAsync(plan, autoSave: true);

        return ObjectMapper.Map<TestPlan, TestPlanDto>(plan);
    }

    [Authorize(TestCaseManagementPermissions.TestPlans.Manage)]
    public virtual async Task DeleteAsync(Guid id)
    {
        var plan = await _planRepository.GetAsync(id);

        if (await _runRepository.AnyAsync(x => x.TestPlanId == id))
        {
            throw new BusinessException(TestCaseManagementErrorCodes.TestPlanHasRuns).WithData("Name", plan.Name);
        }

        await _planRepository.DeleteAsync(plan);
    }

    [Authorize(TestCaseManagementPermissions.TestPlans.Manage)]
    public virtual async Task<TestPlanDto> ChangeStatusAsync(Guid id, ChangeTestPlanStatusDto input)
    {
        var plan = await _planRepository.GetAsync(id);

        plan.ChangeStatus(input.TargetStatus);
        await _planRepository.UpdateAsync(plan, autoSave: true);

        return ObjectMapper.Map<TestPlan, TestPlanDto>(plan);
    }

    private static IQueryable<TestPlan> Sort(IQueryable<TestPlan> query, string? sorting)
    {
        var (column, descending) = ParseSorting(sorting);

        var ordered = (column, descending) switch
        {
            ("startdate", false) => query.OrderBy(x => x.StartDate).ThenBy(x => x.Name),
            ("startdate", true) => query.OrderByDescending(x => x.StartDate).ThenBy(x => x.Name),
            ("enddate", false) => query.OrderBy(x => x.EndDate).ThenBy(x => x.Name),
            ("enddate", true) => query.OrderByDescending(x => x.EndDate).ThenBy(x => x.Name),
            ("status", false) => query.OrderBy(x => x.Status).ThenBy(x => x.Name),
            ("status", true) => query.OrderByDescending(x => x.Status).ThenBy(x => x.Name),
            ("creationtime", false) => query.OrderBy(x => x.CreationTime),
            ("creationtime", true) => query.OrderByDescending(x => x.CreationTime),
            ("name", true) => query.OrderByDescending(x => x.Name),
            _ => query.OrderBy(x => x.Name),
        };

        // The columns above are not unique. Without a last unique key a page boundary can repeat or skip rows: SQL Server and
        // PostgreSQL order equal values as they like, differently from one query to the next.
        return ordered.ThenBy(x => x.Id);
    }

    /// <summary>Reads the first "column [asc|desc]" pair; unknown input falls back to the default order.</summary>
    internal static (string Column, bool Descending) ParseSorting(string? sorting)
    {
        var tokens = (sorting ?? string.Empty)
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .FirstOrDefault()?
            .Split(' ', StringSplitOptions.RemoveEmptyEntries);

        if (tokens is not { Length: 1 or 2 })
        {
            return (string.Empty, false);
        }

        var descending = tokens.Length == 2 && tokens[1].Equals("desc", StringComparison.OrdinalIgnoreCase);
        return (tokens[0].ToLowerInvariant(), descending);
    }
}
