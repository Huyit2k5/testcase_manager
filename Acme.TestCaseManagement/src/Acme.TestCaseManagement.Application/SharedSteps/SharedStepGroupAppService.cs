using Acme.TestCaseManagement.Enums;
using Acme.TestCaseManagement.Permissions;
using Acme.TestCaseManagement.Repositories;
using Acme.TestCaseManagement.SharedSteps.Dtos;
using Acme.TestCaseManagement.TestCases;
using Microsoft.AspNetCore.Authorization;
using Volo.Abp;
using Volo.Abp.Domain.Repositories;

namespace Acme.TestCaseManagement.SharedSteps;

[Authorize(TestCaseManagementPermissions.SharedSteps.Default)]
public class SharedStepGroupAppService : TestCaseManagementAppService, ISharedStepGroupAppService
{
    private readonly IRepository<SharedStepGroup, Guid> _groups;
    private readonly ITestCaseRepository _testCases;
    private readonly SharedStepGroupManager _manager;
    private readonly TestCaseManager _testCaseManager;

    public SharedStepGroupAppService(
        IRepository<SharedStepGroup, Guid> groups,
        ITestCaseRepository testCases,
        SharedStepGroupManager manager,
        TestCaseManager testCaseManager)
    {
        _groups = groups;
        _testCases = testCases;
        _manager = manager;
        _testCaseManager = testCaseManager;
    }

    public virtual async Task<List<SharedStepGroupSummaryDto>> GetListAsync(GetSharedStepGroupsInput input)
    {
        var filter = input.Filter?.Trim().ToLowerInvariant();
        var groups = await _groups.GetListAsync(
            x => string.IsNullOrEmpty(filter)
                 || x.Name.ToLower().Contains(filter)
                 || (x.Description != null && x.Description.ToLower().Contains(filter)),
            includeDetails: true);
        var usage = await _testCases.GetSharedStepUsageCountsAsync();

        return groups
            .OrderBy(g => g.Name, StringComparer.OrdinalIgnoreCase)
            .Select(g => new SharedStepGroupSummaryDto
            {
                Id = g.Id,
                Name = g.Name,
                Description = g.Description,
                Revision = g.Revision,
                StepCount = g.Steps.Count,
                UsedByCount = usage.GetValueOrDefault(g.Id),
            })
            .ToList();
    }

    public virtual async Task<SharedStepGroupDto> GetAsync(Guid id)
    {
        return ToDto(await _groups.GetAsync(id));
    }

    [Authorize(TestCaseManagementPermissions.SharedSteps.Manage)]
    public virtual async Task<SharedStepGroupDto> CreateAsync(CreateUpdateSharedStepGroupDto input)
    {
        var group = await _manager.CreateAsync(input.Name, input.Description, ToInputs(input));
        await _groups.InsertAsync(group, autoSave: true);

        return ToDto(group);
    }

    [Authorize(TestCaseManagementPermissions.SharedSteps.Manage)]
    public virtual async Task<SharedStepGroupDto> UpdateAsync(Guid id, CreateUpdateSharedStepGroupDto input)
    {
        var group = await _groups.GetAsync(id);

        await _manager.ChangeNameAsync(group, input.Name);
        group.SetDescription(input.Description);
        group.SetSteps(ToInputs(input));

        await _groups.UpdateAsync(group, autoSave: true);

        return ToDto(group);
    }

    [Authorize(TestCaseManagementPermissions.SharedSteps.Manage)]
    public virtual async Task DeleteAsync(Guid id)
    {
        var group = await _groups.GetAsync(id);
        var usage = await _testCases.GetSharedStepUsageCountsAsync();

        if (usage.GetValueOrDefault(id) > 0)
        {
            throw new BusinessException(TestCaseManagementErrorCodes.SharedStepGroupInUse)
                .WithData("Name", group.Name)
                .WithData("Count", usage[id]);
        }

        await _groups.DeleteAsync(group);
    }

    public virtual async Task<List<SharedStepUsageDto>> GetUsageAsync(Guid id)
    {
        var group = await _groups.GetAsync(id);

        return (await _testCases.GetListBySharedStepGroupAsync(id))
            .Select(testCase =>
            {
                var linked = testCase.Steps.Where(s => s.SharedStepGroupId == id).ToList();
                var oldest = linked.Min(s => s.SharedStepRevision ?? 0);
                return new SharedStepUsageDto
                {
                    TestCaseId = testCase.Id,
                    Code = testCase.Code,
                    Title = testCase.Title,
                    Status = testCase.Status,
                    LinkedRevision = oldest,
                    LinkedStepCount = linked.Count,
                    IsOutdated = oldest < group.Revision,
                };
            })
            .ToList();
    }

    [Authorize(TestCaseManagementPermissions.SharedSteps.Manage)]
    public virtual async Task<UpdateSharedStepUsersResultDto> UpdateTestCasesAsync(Guid id, UpdateSharedStepUsersInput input)
    {
        await AuthorizationService.CheckAsync(TestCaseManagementPermissions.TestCases.Update);

        var group = await _groups.GetAsync(id);
        var wanted = input.TestCaseIds?.ToHashSet();
        var result = new UpdateSharedStepUsersResultDto();

        foreach (var testCase in await _testCases.GetListBySharedStepGroupAsync(id))
        {
            var behind = testCase.Steps.Where(s => s.SharedStepGroupId == id).Any(s => (s.SharedStepRevision ?? 0) < group.Revision);
            if (!behind || (wanted != null && !wanted.Contains(testCase.Id)))
            {
                continue;
            }

            testCase.RefreshSharedSteps(group);

            // An approved test case is changed in content, so it goes back to review, as for any other edit of its steps.
            if (_testCaseManager.SendBackForReviewIfApproved(testCase))
            {
                result.SentToReview++;
            }

            await _testCases.UpdateAsync(testCase);
            result.Codes.Add(testCase.Code);
        }

        result.Updated = result.Codes.Count;
        return result;
    }

    private static List<TestStepInput> ToInputs(CreateUpdateSharedStepGroupDto input) =>
        input.Steps.Select(s => new TestStepInput(s.Id, s.Action, s.ExpectedResult, s.TestData)).ToList();

    private static SharedStepGroupDto ToDto(SharedStepGroup group) => new()
    {
        Id = group.Id,
        Name = group.Name,
        Description = group.Description,
        Revision = group.Revision,
        CreationTime = group.CreationTime,
        LastModificationTime = group.LastModificationTime,
        Steps = group.Steps
            .OrderBy(s => s.StepOrder)
            .Select(s => new SharedStepDto { Id = s.Id, StepOrder = s.StepOrder, Action = s.Action, ExpectedResult = s.ExpectedResult, TestData = s.TestData })
            .ToList(),
    };
}
