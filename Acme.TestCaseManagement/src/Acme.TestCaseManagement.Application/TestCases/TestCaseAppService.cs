using Acme.TestCaseManagement.Enums;
using Acme.TestCaseManagement.Permissions;
using Acme.TestCaseManagement.Repositories;
using Acme.TestCaseManagement.SharedSteps;
using Acme.TestCaseManagement.Suites;
using Acme.TestCaseManagement.TestCases.Dtos;
using Microsoft.AspNetCore.Authorization;
using Volo.Abp.Application.Dtos;
using Volo.Abp.Authorization;
using Volo.Abp.Domain.Entities;
using Volo.Abp.Domain.Repositories;

namespace Acme.TestCaseManagement.TestCases;

[Authorize(TestCaseManagementPermissions.TestCases.Default)]
public class TestCaseAppService : TestCaseManagementAppService, ITestCaseAppService
{
    /// <summary>Only these columns may be used for sorting; anything else falls back to Code.</summary>
    private static readonly Dictionary<string, string> SortableColumns = new(StringComparer.OrdinalIgnoreCase)
    {
        ["code"] = nameof(TestCase.Code),
        ["title"] = nameof(TestCase.Title),
        ["priority"] = nameof(TestCase.Priority),
        ["severity"] = nameof(TestCase.Severity),
        ["status"] = nameof(TestCase.Status),
        ["creationTime"] = nameof(TestCase.CreationTime),
        ["lastModificationTime"] = nameof(TestCase.LastModificationTime),
    };

    private readonly ITestCaseRepository _testCaseRepository;
    private readonly IRepository<TestCaseVersion, Guid> _versionRepository;
    private readonly IRepository<TestSuite, Guid> _suiteRepository;
    private readonly IDefectLinkRepository _defectLinkRepository;
    private readonly IRepository<SharedStepGroup, Guid> _sharedStepGroups;
    private readonly TestCaseManager _testCaseManager;

    public TestCaseAppService(
        ITestCaseRepository testCaseRepository,
        IRepository<TestCaseVersion, Guid> versionRepository,
        IRepository<TestSuite, Guid> suiteRepository,
        IDefectLinkRepository defectLinkRepository,
        IRepository<SharedStepGroup, Guid> sharedStepGroups,
        TestCaseManager testCaseManager)
    {
        _testCaseRepository = testCaseRepository;
        _versionRepository = versionRepository;
        _suiteRepository = suiteRepository;
        _defectLinkRepository = defectLinkRepository;
        _sharedStepGroups = sharedStepGroups;
        _testCaseManager = testCaseManager;
    }

    public virtual async Task<TestCaseDto> GetAsync(Guid id)
    {
        return await MapAsync(await _testCaseRepository.GetAsync(id));
    }

    public virtual async Task<PagedResultDto<TestCaseDto>> GetListAsync(GetTestCaseListInput input)
    {
        var filter = new TestCaseFilter
        {
            SearchText = input.Filter,
            SuiteIds = await ResolveSuiteIdsAsync(input.SuiteId, input.IncludeDescendantSuites),
            Status = input.Status,
            Priority = input.Priority,
            Severity = input.Severity,
            ExecutionType = input.ExecutionType,
            Kind = input.Kind,
            Layer = input.Layer,
            Tags = input.Tags,
            HasAutomationId = input.HasAutomationId,
        };

        var totalCount = await _testCaseRepository.GetFilteredCountAsync(filter);
        var items = await _testCaseRepository.GetFilteredListAsync(
            filter,
            NormalizeSorting(input.Sorting),
            input.SkipCount,
            input.MaxResultCount);

        return new PagedResultDto<TestCaseDto>(
            totalCount,
            ObjectMapper.Map<List<TestCase>, List<TestCaseDto>>(items));
    }

    [Authorize(TestCaseManagementPermissions.TestCases.Create)]
    public virtual async Task<TestCaseDto> CreateAsync(CreateUpdateTestCaseDto input)
    {
        var testCase = await _testCaseManager.CreateAsync(input.SuiteId, input.Code, input.Title);
        await _testCaseManager.EnsureAutomationIdIsUniqueAsync(input.AutomationId, exceptTestCaseId: null);
        ApplyContent(testCase, input);

        await _testCaseRepository.InsertAsync(testCase, autoSave: true);

        return await MapAsync(testCase);
    }

    [Authorize(TestCaseManagementPermissions.TestCases.Update)]
    public virtual async Task<TestCaseDto> UpdateAsync(Guid id, CreateUpdateTestCaseDto input)
    {
        var testCase = await _testCaseRepository.GetAsync(id);

        await _testCaseManager.ChangeCodeAsync(testCase, input.Code);
        await _testCaseManager.ChangeSuiteAsync(testCase, input.SuiteId);
        testCase.SetTitle(input.Title);

        // Checked only when it changes, so that a test case that already shares an ID can still be edited.
        if (!string.Equals(testCase.AutomationId, TestCaseManager.NormalizeAutomationId(input.AutomationId), StringComparison.Ordinal))
        {
            await _testCaseManager.EnsureAutomationIdIsUniqueAsync(input.AutomationId, testCase.Id);
        }

        ApplyContent(testCase, input);

        await PublishVersionIfApprovedAsync(testCase, input.ChangeSummary);
        await _testCaseRepository.UpdateAsync(testCase, autoSave: true);

        return await MapAsync(testCase);
    }

    [Authorize(TestCaseManagementPermissions.TestCases.Delete)]
    public virtual async Task DeleteAsync(Guid id)
    {
        // Soft delete: versions remain, so historical runs that reference them stay valid.
        await _testCaseRepository.DeleteAsync(id);
    }

    [Authorize(TestCaseManagementPermissions.TestCases.Update)]
    public virtual async Task<TestCaseDto> ReorderStepsAsync(Guid id, ReorderTestStepsDto input)
    {
        var testCase = await _testCaseRepository.GetAsync(id);

        testCase.ReorderSteps(input.StepIds);

        await PublishVersionIfApprovedAsync(testCase, input.ChangeSummary);
        await _testCaseRepository.UpdateAsync(testCase, autoSave: true);

        return await MapAsync(testCase);
    }

    [Authorize(TestCaseManagementPermissions.TestCases.Update)]
    public virtual async Task<TestCaseDto> InsertSharedStepsAsync(Guid id, InsertSharedStepsDto input)
    {
        var testCase = await _testCaseRepository.GetAsync(id);
        var group = await _sharedStepGroups.GetAsync(input.SharedStepGroupId);

        testCase.InsertSharedSteps(group, input.Position);

        await PublishVersionIfApprovedAsync(testCase, input.ChangeSummary);
        await _testCaseRepository.UpdateAsync(testCase, autoSave: true);

        return await MapAsync(testCase);
    }

    [Authorize(TestCaseManagementPermissions.TestCases.Update)]
    public virtual async Task<TestCaseDto> RefreshSharedStepsAsync(Guid id, Guid groupId, RefreshSharedStepsDto input)
    {
        var testCase = await _testCaseRepository.GetAsync(id);
        var group = await _sharedStepGroups.GetAsync(groupId);

        testCase.RefreshSharedSteps(group);

        await PublishVersionIfApprovedAsync(
            testCase, string.IsNullOrWhiteSpace(input.ChangeSummary) ? L["TestCaseManagement:SharedStepsUpdatedSummary", group.Name, group.Revision].Value : input.ChangeSummary);
        await _testCaseRepository.UpdateAsync(testCase, autoSave: true);

        return await MapAsync(testCase);
    }

    [Authorize(TestCaseManagementPermissions.TestCases.Update)]
    public virtual async Task<TestCaseDto> DetachSharedStepsAsync(Guid id, Guid groupId)
    {
        var testCase = await _testCaseRepository.GetAsync(id);

        // Only the link is removed: the steps are the same, so there is nothing to version.
        testCase.DetachSharedSteps(groupId);
        await _testCaseRepository.UpdateAsync(testCase, autoSave: true);

        return await MapAsync(testCase);
    }

    [Authorize(TestCaseManagementPermissions.TestCases.Update)]
    public virtual async Task<TestCaseDto> SetTagsAsync(Guid id, SetTestCaseTagsDto input)
    {
        var testCase = await _testCaseRepository.GetAsync(id);

        // Labels are not content: no new version, and the status stays as it is.
        testCase.SetTags(input.Tags);
        await _testCaseRepository.UpdateAsync(testCase, autoSave: true);

        return await MapAsync(testCase);
    }

    public virtual async Task<List<TagSummaryDto>> GetTagsAsync()
    {
        return (await _testCaseRepository.GetTagSummariesAsync())
            .Select(t => new TagSummaryDto { Name = t.Name, Count = t.Count })
            .ToList();
    }

    [Authorize(TestCaseManagementPermissions.TestCases.Update)]
    public virtual async Task<TestCaseDto> ChangeStatusAsync(Guid id, ChangeTestCaseStatusDto input)
    {
        if (input.TargetStatus == TestCaseStatus.Approved)
        {
            await AuthorizationService.CheckAsync(TestCaseManagementPermissions.TestCases.Approve);
        }

        var testCase = await _testCaseRepository.GetAsync(id);

        await _testCaseManager.ChangeStatusAsync(testCase, input.TargetStatus, input.ChangeSummary);
        await _testCaseRepository.UpdateAsync(testCase, autoSave: true);

        return await MapAsync(testCase);
    }

    public virtual async Task<List<TestCaseVersionDto>> GetVersionsAsync(Guid id)
    {
        await _testCaseRepository.GetAsync(id, includeDetails: false);

        var versions = await _versionRepository.GetListAsync(x => x.TestCaseId == id);

        return ObjectMapper.Map<List<TestCaseVersion>, List<TestCaseVersionDto>>(
            versions.OrderByDescending(x => x.VersionNumber).ToList());
    }

    public virtual async Task<TestCaseVersionDto> GetVersionAsync(Guid id, int versionNumber)
    {
        var version = await _versionRepository.FindAsync(x => x.TestCaseId == id && x.VersionNumber == versionNumber);
        if (version == null)
        {
            throw new EntityNotFoundException(typeof(TestCaseVersion), $"{id}/v{versionNumber}");
        }

        return ObjectMapper.Map<TestCaseVersion, TestCaseVersionDto>(version);
    }

    public virtual async Task<List<TestCaseDefectDto>> GetDefectLinksAsync(Guid id)
    {
        await _testCaseRepository.GetAsync(id, includeDetails: false);

        var items = await _defectLinkRepository.GetListByTestCaseAsync(id);

        return items.Select(x => new TestCaseDefectDto
        {
            DefectLinkId = x.Link.Id,
            ExternalSystem = x.Link.ExternalSystem,
            IssueKey = x.Link.IssueKey,
            IssueUrl = x.Link.IssueUrl,
            Severity = x.Link.Severity,
            IsResolved = x.Link.IsResolved,
            LinkedTime = x.Link.CreationTime,
            LinkedByUserId = x.Link.CreatorId,
            TestExecutionId = x.Link.TestExecutionId,
            AttemptNumber = x.AttemptNumber,
            VersionNumber = x.VersionNumber,
            ExecutionTime = x.ExecutionTime,
            TestRunId = x.TestRunId,
            TestRunTitle = x.TestRunTitle,
            Environment = x.Environment,
        }).ToList();
    }

    /// <summary>The test case as a DTO, with the name of each shared group its steps came from and whether the copy is behind.</summary>
    protected virtual async Task<TestCaseDto> MapAsync(TestCase testCase)
    {
        var dto = ObjectMapper.Map<TestCase, TestCaseDto>(testCase);

        var ids = testCase.SharedStepGroupIds();
        if (ids.Count == 0)
        {
            return dto;
        }

        var groups = (await _sharedStepGroups.GetListAsync(g => ids.Contains(g.Id))).ToDictionary(g => g.Id);
        foreach (var step in dto.Steps.Where(s => s.SharedStepGroupId.HasValue))
        {
            if (groups.TryGetValue(step.SharedStepGroupId!.Value, out var group))
            {
                step.SharedStepGroupName = group.Name;
                step.SharedStepOutdated = (step.SharedStepRevision ?? 0) < group.Revision;
            }
        }

        return dto;
    }

    protected virtual void ApplyContent(TestCase testCase, CreateUpdateTestCaseDto input)
    {
        testCase.SetDetails(
            input.Description,
            input.Preconditions,
            input.Postconditions,
            input.Priority,
            input.Severity,
            input.ExecutionType,
            input.Kind,
            input.Layer,
            TestCaseManager.NormalizeAutomationId(input.AutomationId));
        testCase.SetFlaky(input.IsFlaky);
        if (input.Tags != null)
        {
            testCase.SetTags(input.Tags);
        }

        testCase.SetSteps(
            input.Steps
                .Select(s => new TestStepInput(s.Id, s.Action, s.ExpectedResult, s.TestData))
                .ToList());
    }

    /// <summary>An approved test case that is modified gets a new immutable version so that runs can keep using the old one.</summary>
    protected virtual async Task PublishVersionIfApprovedAsync(TestCase testCase, string? changeSummary)
    {
        if (testCase.Status == TestCaseStatus.Approved)
        {
            await _testCaseManager.PublishNewVersionAsync(testCase, changeSummary);
        }
    }

    protected virtual async Task<IReadOnlyCollection<Guid>?> ResolveSuiteIdsAsync(Guid? suiteId, bool includeDescendants)
    {
        if (!suiteId.HasValue)
        {
            return null;
        }

        if (!includeDescendants)
        {
            return new[] { suiteId.Value };
        }

        var childrenByParent = (await _suiteRepository.GetListAsync())
            .Where(s => s.ParentId.HasValue)
            .ToLookup(s => s.ParentId!.Value, s => s.Id);

        var result = new HashSet<Guid> { suiteId.Value };
        var pending = new Queue<Guid>();
        pending.Enqueue(suiteId.Value);

        while (pending.Count > 0)
        {
            foreach (var childId in childrenByParent[pending.Dequeue()])
            {
                if (result.Add(childId))
                {
                    pending.Enqueue(childId);
                }
            }
        }

        return result;
    }

    private static string NormalizeSorting(string? sorting)
    {
        if (string.IsNullOrWhiteSpace(sorting))
        {
            return nameof(TestCase.Code);
        }

        var parts = new List<string>();
        foreach (var segment in sorting.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var tokens = segment.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (tokens.Length is 0 or > 2 || !SortableColumns.TryGetValue(tokens[0], out var column))
            {
                continue;
            }

            var descending = tokens.Length == 2 && tokens[1].Equals("desc", StringComparison.OrdinalIgnoreCase);
            parts.Add(descending ? $"{column} desc" : column);
        }

        return parts.Count == 0 ? nameof(TestCase.Code) : string.Join(", ", parts);
    }
}
