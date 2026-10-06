using Acme.TestCaseManagement.Permissions;
using Acme.TestCaseManagement.Plans;
using Acme.TestCaseManagement.Requirements.Dtos;
using Microsoft.AspNetCore.Authorization;
using Volo.Abp.Application.Dtos;
using Volo.Abp.Domain.Repositories;

namespace Acme.TestCaseManagement.Requirements;

[Authorize(TestCaseManagementPermissions.Requirements.Default)]
public class RequirementAppService : TestCaseManagementAppService, IRequirementAppService
{
    private readonly IRepository<Requirement, Guid> _requirementRepository;
    private readonly RequirementManager _requirementManager;

    public RequirementAppService(
        IRepository<Requirement, Guid> requirementRepository,
        RequirementManager requirementManager)
    {
        _requirementRepository = requirementRepository;
        _requirementManager = requirementManager;
    }

    public virtual async Task<RequirementDto> GetAsync(Guid id)
    {
        return ObjectMapper.Map<Requirement, RequirementDto>(await _requirementRepository.GetAsync(id));
    }

    public virtual async Task<PagedResultDto<RequirementDto>> GetListAsync(GetRequirementListInput input)
    {
        var query = (await _requirementRepository.GetQueryableAsync())
            .WhereIf(input.MilestoneId.HasValue, x => x.MilestoneId == input.MilestoneId);

        if (!string.IsNullOrWhiteSpace(input.Filter))
        {
            var text = input.Filter.Trim().ToLowerInvariant();
            query = query.Where(x => x.Code.ToLower().Contains(text) || x.Title.ToLower().Contains(text));
        }

        var totalCount = await AsyncExecuter.CountAsync(query);
        var items = await AsyncExecuter.ToListAsync(
            Sort(query, input.Sorting).Skip(input.SkipCount).Take(input.MaxResultCount));

        return new PagedResultDto<RequirementDto>(
            totalCount, ObjectMapper.Map<List<Requirement>, List<RequirementDto>>(items));
    }

    [Authorize(TestCaseManagementPermissions.Requirements.Manage)]
    public virtual async Task<RequirementDto> CreateAsync(CreateUpdateRequirementDto input)
    {
        var requirement = await _requirementManager.CreateAsync(input.Code, input.Title);
        requirement.SetDetails(input.Description, input.AcceptanceCriteria, input.Priority, input.MilestoneId);

        await _requirementRepository.InsertAsync(requirement, autoSave: true);

        return ObjectMapper.Map<Requirement, RequirementDto>(requirement);
    }

    [Authorize(TestCaseManagementPermissions.Requirements.Manage)]
    public virtual async Task<RequirementDto> UpdateAsync(Guid id, CreateUpdateRequirementDto input)
    {
        var requirement = await _requirementRepository.GetAsync(id);

        await _requirementManager.ChangeCodeAsync(requirement, input.Code);
        requirement.SetTitle(input.Title);
        requirement.SetDetails(input.Description, input.AcceptanceCriteria, input.Priority, input.MilestoneId);

        await _requirementRepository.UpdateAsync(requirement, autoSave: true);

        return ObjectMapper.Map<Requirement, RequirementDto>(requirement);
    }

    [Authorize(TestCaseManagementPermissions.Requirements.Manage)]
    public virtual async Task DeleteAsync(Guid id)
    {
        await _requirementRepository.DeleteAsync(id);
    }

    [Authorize(TestCaseManagementPermissions.Requirements.Manage)]
    public virtual async Task LinkTestCasesAsync(Guid id, LinkTestCasesDto input)
    {
        var requirement = await _requirementRepository.GetAsync(id);

        foreach (var testCaseId in input.TestCaseIds.Distinct())
        {
            await _requirementManager.LinkTestCaseAsync(requirement, testCaseId);
        }
    }

    [Authorize(TestCaseManagementPermissions.Requirements.Manage)]
    public virtual async Task UnlinkTestCaseAsync(Guid id, Guid testCaseId)
    {
        var requirement = await _requirementRepository.GetAsync(id);

        await _requirementManager.UnlinkTestCaseAsync(requirement, testCaseId);
    }

    private static IQueryable<Requirement> Sort(IQueryable<Requirement> query, string? sorting)
    {
        var (column, descending) = TestPlanAppService.ParseSorting(sorting);

        return (column, descending) switch
        {
            ("title", false) => query.OrderBy(x => x.Title),
            ("title", true) => query.OrderByDescending(x => x.Title),
            ("priority", false) => query.OrderBy(x => x.Priority).ThenBy(x => x.Code),
            ("priority", true) => query.OrderByDescending(x => x.Priority).ThenBy(x => x.Code),
            ("creationtime", false) => query.OrderBy(x => x.CreationTime),
            ("creationtime", true) => query.OrderByDescending(x => x.CreationTime),
            ("code", true) => query.OrderByDescending(x => x.Code),
            _ => query.OrderBy(x => x.Code),
        };
    }
}
