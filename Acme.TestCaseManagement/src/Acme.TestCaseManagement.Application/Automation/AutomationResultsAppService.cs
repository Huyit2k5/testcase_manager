using System.ComponentModel.DataAnnotations;
using System.Security.Cryptography;
using System.Text.Json;
using Acme.TestCaseManagement.Automation.Dtos;
using Acme.TestCaseManagement.Enums;
using Acme.TestCaseManagement.Permissions;
using Acme.TestCaseManagement.Projects;
using Acme.TestCaseManagement.Quality;
using Acme.TestCaseManagement.Repositories;
using Acme.TestCaseManagement.Runs;
using Acme.TestCaseManagement.Runs.Dtos;
using Acme.TestCaseManagement.TestCases;
using Acme.TestCaseManagement.Transfer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;
using Volo.Abp;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.DistributedLocking;
using Volo.Abp.Uow;
using Volo.Abp.Validation;

namespace Acme.TestCaseManagement.Automation;

[Authorize(TestCaseManagementPermissions.AutomationResults.Publish)]
public class AutomationResultsAppService : TestCaseManagementAppService, IAutomationResultsAppService
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private static readonly TimeSpan PublishLockWait = TimeSpan.FromSeconds(30);

    private readonly ITestCaseRepository _testCaseRepository;
    private readonly IRepository<TestRun, Guid> _runRepository;
    private readonly IRepository<TestCaseVersion, Guid> _versionRepository;
    private readonly IRepository<AutomationPublication, Guid> _publicationRepository;
    private readonly TestRunManager _runManager;
    private readonly DefectLinkManager _defectManager;
    private readonly TestCaseManagementAutomationOptions _options;
    private readonly IAbpDistributedLock _distributedLock;
    private readonly ProjectManager _projectManager;

    public AutomationResultsAppService(
        ITestCaseRepository testCaseRepository,
        IRepository<TestRun, Guid> runRepository,
        IRepository<TestCaseVersion, Guid> versionRepository,
        IRepository<AutomationPublication, Guid> publicationRepository,
        TestRunManager runManager,
        DefectLinkManager defectManager,
        IOptions<TestCaseManagementAutomationOptions> options,
        IAbpDistributedLock distributedLock,
        ProjectManager projectManager)
    {
        _projectManager = projectManager;
        _testCaseRepository = testCaseRepository;
        _runRepository = runRepository;
        _versionRepository = versionRepository;
        _publicationRepository = publicationRepository;
        _runManager = runManager;
        _defectManager = defectManager;
        _options = options.Value;
        _distributedLock = distributedLock;
    }

    public virtual async Task<PublishAutomationResultsDto> PublishAsync(PublishAutomationResultsInput input)
    {
        EnsureWellFormed(input);

        var key = string.IsNullOrWhiteSpace(input.IdempotencyKey) ? null : input.IdempotencyKey.Trim();
        if (key == null)
        {
            return await PublishCoreAsync(input, null);
        }

        // Two requests with one key must not both pass the check "was this key used?" before either has written. The lock is
        // held until the unit of work of this request is over (committed or rolled back), so the second request starts
        // after the first has committed and then finds its answer. (The lock is in this process unless the host registers a
        // distributed lock provider.)
        var handle = await _distributedLock.TryAcquireAsync($"tcm-automation-publish:{CurrentTenant.Id}:{key}", PublishLockWait);
        if (handle == null)
        {
            throw new BusinessException(TestCaseManagementErrorCodes.AutomationPublishInProgress).WithData("Key", key);
        }

        var unitOfWork = CurrentUnitOfWork;
        if (unitOfWork == null)
        {
            await using (handle)
            {
                return await PublishCoreAsync(input, key);
            }
        }

        unitOfWork.Disposed += (_, _) => handle.DisposeAsync().AsTask().GetAwaiter().GetResult();
        return await PublishCoreAsync(input, key);
    }

    private async Task<PublishAutomationResultsDto> PublishCoreAsync(PublishAutomationResultsInput input, string? key)
    {
        var requestHash = key == null ? null : HashOf(input);
        if (key != null)
        {
            var replay = await FindEarlierAnswerAsync(key, requestHash!);
            if (replay != null)
            {
                return replay;
            }
        }

        var messages = new TransferMessages(L);
        var run = input.RunId.HasValue ? await GetOpenRunAsync(input.RunId.Value) : null;

        // Pass 1: decide, for every result, which test case it is for. Nothing is written.
        var matches = await MatchAsync(input, run, messages);
        var response = new PublishAutomationResultsDto
        {
            Accepted = true,
            RunId = run?.Id,
            RunStatus = run?.Status,
            Received = input.Results.Count,
            Results = matches.Select(m => m.Outcome).ToList(),
        };
        CountOutcomes(response);

        var recordable = matches.Where(m => m.TestCase != null && m.Outcome.Outcome == AutomationOutcome.Recorded).ToList();
        if (input.FailOnUnmatched && recordable.Count != matches.Count)
        {
            response.Accepted = false;
            response.Recorded = 0;
            return response;
        }

        // Pass 2: find or create the run, schedule what it lacks, record, flag, complete.
        var runCreated = false;
        if (run == null && recordable.Count > 0)
        {
            // A run without a plan is in the project of the test cases it records (the first one decides; a test case of another project is refused).
            Guid? projectId = null;
            if (input.Run!.TestPlanId == null)
            {
                var of = await _projectManager.GetProjectOfSuiteAsync(recordable[0].TestCase!.SuiteId);
                projectId = of == Guid.Empty ? null : of;
            }

            run = await _runManager.CreateRunAsync(input.Run!.Title, input.Run.Environment, input.Run.TestPlanId, projectId: projectId);
            runCreated = true;
        }

        if (run != null)
        {
            response.Scheduled = await ScheduleMissingAsync(run, recordable, runCreated);
        }

        var flagged = await RecordAsync(run, recordable);
        response.FlaggedFlaky = flagged;

        if (run != null && input.CompleteRun)
        {
            run.Complete();
            await _runRepository.UpdateAsync(run, autoSave: true);
        }

        response.RunId = run?.Id;
        response.RunCreated = runCreated;
        response.RunStatus = run?.Status;
        response.Recorded = recordable.Count;

        if (key != null)
        {
            await _publicationRepository.InsertAsync(
                new AutomationPublication(GuidGenerator.Create(), CurrentTenant.Id, key, requestHash!, run?.Id, JsonSerializer.Serialize(response, Json)),
                autoSave: true);
        }

        return response;
    }

    /// <summary>What can be refused before looking at any data: the shape of the request.</summary>
    private void EnsureWellFormed(PublishAutomationResultsInput input)
    {
        if (input.RunId.HasValue == (input.Run != null))
        {
            throw new BusinessException(TestCaseManagementErrorCodes.InvalidAutomationRun);
        }

        if (input.Results.Count > _options.MaxResultsPerRequest)
        {
            throw new BusinessException(TestCaseManagementErrorCodes.AutomationTooManyResults)
                .WithData("Count", input.Results.Count)
                .WithData("Limit", _options.MaxResultsPerRequest);
        }

        // [Required] and [MinLength] look at the list, not at what is in it: a null element or a null Defects list is a malformed payload,
        // and it must be answered with a 400 that says so, not a NullReferenceException.
        if (input.Results.Any(result => result == null))
        {
            throw new AbpValidationException(new List<ValidationResult>
            {
                new("A result must not be null.", new[] { nameof(PublishAutomationResultsInput.Results) }),
            });
        }

        foreach (var result in input.Results)
        {
            if ((object?)result.Defects == null)
            {
                result.Defects = new List<AddDefectLinkDto>();
            }

            if (result.Status == TestResultStatus.Untested)
            {
                throw new BusinessException(TestCaseManagementErrorCodes.InvalidExecutionStatus);
            }

            if (result.Defects.Count > 0 && result.Status != TestResultStatus.Failed)
            {
                throw new BusinessException(TestCaseManagementErrorCodes.DefectRequiresFailedExecution).WithData("Status", result.Status);
            }
        }
    }

    private async Task<TestRun> GetOpenRunAsync(Guid runId)
    {
        var run = await _runRepository.GetAsync(runId);
        if (run.Status == RunStatus.Completed)
        {
            throw new BusinessException(TestCaseManagementErrorCodes.TestRunAlreadyCompleted).WithData("Title", run.Title);
        }

        return run;
    }

    /// <summary>The stored answer of an earlier request with this key; refuses a different request that reuses the key.</summary>
    private async Task<PublishAutomationResultsDto?> FindEarlierAnswerAsync(string key, string requestHash)
    {
        var earlier = await _publicationRepository.FindAsync(x => x.IdempotencyKey == key);
        if (earlier == null)
        {
            return null;
        }

        if (!string.Equals(earlier.RequestHash, requestHash, StringComparison.Ordinal))
        {
            throw new BusinessException(TestCaseManagementErrorCodes.IdempotencyKeyReused).WithData("Key", key);
        }

        var replay = JsonSerializer.Deserialize<PublishAutomationResultsDto>(earlier.ResponseJson, Json)!;
        replay.Replayed = true;
        return replay;
    }

    /// <summary>Matches each result to the one test case that has its Automation ID, and decides whether it can be recorded.</summary>
    private async Task<List<Match>> MatchAsync(PublishAutomationResultsInput input, TestRun? run, TransferMessages messages)
    {
        var automationIds = input.Results.Select(r => r.AutomationId.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var found = (await _testCaseRepository.GetListByAutomationIdsAsync(automationIds))
            .GroupBy(testCase => testCase.AutomationId!.Trim(), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.ToList(), StringComparer.OrdinalIgnoreCase);

        var inRun = await ItemsByTestCaseAsync(run);
        var matches = new List<Match>(input.Results.Count);

        for (var index = 0; index < input.Results.Count; index++)
        {
            var result = input.Results[index];
            var automationId = result.AutomationId.Trim();
            var outcome = new AutomationResultOutcomeDto { Index = index, AutomationId = automationId, Outcome = AutomationOutcome.Recorded };
            var match = new Match(result, outcome);
            matches.Add(match);

            if (!found.TryGetValue(automationId, out var candidates))
            {
                Reject(outcome, AutomationOutcome.Unmatched, messages.Get("Automation:Unmatched", ("AutomationId", automationId)));
                continue;
            }

            if (candidates.Count > 1)
            {
                Reject(
                    outcome,
                    AutomationOutcome.Ambiguous,
                    messages.Get("Automation:Ambiguous", ("Count", candidates.Count), ("AutomationId", automationId), ("Codes", string.Join(", ", candidates.Select(c => c.Code).Order()))));
                continue;
            }

            var testCase = candidates[0];
            outcome.TestCaseCode = testCase.Code;

            if (inRun.ContainsKey(testCase.Id))
            {
                match.TestCase = testCase;
            }
            else if (!input.AddMissingToRun)
            {
                Reject(outcome, AutomationOutcome.NotInRun, messages.Get("Automation:NotInRun", ("Code", testCase.Code)));
            }
            else if (testCase.Status != TestCaseStatus.Approved || testCase.CurrentVersion == 0)
            {
                Reject(outcome, AutomationOutcome.NotApproved, messages.Get("Automation:NotApproved", ("Code", testCase.Code), ("Status", testCase.Status)));
            }
            else
            {
                match.TestCase = testCase;
            }
        }

        return matches;
    }

    /// <summary>The item of each test case in the run. A test case that is in it with two versions maps to the later item.</summary>
    private async Task<Dictionary<Guid, TestRunItem>> ItemsByTestCaseAsync(TestRun? run)
    {
        var map = new Dictionary<Guid, TestRunItem>();
        if (run == null || run.Items.Count == 0)
        {
            return map;
        }

        var versionIds = run.Items.Select(i => i.TestCaseVersionId).ToList();
        var testCaseOfVersion = (await _versionRepository.GetListAsync(v => versionIds.Contains(v.Id))).ToDictionary(v => v.Id, v => v.TestCaseId);

        foreach (var item in run.Items.OrderBy(i => i.Sequence))
        {
            if (testCaseOfVersion.TryGetValue(item.TestCaseVersionId, out var testCaseId))
            {
                map[testCaseId] = item;
            }
        }

        return map;
    }

    /// <summary>Adds the approved test cases that the run does not have yet, and saves the run, so that its items exist.</summary>
    private async Task<int> ScheduleMissingAsync(TestRun run, List<Match> recordable, bool runIsNew)
    {
        var inRun = await ItemsByTestCaseAsync(run);
        var missing = recordable.Select(m => m.TestCase!).DistinctBy(t => t.Id).Where(t => !inRun.ContainsKey(t.Id)).ToList();

        foreach (var testCase in missing)
        {
            await _runManager.AddTestCaseAsync(run, testCase.Id, assignedUserId: null);
        }

        if (runIsNew)
        {
            await _runRepository.InsertAsync(run, autoSave: true);
        }
        else if (missing.Count > 0)
        {
            await _runRepository.UpdateAsync(run, autoSave: true);
        }

        return missing.Count;
    }

    /// <summary>
    /// Records the results, the retries of one test in the order of their AttemptNumber. Returns the codes of the test cases
    /// that were flagged flaky: the runner said so, or the same test both passed and failed in this request.
    /// </summary>
    private async Task<List<string>> RecordAsync(TestRun? run, List<Match> recordable)
    {
        var flagged = new List<string>();
        if (run == null)
        {
            return flagged;
        }

        var itemOf = await ItemsByTestCaseAsync(run);

        foreach (var group in recordable.GroupBy(m => m.TestCase!.Id).OrderBy(g => g.Min(m => m.Outcome.Index)))
        {
            var testCase = group.First().TestCase!;
            var ordered = group.OrderBy(m => m.Result.AttemptNumber ?? 0).ThenBy(m => m.Outcome.Index).ToList();

            foreach (var match in ordered)
            {
                var result = match.Result;
                var execution = await _runManager.RecordExecutionAttemptAsync(
                    itemOf[testCase.Id].Id, result.Status, result.ActualResult, result.DurationSeconds);

                match.Outcome.AttemptNumber = execution.AttemptNumber;

                foreach (var defect in result.Defects)
                {
                    await _defectManager.AddAsync(execution, defect.ExternalSystem, defect.IssueKey, defect.IssueUrl, defect.Severity);
                }
            }

            var intermittent = ordered.Any(m => m.Result.IsFlaky)
                               || (ordered.Any(m => m.Result.Status == TestResultStatus.Failed) && ordered.Any(m => m.Result.Status == TestResultStatus.Passed));
            if (intermittent && !testCase.IsFlaky)
            {
                testCase.SetFlaky(true);
                await _testCaseRepository.UpdateAsync(testCase, autoSave: true);
                flagged.Add(testCase.Code);
            }
        }

        return flagged;
    }

    private static void Reject(AutomationResultOutcomeDto outcome, AutomationOutcome reason, string message)
    {
        outcome.Outcome = reason;
        outcome.Message = message;
    }

    private static void CountOutcomes(PublishAutomationResultsDto response)
    {
        response.Recorded = response.Results.Count(r => r.Outcome == AutomationOutcome.Recorded);
        response.Unmatched = response.Results.Count(r => r.Outcome == AutomationOutcome.Unmatched);
        response.Ambiguous = response.Results.Count(r => r.Outcome == AutomationOutcome.Ambiguous);
        response.NotApproved = response.Results.Count(r => r.Outcome == AutomationOutcome.NotApproved);
        response.NotInRun = response.Results.Count(r => r.Outcome == AutomationOutcome.NotInRun);
    }

    private static string HashOf(PublishAutomationResultsInput input)
    {
        return Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(input, Json))).ToLowerInvariant();
    }

    private sealed class Match
    {
        public Match(AutomationResultInput result, AutomationResultOutcomeDto outcome)
        {
            Result = result;
            Outcome = outcome;
        }

        public AutomationResultInput Result { get; }

        public AutomationResultOutcomeDto Outcome { get; }

        /// <summary>The test case this result will be recorded for; null when it cannot be recorded.</summary>
        public TestCase? TestCase { get; set; }
    }
}
