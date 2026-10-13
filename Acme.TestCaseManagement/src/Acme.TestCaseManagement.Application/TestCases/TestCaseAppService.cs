using Acme.TestCaseManagement.Enums;
using Acme.TestCaseManagement.Permissions;
using Acme.TestCaseManagement.Repositories;
using Acme.TestCaseManagement.SharedSteps;
using Acme.TestCaseManagement.Suites;
using Acme.TestCaseManagement.TestCases.Dtos;
using Microsoft.AspNetCore.Authorization;
using Volo.Abp;
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
    private readonly TestCaseHistoryRecorder _history;
    private readonly IRepository<TestCaseHistoryEntry, Guid> _historyRepository;

    public TestCaseAppService(
        ITestCaseRepository testCaseRepository,
        IRepository<TestCaseVersion, Guid> versionRepository,
        IRepository<TestSuite, Guid> suiteRepository,
        IDefectLinkRepository defectLinkRepository,
        IRepository<SharedStepGroup, Guid> sharedStepGroups,
        TestCaseManager testCaseManager,
        TestCaseHistoryRecorder history,
        IRepository<TestCaseHistoryEntry, Guid> historyRepository)
    {
        _testCaseRepository = testCaseRepository;
        _versionRepository = versionRepository;
        _suiteRepository = suiteRepository;
        _defectLinkRepository = defectLinkRepository;
        _sharedStepGroups = sharedStepGroups;
        _testCaseManager = testCaseManager;
        _history = history;
        _historyRepository = historyRepository;
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
            SuiteIds = await ResolveSuiteIdsAsync(input.SuiteId, input.IncludeDescendantSuites, input.ProjectId),
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
        await _history.RecordAsync(testCase.Id, TestCaseHistoryType.Created);

        return await MapAsync(testCase);
    }

    [Authorize(TestCaseManagementPermissions.TestCases.Create)]
    public virtual async Task<TestCaseDto> DuplicateAsync(Guid id, DuplicateTestCaseDto input)
    {
        var source = await _testCaseRepository.GetAsync(id);

        var title = string.IsNullOrWhiteSpace(input.Title) ? L["TestCase:CopyTitle", source.Title].Value : input.Title.Trim();
        if (title.Length > TestCaseConsts.MaxTitleLength)
        {
            title = title[..TestCaseConsts.MaxTitleLength];
        }

        var copy = await _testCaseManager.CreateAsync(input.SuiteId ?? source.SuiteId, input.Code, title);

        // What describes the test case is copied; what ties the original to the rest of the library is not: its Automation ID names
        // one test case only, and its versions, runs, requirements and files belong to the original. Steps become the copy's own.
        ApplyContent(copy, new CreateUpdateTestCaseDto
        {
            Description = source.Description,
            Preconditions = source.Preconditions,
            Postconditions = source.Postconditions,
            Priority = source.Priority,
            Severity = source.Severity,
            ExecutionType = source.ExecutionType,
            Kind = source.Kind,
            Layer = source.Layer,
            IsFlaky = source.IsFlaky,
            Tags = source.Tags.Select(t => t.Name).ToList(),
            Steps = source.Steps.OrderBy(s => s.StepOrder).Select(s => new TestStepDto { Action = s.Action, ExpectedResult = s.ExpectedResult, TestData = s.TestData }).ToList(),
        });

        await _testCaseRepository.InsertAsync(copy, autoSave: true);
        await _history.RecordAsync(copy.Id, TestCaseHistoryType.Created, note: L["TestCase:DuplicatedFrom", source.Code].Value);

        return await MapAsync(copy);
    }

    [Authorize(TestCaseManagementPermissions.TestCases.Update)]
    public virtual async Task<TestCaseDto> UpdateAsync(Guid id, CreateUpdateTestCaseDto input)
    {
        var testCase = await _testCaseRepository.GetAsync(id);
        var before = TestCaseContentSnapshot.Of(testCase);

        if (!string.IsNullOrWhiteSpace(input.Code))
        {
            await _testCaseManager.ChangeCodeAsync(testCase, input.Code);
        }

        await _testCaseManager.ChangeSuiteAsync(testCase, input.SuiteId);
        testCase.SetTitle(input.Title);

        // Checked only when it changes, so that a test case that already shares an ID can still be edited.
        if (!string.Equals(testCase.AutomationId, TestCaseManager.NormalizeAutomationId(input.AutomationId), StringComparison.Ordinal))
        {
            await _testCaseManager.EnsureAutomationIdIsUniqueAsync(input.AutomationId, testCase.Id);
        }

        ApplyContent(testCase, input);

        var changed = before.ChangedFields(TestCaseContentSnapshot.Of(testCase));
        var sentBack = _testCaseManager.SendBackForReviewIfApproved(testCase);
        await _testCaseRepository.UpdateAsync(testCase, autoSave: true);
        if (changed.Count > 0 || sentBack)
        {
            await _history.RecordContentChangeAsync(testCase, TestCaseHistoryType.Edited, string.Join(", ", changed), sentBack);
        }

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

        var sentBack = _testCaseManager.SendBackForReviewIfApproved(testCase);
        await _testCaseRepository.UpdateAsync(testCase, autoSave: true);
        await _history.RecordContentChangeAsync(testCase, TestCaseHistoryType.StepsReordered, null, sentBack);

        return await MapAsync(testCase);
    }

    [Authorize(TestCaseManagementPermissions.TestCases.Update)]
    public virtual async Task<TestCaseDto> InsertSharedStepsAsync(Guid id, InsertSharedStepsDto input)
    {
        var testCase = await _testCaseRepository.GetAsync(id);
        var group = await _sharedStepGroups.GetAsync(input.SharedStepGroupId);

        testCase.InsertSharedSteps(group, input.Position);

        var sentBack = _testCaseManager.SendBackForReviewIfApproved(testCase);
        await _testCaseRepository.UpdateAsync(testCase, autoSave: true);
        await _history.RecordContentChangeAsync(testCase, TestCaseHistoryType.SharedStepsChanged, "Inserted: " + group.Name, sentBack);

        return await MapAsync(testCase);
    }

    [Authorize(TestCaseManagementPermissions.TestCases.Update)]
    public virtual async Task<TestCaseDto> RefreshSharedStepsAsync(Guid id, Guid groupId, RefreshSharedStepsDto input)
    {
        var testCase = await _testCaseRepository.GetAsync(id);
        var group = await _sharedStepGroups.GetAsync(groupId);

        testCase.RefreshSharedSteps(group);

        var sentBack = _testCaseManager.SendBackForReviewIfApproved(testCase);
        await _testCaseRepository.UpdateAsync(testCase, autoSave: true);
        await _history.RecordContentChangeAsync(testCase, TestCaseHistoryType.SharedStepsChanged, "Refreshed: " + group.Name, sentBack);

        return await MapAsync(testCase);
    }

    [Authorize(TestCaseManagementPermissions.TestCases.Update)]
    public virtual async Task<TestCaseDto> DetachSharedStepsAsync(Guid id, Guid groupId)
    {
        var testCase = await _testCaseRepository.GetAsync(id);

        // Only the link is removed: the steps are the same, so there is nothing to version.
        testCase.DetachSharedSteps(groupId);
        await _testCaseRepository.UpdateAsync(testCase, autoSave: true);
        await _history.RecordAsync(testCase.Id, TestCaseHistoryType.SharedStepsChanged, note: "Detached");

        return await MapAsync(testCase);
    }

    [Authorize(TestCaseManagementPermissions.TestCases.Update)]
    public virtual async Task<TestCaseDto> SetTagsAsync(Guid id, SetTestCaseTagsDto input)
    {
        var testCase = await _testCaseRepository.GetAsync(id);

        // Labels are not content: no new version, and the status stays as it is.
        var tagsBefore = string.Join(", ", testCase.Tags.Select(t => t.Name).OrderBy(n => n, StringComparer.OrdinalIgnoreCase));
        testCase.SetTags(input.Tags);
        await _testCaseRepository.UpdateAsync(testCase, autoSave: true);
        var tagsAfter = string.Join(", ", testCase.Tags.Select(t => t.Name).OrderBy(n => n, StringComparer.OrdinalIgnoreCase));
        if (!string.Equals(tagsBefore, tagsAfter, StringComparison.Ordinal))
        {
            await _history.RecordAsync(testCase.Id, TestCaseHistoryType.TagsChanged, note: tagsAfter);
        }

        return await MapAsync(testCase);
    }

    public virtual async Task<List<TagSummaryDto>> GetTagsAsync(Guid? projectId = null)
    {
        var suiteIds = projectId.HasValue ? (await _suiteRepository.GetListAsync(s => s.ProjectId == projectId)).Select(s => s.Id).ToList() : null;
        return (await _testCaseRepository.GetTagSummariesAsync(suiteIds))
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
        await ChangeStatusAndRecordAsync(testCase, input.TargetStatus, input.ChangeSummary);

        return await MapAsync(testCase);
    }

    [Authorize(TestCaseManagementPermissions.TestCases.Update)]
    public virtual async Task<ApproveTestCasesResultDto> ApproveManyAsync(ApproveTestCasesInput input)
    {
        await AuthorizationService.CheckAsync(TestCaseManagementPermissions.TestCases.Approve);

        var result = new ApproveTestCasesResultDto();
        foreach (var id in input.TestCaseIds.Distinct())
        {
            var testCase = await _testCaseRepository.FindAsync(id);
            if (testCase == null)
            {
                result.Failed.Add(new ApproveTestCaseFailureDto
                {
                    TestCaseId = id,
                    ErrorCode = TestCaseManagementErrorCodes.TestCaseNotFound,
                    Message = Describe(new BusinessException(TestCaseManagementErrorCodes.TestCaseNotFound).WithData("Id", id)),
                });
                continue;
            }

            try
            {
                await ChangeStatusAndRecordAsync(testCase, TestCaseStatus.Approved, input.ChangeSummary);
                result.ApprovedCodes.Add(testCase.Code);
            }
            catch (BusinessException exception)
            {
                // The checks of a status change come before anything is changed, so a refused test case is left as it was.
                result.Failed.Add(new ApproveTestCaseFailureDto
                {
                    TestCaseId = id,
                    Code = testCase.Code,
                    ErrorCode = exception.Code ?? string.Empty,
                    Message = Describe(exception),
                });
            }
        }

        result.Approved = result.ApprovedCodes.Count;
        return result;
    }

    /// <summary>The message of a business error in the language of the caller, with the values it carries put in.</summary>
    protected virtual string Describe(BusinessException exception)
    {
        var text = exception.Code == null ? exception.Message : L[exception.Code].Value;
        foreach (System.Collections.DictionaryEntry data in exception.Data)
        {
            text = text.Replace("{" + data.Key + "}", data.Value?.ToString() ?? string.Empty);
        }

        return text;
    }

    /// <summary>One change of status as it is done for a single test case: the change, the version it publishes, and the history.</summary>
    protected virtual async Task ChangeStatusAndRecordAsync(TestCase testCase, TestCaseStatus target, string? changeSummary)
    {
        var from = testCase.Status;

        var published = await _testCaseManager.ChangeStatusAsync(testCase, target, changeSummary);
        await _testCaseRepository.UpdateAsync(testCase, autoSave: true);
        await _history.RecordStatusChangeAsync(testCase, from, published, changeSummary);
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

    public virtual async Task<List<TestCaseHistoryDto>> GetHistoryAsync(Guid id)
    {
        var testCase = await _testCaseRepository.GetAsync(id, includeDetails: false);

        var entries = await _historyRepository.GetListAsync(x => x.TestCaseId == id);
        var lines = ObjectMapper.Map<List<TestCaseHistoryEntry>, List<TestCaseHistoryDto>>(entries);

        // A test case made before the history was kept still tells when it was made and which versions it has.
        if (!lines.Any(l => l.Type == TestCaseHistoryType.Created))
        {
            lines.Add(new TestCaseHistoryDto
            {
                Id = Guid.Empty, TestCaseId = id, Type = TestCaseHistoryType.Created,
                CreationTime = testCase.CreationTime, CreatorId = testCase.CreatorId,
            });
        }

        var published = lines.Where(l => l.Type == TestCaseHistoryType.VersionPublished && l.VersionNumber.HasValue).Select(l => l.VersionNumber!.Value).ToHashSet();
        foreach (var version in await _versionRepository.GetListAsync(x => x.TestCaseId == id))
        {
            if (!published.Contains(version.VersionNumber))
            {
                lines.Add(new TestCaseHistoryDto
                {
                    Id = Guid.Empty, TestCaseId = id, Type = TestCaseHistoryType.VersionPublished, VersionNumber = version.VersionNumber,
                    Note = version.ChangeSummary, CreationTime = version.CreationTime, CreatorId = version.CreatorId,
                });
            }
        }

        return lines.OrderByDescending(l => l.CreationTime).ThenByDescending(l => l.VersionNumber ?? 0).ToList();
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

    protected virtual async Task<IReadOnlyCollection<Guid>?> ResolveSuiteIdsAsync(Guid? suiteId, bool includeDescendants, Guid? projectId = null)
    {
        if (!suiteId.HasValue)
        {
            // Without a suite, a project means every suite of that project (none: no test case).
            return projectId.HasValue
                ? (await _suiteRepository.GetListAsync(s => s.ProjectId == projectId)).Select(s => s.Id).ToList()
                : null;
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

        if (parts.Count == 0)
        {
            parts.Add(nameof(TestCase.Code));
        }

        // Priority, severity and status are not unique: a last unique key keeps the pages from repeating or skipping rows on SQL Server
        // and PostgreSQL, which order equal values as they like.
        parts.Add(nameof(TestCase.Id));
        return string.Join(", ", parts);
    }
}
