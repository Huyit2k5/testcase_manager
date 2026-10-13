using Acme.TestCaseManagement.Permissions;
using Acme.TestCaseManagement.Enums;
using Acme.TestCaseManagement.Plans;
using Acme.TestCaseManagement.Repositories;
using Acme.TestCaseManagement.Requirements.Dtos;
using Microsoft.AspNetCore.Authorization;
using Volo.Abp.Application.Dtos;
using Volo.Abp.Domain.Repositories;

namespace Acme.TestCaseManagement.Requirements;

[Authorize(TestCaseManagementPermissions.Requirements.Default)]
public class RequirementAppService : TestCaseManagementAppService, IRequirementAppService
{
    private readonly IRepository<Requirement, Guid> _requirementRepository;
    private readonly IRepository<RequirementTestCase> _linkRepository;
    private readonly ITestCaseRepository _testCaseRepository;
    private readonly RequirementManager _requirementManager;

    public RequirementAppService(
        IRepository<Requirement, Guid> requirementRepository,
        IRepository<RequirementTestCase> linkRepository,
        ITestCaseRepository testCaseRepository,
        RequirementManager requirementManager)
    {
        _requirementRepository = requirementRepository;
        _linkRepository = linkRepository;
        _testCaseRepository = testCaseRepository;
        _requirementManager = requirementManager;
    }

    public virtual async Task<RequirementDto> GetAsync(Guid id)
    {
        return await MapAsync(await _requirementRepository.GetAsync(id));
    }

    /// <summary>The requirement as a DTO with the number of its test cases that are to be checked against it.</summary>
    protected virtual async Task<RequirementDto> MapAsync(Requirement requirement)
    {
        var dto = ObjectMapper.Map<Requirement, RequirementDto>(requirement);
        dto.SuspectTestCaseCount = (await SuspectCountsAsync(new[] { requirement.Id })).GetValueOrDefault(requirement.Id);
        return dto;
    }

    /// <summary>Per requirement, the linked test cases that are suspect and still count (they exist and are not deprecated).</summary>
    protected virtual async Task<Dictionary<Guid, int>> SuspectCountsAsync(IReadOnlyCollection<Guid> requirementIds)
    {
        var ids = requirementIds.ToList();
        var links = await _linkRepository.GetListAsync(x => ids.Contains(x.RequirementId) && x.IsSuspect);
        if (links.Count == 0)
        {
            return new Dictionary<Guid, int>();
        }

        var testCaseIds = links.Select(l => l.TestCaseId).Distinct().ToList();
        var counted = (await _testCaseRepository.GetListAsync(x => testCaseIds.Contains(x.Id)))
            .Where(tc => tc.Status != TestCaseStatus.Deprecated)
            .Select(tc => tc.Id)
            .ToHashSet();

        return links.Where(l => counted.Contains(l.TestCaseId)).GroupBy(l => l.RequirementId).ToDictionary(g => g.Key, g => g.Count());
    }

    public virtual async Task<PagedResultDto<RequirementDto>> GetListAsync(GetRequirementListInput input)
    {
        var query = (await _requirementRepository.GetQueryableAsync())
            .WhereIf(input.ProjectId.HasValue, x => x.ProjectId == input.ProjectId)
            .WhereIf(input.MilestoneId.HasValue, x => x.MilestoneId == input.MilestoneId);

        if (!string.IsNullOrWhiteSpace(input.Filter))
        {
            var text = input.Filter.Trim().ToLowerInvariant();
            query = query.Where(x => x.Code.ToLower().Contains(text) || x.Title.ToLower().Contains(text));
        }

        var totalCount = await AsyncExecuter.CountAsync(query);
        var items = await AsyncExecuter.ToListAsync(
            Sort(query, input.Sorting).Skip(input.SkipCount).Take(input.MaxResultCount));

        var dtos = ObjectMapper.Map<List<Requirement>, List<RequirementDto>>(items);
        var suspect = await SuspectCountsAsync(items.Select(i => i.Id).ToList());
        foreach (var dto in dtos)
        {
            dto.SuspectTestCaseCount = suspect.GetValueOrDefault(dto.Id);
        }

        return new PagedResultDto<RequirementDto>(totalCount, dtos);
    }

    [Authorize(TestCaseManagementPermissions.Requirements.Manage)]
    public virtual async Task<RequirementDto> CreateAsync(CreateUpdateRequirementDto input)
    {
        var requirement = await _requirementManager.CreateAsync(input.Code, input.Title, input.ProjectId);
        requirement.SetDetails(input.Description, input.AcceptanceCriteria, input.Priority, input.MilestoneId);

        await _requirementRepository.InsertAsync(requirement, autoSave: true);

        return await MapAsync(requirement);
    }

    [Authorize(TestCaseManagementPermissions.Requirements.Manage)]
    public virtual async Task<RequirementDto> UpdateAsync(Guid id, CreateUpdateRequirementDto input)
    {
        var requirement = await _requirementRepository.GetAsync(id);
        var before = (Title: requirement.Title, Description: Normalize(requirement.Description), Criteria: Normalize(requirement.AcceptanceCriteria));

        await _requirementManager.ChangeCodeAsync(requirement, input.Code);
        requirement.SetTitle(input.Title);
        requirement.SetDetails(input.Description, input.AcceptanceCriteria, input.Priority, input.MilestoneId);

        await _requirementRepository.UpdateAsync(requirement, autoSave: true);

        // What the tests verify against changed: the test cases linked to it are to be checked against the new text.
        var changed = new List<string>();
        if (!string.Equals(before.Title, requirement.Title, StringComparison.Ordinal)) { changed.Add("Title"); }
        if (!string.Equals(before.Description, Normalize(requirement.Description), StringComparison.Ordinal)) { changed.Add("Description"); }
        if (!string.Equals(before.Criteria, Normalize(requirement.AcceptanceCriteria), StringComparison.Ordinal)) { changed.Add("AcceptanceCriteria"); }
        await _requirementManager.MarkLinksSuspectAsync(requirement, changed);

        return await MapAsync(requirement);
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

    private static string Normalize(string? text) => text?.Trim() ?? string.Empty;

    public virtual async Task<List<RequirementLinkDto>> GetForTestCaseAsync(Guid testCaseId)
    {
        await _testCaseRepository.GetAsync(testCaseId, includeDetails: false);   // 404 when it does not exist or is out of reach

        var links = await _linkRepository.GetListAsync(x => x.TestCaseId == testCaseId);
        var requirementIds = links.Select(l => l.RequirementId).ToList();
        var requirements = (await _requirementRepository.GetListAsync(x => requirementIds.Contains(x.Id))).ToDictionary(x => x.Id);

        return links
            .Where(l => requirements.ContainsKey(l.RequirementId))
            .Select(l => new RequirementLinkDto
            {
                RequirementId = l.RequirementId,
                Code = requirements[l.RequirementId].Code,
                Title = requirements[l.RequirementId].Title,
                IsSuspect = l.IsSuspect,
                SuspectSince = l.SuspectSince,
                SuspectReason = l.SuspectReason,
            })
            .OrderBy(x => x.Code)
            .ToList();
    }

    [Authorize(TestCaseManagementPermissions.TestCases.Update)]
    public virtual async Task<RequirementReviewResultDto> ReviewLinkAsync(Guid id, Guid testCaseId)
    {
        var requirement = await _requirementRepository.GetAsync(id);
        await _testCaseRepository.GetAsync(testCaseId, includeDetails: false);

        return new RequirementReviewResultDto { Reviewed = await _requirementManager.ReviewLinkAsync(requirement, testCaseId) ? 1 : 0 };
    }

    [Authorize(TestCaseManagementPermissions.TestCases.Update)]
    public virtual async Task<RequirementReviewResultDto> ReviewAllAsync(Guid id)
    {
        var requirement = await _requirementRepository.GetAsync(id);

        return new RequirementReviewResultDto { Reviewed = await _requirementManager.ReviewAllLinksAsync(requirement) };
    }

    private static IQueryable<Requirement> Sort(IQueryable<Requirement> query, string? sorting)
    {
        var (column, descending) = TestPlanAppService.ParseSorting(sorting);

        var ordered = (column, descending) switch
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

        // The columns above are not unique. Without a last unique key a page boundary can repeat or skip rows: SQL Server and
        // PostgreSQL order equal values as they like, differently from one query to the next.
        return ordered.ThenBy(x => x.Id);
    }
}
