using Acme.TestCaseManagement.Enums;
using Acme.TestCaseManagement.Permissions;
using Acme.TestCaseManagement.Plans;
using Acme.TestCaseManagement.Projects;
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
    private readonly IRepository<TestRunItem, Guid> _itemRepository;
    private readonly IRepository<TestCaseVersion, Guid> _versionRepository;
    private readonly IRepository<DefectLink, Guid> _defectRepository;
    private readonly ITestCaseRepository _testCaseRepository;
    private readonly TestRunManager _runManager;
    private readonly DefectLinkManager _defectManager;
    private readonly ProjectManager _projectManager;

    public TestRunAppService(
        IRepository<TestRun, Guid> runRepository,
        IRepository<TestExecution, Guid> executionRepository,
        IRepository<TestRunItem, Guid> itemRepository,
        IRepository<TestCaseVersion, Guid> versionRepository,
        IRepository<DefectLink, Guid> defectRepository,
        ITestCaseRepository testCaseRepository,
        TestRunManager runManager,
        DefectLinkManager defectManager,
        ProjectManager projectManager)
    {
        _projectManager = projectManager;
        _runRepository = runRepository;
        _executionRepository = executionRepository;
        _itemRepository = itemRepository;
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

    public virtual async Task<MyWorkDto> GetMyWorkAsync(GetMyWorkInput input)
    {
        var result = new MyWorkDto();
        var me = CurrentUser.Id;
        if (me == null)
        {
            return result;
        }

        // The runs query carries the project filters, so what the caller may not see is not found here either.
        var items = await _itemRepository.GetQueryableAsync();
        var runs = await _runRepository.GetQueryableAsync();
        var mine = from i in items
                   join r in runs on i.TestRunId equals r.Id
                   where i.AssignedUserId == me && r.Status != RunStatus.Completed
                   select new { Item = i, Run = r };

        mine = mine.WhereIf(input.ProjectId.HasValue, x => x.Run.ProjectId == input.ProjectId);

        var rows = await AsyncExecuter.ToListAsync(mine.Select(x => new
        {
            x.Item.Id,
            x.Item.TestRunId,
            x.Item.TestCaseVersionId,
            x.Item.Sequence,
            x.Item.CurrentStatus,
            RunTitle = x.Run.Title,
            x.Run.Environment,
            x.Run.ProjectId,
            x.Run.CreationTime,
        }));

        result.Total = rows.Count;
        result.Untested = rows.Count(r => r.CurrentStatus == TestResultStatus.Untested);
        result.NeedAttention = rows.Count(r => r.CurrentStatus is TestResultStatus.Failed or TestResultStatus.Blocked);

        const int limit = 500;
        var wanted = rows
            .Where(r => input.IncludeDone || r.CurrentStatus is not (TestResultStatus.Passed or TestResultStatus.Skipped))
            .OrderBy(r => StatusOrder(r.CurrentStatus))
            .ThenByDescending(r => r.CreationTime)
            .ThenBy(r => r.TestRunId)
            .ThenBy(r => r.Sequence)
            .ToList();
        result.Truncated = wanted.Count > limit;
        wanted = wanted.Take(limit).ToList();
        if (wanted.Count == 0)
        {
            return result;
        }

        var itemIds = wanted.Select(r => r.Id).ToList();
        var versionIds = wanted.Select(r => r.TestCaseVersionId).ToList();
        var versions = (await _versionRepository.GetListAsync(v => versionIds.Contains(v.Id))).ToDictionary(v => v.Id);
        var testCaseIds = versions.Values.Select(v => v.TestCaseId).Distinct().ToList();
        var testCases = (await _testCaseRepository.GetListAsync(x => testCaseIds.Contains(x.Id))).ToDictionary(x => x.Id);
        var attempts = (await _executionRepository.GetListAsync(x => itemIds.Contains(x.TestRunItemId)))
            .GroupBy(e => e.TestRunItemId)
            .ToDictionary(g => g.Key, g => g.Count());

        foreach (var row in wanted)
        {
            var dto = new MyWorkItemDto
            {
                RunItemId = row.Id,
                RunId = row.TestRunId,
                RunTitle = row.RunTitle,
                Environment = row.Environment,
                ProjectId = row.ProjectId,
                CurrentStatus = row.CurrentStatus,
                AttemptCount = attempts.GetValueOrDefault(row.Id),
            };
            if (versions.TryGetValue(row.TestCaseVersionId, out var version))
            {
                dto.VersionNumber = version.VersionNumber;
                dto.TestCaseId = version.TestCaseId;
                dto.TestCaseTitle = version.Title;
                dto.TestCaseCode = testCases.GetValueOrDefault(version.TestCaseId)?.Code;
            }

            result.Items.Add(dto);
        }

        return result;
    }

    private static int StatusOrder(TestResultStatus status) => status switch
    {
        TestResultStatus.Untested => 0,
        TestResultStatus.Failed => 1,
        TestResultStatus.Blocked => 2,
        TestResultStatus.Skipped => 3,
        _ => 4,
    };

    public virtual async Task<PagedResultDto<TestRunDto>> GetListAsync(GetTestRunListInput input)
    {
        var query = (await _runRepository.GetQueryableAsync())
            .WhereIf(input.ProjectId.HasValue, x => x.ProjectId == input.ProjectId)
            .WhereIf(input.NoPlan, x => x.TestPlanId == null)
            .WhereIf(!input.NoPlan && input.TestPlanId.HasValue, x => x.TestPlanId == input.TestPlanId)
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

        var dtos = ObjectMapper.Map<List<TestRun>, List<TestRunDto>>(items);
        await FillSummariesAsync(dtos);

        return new PagedResultDto<TestRunDto>(totalCount, dtos);
    }

    /// <summary>
    /// The list shows the progress of each run, so the summary of the runs of the page is worked out here (the items themselves are not sent).
    /// Two queries for the whole page: the items of its runs, and the first attempt of each of those items (the only one that counts for the first-time pass rate).
    /// </summary>
    protected virtual async Task FillSummariesAsync(List<TestRunDto> runs)
    {
        if (runs.Count == 0)
        {
            return;
        }

        var runIds = runs.Select(r => r.Id).ToList();
        var items = await _itemRepository.GetListAsync(i => runIds.Contains(i.TestRunId));

        var itemQuery = await _itemRepository.GetQueryableAsync();
        var executionQuery = await _executionRepository.GetQueryableAsync();
        var firstAttempts = await AsyncExecuter.ToListAsync(
            executionQuery.Where(e => e.AttemptNumber == 1
                                      && itemQuery.Where(i => runIds.Contains(i.TestRunId)).Select(i => i.Id).Contains(e.TestRunItemId)));

        var itemsOfRun = items.ToLookup(i => i.TestRunId);
        var runOfItem = items.ToDictionary(i => i.Id, i => i.TestRunId);
        var attemptsOfRun = firstAttempts.ToLookup(e => runOfItem.GetValueOrDefault(e.TestRunItemId));

        foreach (var dto in runs)
        {
            dto.Summary = ObjectMapper.Map<TestRunMetrics, TestRunSummaryDto>(
                TestRunMetrics.Calculate(itemsOfRun[dto.Id].ToList(), attemptsOfRun[dto.Id]));
        }
    }

    [Authorize(TestCaseManagementPermissions.TestPlans.Manage)]
    public virtual async Task<TestRunDto> CreateAsync(CreateTestRunDto input)
    {
        // A run without a plan or a project of its own is in the project of its test cases (the first one decides).
        var projectId = input.ProjectId;
        if (projectId == null && input.TestPlanId == null && input.TestCaseIds.Count > 0)
        {
            var first = await _testCaseRepository.GetAsync(input.TestCaseIds[0], includeDetails: false);
            var of = await _projectManager.GetProjectOfSuiteAsync(first.SuiteId);
            projectId = of == Guid.Empty ? null : of;
        }

        var run = await _runManager.CreateRunAsync(input.Title, input.Environment, input.TestPlanId, input.AssignedToUserId, projectId);

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

        var stepResults = await _runManager.GetStepResultsAsync(executionIds);

        return executions.Select(execution =>
        {
            var dto = ObjectMapper.Map<TestExecution, TestExecutionDto>(execution);
            dto.StepResults = ObjectMapper.Map<List<TestStepResult>, List<TestStepResultDto>>(stepResults[execution.Id].ToList());
            dto.DefectLinks = ObjectMapper.Map<List<DefectLink>, List<DefectLinkDto>>(links[execution.Id].ToList());
            return dto;
        }).ToList();
    }

    [Authorize(TestCaseManagementPermissions.TestRuns.Execute)]
    public virtual async Task<DefectLinkDto> AddDefectLinkAsync(Guid executionId, AddDefectLinkDto input)
    {
        await EnsureExecutionInReachAsync(executionId);
        var execution = await _executionRepository.GetAsync(executionId);
        var link = await _defectManager.AddAsync(
            execution, input.ExternalSystem, input.IssueKey, input.IssueUrl, input.Severity);

        return ObjectMapper.Map<DefectLink, DefectLinkDto>(link);
    }

    [Authorize(TestCaseManagementPermissions.TestRuns.Execute)]
    public virtual async Task<DefectLinkDto> UpdateDefectLinkAsync(Guid executionId, Guid defectLinkId, UpdateDefectLinkDto input)
    {
        await EnsureExecutionInReachAsync(executionId);
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
        await EnsureExecutionInReachAsync(executionId);
        await _executionRepository.GetAsync(executionId);

        var links = (await _defectRepository.GetListAsync(x => x.TestExecutionId == executionId))
            .OrderBy(x => x.CreationTime)
            .ToList();

        return ObjectMapper.Map<List<DefectLink>, List<DefectLinkDto>>(links);
    }

    [Authorize(TestCaseManagementPermissions.TestRuns.Execute)]
    public virtual async Task RemoveDefectLinkAsync(Guid executionId, Guid defectLinkId)
    {
        await EnsureExecutionInReachAsync(executionId);
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

    /// <summary>An attempt is in the run of an item; if that run is in a project the caller may not see, the attempt is not found either.</summary>
    protected virtual async Task EnsureExecutionInReachAsync(Guid executionId)
    {
        var executions = await _executionRepository.GetQueryableAsync();
        var items = await _itemRepository.GetQueryableAsync();
        var runs = await _runRepository.GetQueryableAsync();
        var found = await AsyncExecuter.AnyAsync(
            from e in executions
            join i in items on e.TestRunItemId equals i.Id
            join r in runs on i.TestRunId equals r.Id
            where e.Id == executionId
            select e.Id);
        if (!found)
        {
            throw new EntityNotFoundException(typeof(TestExecution), executionId);
        }
    }

    [Authorize(TestCaseManagementPermissions.TestPlans.Manage)]
    public virtual async Task DeleteAsync(Guid id)
    {
        var run = await _runRepository.GetAsync(id);

        var itemIds = run.Items.Select(i => i.Id).ToList();
        var hasAttempt = itemIds.Count > 0 && await _executionRepository.AnyAsync(e => itemIds.Contains(e.TestRunItemId));
        if (hasAttempt || run.Items.Any(i => i.CurrentStatus != TestResultStatus.Untested))
        {
            throw new BusinessException(TestCaseManagementErrorCodes.TestRunHasResults).WithData("Title", run.Title);
        }

        // A run without a result has nothing to keep: removing it also removes its items (the database cascades), so that the
        // test cases are free to go into another run.
        await _runRepository.HardDeleteAsync(run, autoSave: true);
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
            itemId, input.Status, input.ActualResult, input.DurationSeconds,
            input.StepResults.Select(s => new StepResultInput(s.StepId, s.Status, s.ActualResult)).ToList());

        var dto = ObjectMapper.Map<TestExecution, TestExecutionDto>(execution);
        var recorded = await _runManager.GetStepResultsAsync(new[] { execution.Id });
        dto.StepResults = ObjectMapper.Map<List<TestStepResult>, List<TestStepResultDto>>(recorded[execution.Id].ToList());
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
