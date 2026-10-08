using Acme.TestCaseManagement.Enums;
using Acme.TestCaseManagement.Permissions;
using Acme.TestCaseManagement.Plans;
using Acme.TestCaseManagement.Quality;
using Acme.TestCaseManagement.Repositories;
using Acme.TestCaseManagement.Runs.Dtos;
using Acme.TestCaseManagement.TestCases;
using Microsoft.AspNetCore.Authorization;
using Volo.Abp;
using Volo.Abp.Application.Dtos;
using Volo.Abp.Domain.Entities;
using Volo.Abp.Domain.Repositories;

namespace Acme.TestCaseManagement.Runs;

[Authorize(TestCaseManagementPermissions.TestRuns.Default)]
public class TestRunAppService : TestCaseManagementAppService, ITestRunAppService
{
    private readonly IRepository<TestRun, Guid> _runRepository;
    private readonly IRepository<TestExecution, Guid> _executionRepository;
    private readonly IRepository<TestCaseVersion, Guid> _versionRepository;
    private readonly IRepository<DefectLink, Guid> _defectRepository;
    private readonly ITestCaseRepository _testCaseRepository;
    private readonly TestRunManager _runManager;
    private readonly DefectLinkManager _defectManager;

    public TestRunAppService(
        IRepository<TestRun, Guid> runRepository,
        IRepository<TestExecution, Guid> executionRepository,
        IRepository<TestCaseVersion, Guid> versionRepository,
        IRepository<DefectLink, Guid> defectRepository,
        ITestCaseRepository testCaseRepository,
        TestRunManager runManager,
        DefectLinkManager defectManager)
    {
        _runRepository = runRepository;
        _executionRepository = executionRepository;
        _versionRepository = versionRepository;
        _defectRepository = defectRepository;
        _testCaseRepository = testCaseRepository;
        _runManager = runManager;
        _defectManager = defectManager;
    }

    public virtual async Task<TestRunDto> GetAsync(Guid id)
    {
        return await BuildRunDtoAsync(await _runRepository.GetAsync(id));
    }

    public virtual async Task<PagedResultDto<TestRunDto>> GetListAsync(GetTestRunListInput input)
    {
        var query = (await _runRepository.GetQueryableAsync())
            .WhereIf(input.TestPlanId.HasValue, x => x.TestPlanId == input.TestPlanId)
            .WhereIf(input.Status.HasValue, x => x.Status == input.Status);

        if (!string.IsNullOrWhiteSpace(input.Filter))
        {
            var text = input.Filter.Trim().ToLowerInvariant();
            query = query.Where(x => x.Title.ToLower().Contains(text));
        }

        if (!string.IsNullOrWhiteSpace(input.Environment))
        {
            var environment = input.Environment.Trim().ToLowerInvariant();
            query = query.Where(x => x.Environment.ToLower() == environment);
        }

        var totalCount = await AsyncExecuter.CountAsync(query);
        var items = await AsyncExecuter.ToListAsync(
            Sort(query, input.Sorting).Skip(input.SkipCount).Take(input.MaxResultCount));

        return new PagedResultDto<TestRunDto>(totalCount, ObjectMapper.Map<List<TestRun>, List<TestRunDto>>(items));
    }

    [Authorize(TestCaseManagementPermissions.TestPlans.Manage)]
    public virtual async Task<TestRunDto> CreateAsync(CreateTestRunDto input)
    {
        var run = await _runManager.CreateRunAsync(input.Title, input.Environment, input.TestPlanId, input.AssignedToUserId);

        foreach (var testCaseId in input.TestCaseIds.Distinct())
        {
            await _runManager.AddTestCaseAsync(run, testCaseId, assignedUserId: null);
        }

        await _runRepository.InsertAsync(run, autoSave: true);

        return await BuildRunDtoAsync(run);
    }

    [Authorize(TestCaseManagementPermissions.TestPlans.Manage)]
    public virtual async Task<TestRunDto> AddItemsAsync(Guid id, AddTestRunItemsDto input)
    {
        var run = await _runRepository.GetAsync(id);

        foreach (var testCaseId in input.TestCaseIds.Distinct())
        {
            await _runManager.AddTestCaseAsync(run, testCaseId, input.AssignedUserId);
        }

        await _runRepository.UpdateAsync(run, autoSave: true);

        return await BuildRunDtoAsync(run);
    }

    [Authorize(TestCaseManagementPermissions.TestPlans.Manage)]
    public virtual async Task<TestRunDto> AssignTesterAsync(Guid id, Guid itemId, AssignTestRunItemDto input)
    {
        var run = await _runRepository.GetAsync(id);
        GetItemOrThrow(run, itemId).AssignTo(input.AssignedUserId);

        await _runRepository.UpdateAsync(run, autoSave: true);

        return await BuildRunDtoAsync(run);
    }

    [Authorize(TestCaseManagementPermissions.TestRuns.Execute)]
    public virtual async Task<TestExecutionDto> ExecuteItemAsync(Guid id, Guid itemId, ExecuteTestItemDto input)
    {
        var run = await _runRepository.GetAsync(id);
        GetItemOrThrow(run, itemId);
        EnsureDefectsAllowed(input);

        return await RecordAttemptAsync(itemId, input);
    }

    [Authorize(TestCaseManagementPermissions.TestRuns.Execute)]
    public virtual async Task<List<TestExecutionDto>> BatchExecuteAsync(Guid id, BatchExecuteTestItemsDto input)
    {
        var run = await _runRepository.GetAsync(id);

        // Validate everything first so that a bad entry is rejected before any attempt is recorded.
        foreach (var entry in input.Items)
        {
            GetItemOrThrow(run, entry.TestRunItemId);

            if (entry.Status == TestResultStatus.Untested)
            {
                throw new BusinessException(TestCaseManagementErrorCodes.InvalidExecutionStatus);
            }

            EnsureDefectsAllowed(entry);
        }

        var results = new List<TestExecutionDto>(input.Items.Count);
        foreach (var entry in input.Items)
        {
            results.Add(await RecordAttemptAsync(entry.TestRunItemId, entry));
        }

        return results;
    }

    public virtual async Task<List<TestExecutionDto>> GetExecutionsAsync(Guid id, Guid itemId)
    {
        var run = await _runRepository.GetAsync(id);
        GetItemOrThrow(run, itemId);

        var executions = (await _executionRepository.GetListAsync(x => x.TestRunItemId == itemId))
            .OrderBy(x => x.AttemptNumber)
            .ToList();
        var executionIds = executions.Select(x => x.Id).ToList();
        var links = (await _defectRepository.GetListAsync(x => executionIds.Contains(x.TestExecutionId)))
            .OrderBy(x => x.CreationTime)
            .ToLookup(x => x.TestExecutionId);

        return executions.Select(execution =>
        {
            var dto = ObjectMapper.Map<TestExecution, TestExecutionDto>(execution);
            dto.DefectLinks = ObjectMapper.Map<List<DefectLink>, List<DefectLinkDto>>(links[execution.Id].ToList());
            return dto;
        }).ToList();
    }

    [Authorize(TestCaseManagementPermissions.TestRuns.Execute)]
    public virtual async Task<DefectLinkDto> AddDefectLinkAsync(Guid executionId, AddDefectLinkDto input)
    {
        var execution = await _executionRepository.GetAsync(executionId);
        var link = await _defectManager.AddAsync(
            execution, input.ExternalSystem, input.IssueKey, input.IssueUrl, input.Severity);

        return ObjectMapper.Map<DefectLink, DefectLinkDto>(link);
    }

    [Authorize(TestCaseManagementPermissions.TestRuns.Execute)]
    public virtual async Task<DefectLinkDto> UpdateDefectLinkAsync(Guid executionId, Guid defectLinkId, UpdateDefectLinkDto input)
    {
        var link = await _defectRepository.FindAsync(x => x.Id == defectLinkId && x.TestExecutionId == executionId);
        if (link == null)
        {
            throw new EntityNotFoundException(typeof(DefectLink), defectLinkId);
        }

        await _defectManager.UpdateAsync(link, input.Severity, input.IsResolved);

        return ObjectMapper.Map<DefectLink, DefectLinkDto>(link);
    }

    public virtual async Task<List<DefectLinkDto>> GetDefectLinksAsync(Guid executionId)
    {
        await _executionRepository.GetAsync(executionId);

        var links = (await _defectRepository.GetListAsync(x => x.TestExecutionId == executionId))
            .OrderBy(x => x.CreationTime)
            .ToList();

        return ObjectMapper.Map<List<DefectLink>, List<DefectLinkDto>>(links);
    }

    [Authorize(TestCaseManagementPermissions.TestRuns.Execute)]
    public virtual async Task RemoveDefectLinkAsync(Guid executionId, Guid defectLinkId)
    {
        var link = await _defectRepository.FindAsync(x => x.Id == defectLinkId && x.TestExecutionId == executionId);
        if (link == null)
        {
            throw new EntityNotFoundException(typeof(DefectLink), defectLinkId);
        }

        // Soft delete: the audit trail keeps who linked and who removed it.
        await _defectRepository.DeleteAsync(link);
    }

    [Authorize(TestCaseManagementPermissions.TestPlans.Manage)]
    public virtual async Task<TestRunDto> CompleteAsync(Guid id)
    {
        var run = await _runRepository.GetAsync(id);

        run.Complete();
        await _runRepository.UpdateAsync(run, autoSave: true);

        return await BuildRunDtoAsync(run);
    }

    protected virtual async Task<TestRunDto> BuildRunDtoAsync(TestRun run)
    {
        var dto = ObjectMapper.Map<TestRun, TestRunDto>(run);

        var items = run.Items.OrderBy(i => i.Sequence).ToList();
        if (items.Count == 0)
        {
            dto.Summary = ObjectMapper.Map<TestRunMetrics, TestRunSummaryDto>(
                TestRunMetrics.Calculate(items, Array.Empty<TestExecution>()));
            return dto;
        }

        var itemIds = items.Select(i => i.Id).ToList();
        var versionIds = items.Select(i => i.TestCaseVersionId).ToList();

        var versions = (await _versionRepository.GetListAsync(v => versionIds.Contains(v.Id)))
            .ToDictionary(v => v.Id);
        var testCaseIds = versions.Values.Select(v => v.TestCaseId).Distinct().ToList();
        var testCases = (await _testCaseRepository.GetListAsync(x => testCaseIds.Contains(x.Id)))
            .ToDictionary(x => x.Id);
        var executions = await _executionRepository.GetListAsync(x => itemIds.Contains(x.TestRunItemId));
        var attemptCounts = executions.GroupBy(e => e.TestRunItemId).ToDictionary(g => g.Key, g => g.Count());

        dto.Items = items.Select(item =>
        {
            var itemDto = ObjectMapper.Map<TestRunItem, TestRunItemDto>(item);

            if (versions.TryGetValue(item.TestCaseVersionId, out var version))
            {
                itemDto.VersionNumber = version.VersionNumber;
                itemDto.TestCaseId = version.TestCaseId;
                itemDto.TestCaseTitle = version.Title;
                itemDto.TestCaseCode = testCases.GetValueOrDefault(version.TestCaseId)?.Code;
            }

            itemDto.AttemptCount = attemptCounts.GetValueOrDefault(item.Id);
            return itemDto;
        }).ToList();

        dto.Summary = ObjectMapper.Map<TestRunMetrics, TestRunSummaryDto>(TestRunMetrics.Calculate(items, executions));
        return dto;
    }

    /// <summary>Records the attempt and then links the defects that came with it, in the caller's unit of work.</summary>
    private async Task<TestExecutionDto> RecordAttemptAsync(Guid itemId, ExecuteTestItemDto input)
    {
        var execution = await _runManager.RecordExecutionAttemptAsync(
            itemId, input.Status, input.ActualResult, input.DurationSeconds);

        var dto = ObjectMapper.Map<TestExecution, TestExecutionDto>(execution);
        foreach (var defect in input.Defects)
        {
            var link = await _defectManager.AddAsync(
                execution, defect.ExternalSystem, defect.IssueKey, defect.IssueUrl, defect.Severity);
            dto.DefectLinks.Add(ObjectMapper.Map<DefectLink, DefectLinkDto>(link));
        }

        return dto;
    }

    /// <summary>Rejects defects on a non-failed result before anything is recorded.</summary>
    private static void EnsureDefectsAllowed(ExecuteTestItemDto input)
    {
        if (input.Defects.Count > 0 && input.Status != TestResultStatus.Failed)
        {
            throw new BusinessException(TestCaseManagementErrorCodes.DefectRequiresFailedExecution)
                .WithData("Status", input.Status);
        }
    }

    private static TestRunItem GetItemOrThrow(TestRun run, Guid itemId)
    {
        return run.FindItem(itemId)
               ?? throw new BusinessException(TestCaseManagementErrorCodes.TestRunItemNotFound).WithData("ItemId", itemId);
    }

    private static IQueryable<TestRun> Sort(IQueryable<TestRun> query, string? sorting)
    {
        var (column, descending) = TestPlanAppService.ParseSorting(sorting);

        var ordered = (column, descending) switch
        {
            ("title", false) => query.OrderBy(x => x.Title),
            ("title", true) => query.OrderByDescending(x => x.Title),
            ("environment", false) => query.OrderBy(x => x.Environment).ThenBy(x => x.Title),
            ("environment", true) => query.OrderByDescending(x => x.Environment).ThenBy(x => x.Title),
            ("status", false) => query.OrderBy(x => x.Status).ThenBy(x => x.Title),
            ("status", true) => query.OrderByDescending(x => x.Status).ThenBy(x => x.Title),
            ("creationtime", false) => query.OrderBy(x => x.CreationTime),
            _ => query.OrderByDescending(x => x.CreationTime),
        };

        // The columns above are not unique. Without a last unique key a page boundary can repeat or skip rows: SQL Server and
        // PostgreSQL order equal values as they like, differently from one query to the next.
        return ordered.ThenBy(x => x.Id);
    }
}
