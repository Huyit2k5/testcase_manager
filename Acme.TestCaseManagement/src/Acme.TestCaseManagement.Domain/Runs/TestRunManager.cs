using Acme.TestCaseManagement.Enums;
using Acme.TestCaseManagement.Plans;
using Acme.TestCaseManagement.Projects;
using Acme.TestCaseManagement.Repositories;
using Acme.TestCaseManagement.TestCases;
using Volo.Abp;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Domain.Services;

namespace Acme.TestCaseManagement.Runs;

public class TestRunManager : DomainService
{
    private readonly IRepository<TestPlan, Guid> _planRepository;
    private readonly IRepository<TestRun, Guid> _runRepository;
    private readonly IRepository<TestRunItem, Guid> _itemRepository;
    private readonly IRepository<TestExecution, Guid> _executionRepository;
    private readonly IRepository<TestCaseVersion, Guid> _versionRepository;
    private readonly IRepository<TestStepResult, Guid> _stepResultRepository;
    private readonly ITestCaseRepository _testCaseRepository;
    private readonly ProjectManager _projectManager;

    public TestRunManager(
        IRepository<TestPlan, Guid> planRepository,
        IRepository<TestRun, Guid> runRepository,
        IRepository<TestRunItem, Guid> itemRepository,
        IRepository<TestExecution, Guid> executionRepository,
        IRepository<TestCaseVersion, Guid> versionRepository,
        IRepository<TestStepResult, Guid> stepResultRepository,
        ITestCaseRepository testCaseRepository,
        ProjectManager projectManager)
    {
        _stepResultRepository = stepResultRepository;
        _projectManager = projectManager;
        _planRepository = planRepository;
        _runRepository = runRepository;
        _itemRepository = itemRepository;
        _executionRepository = executionRepository;
        _versionRepository = versionRepository;
        _testCaseRepository = testCaseRepository;
    }

    /// <summary>Builds a new run after checking that the plan (when given) exists. The caller inserts it.</summary>
    public virtual async Task<TestRun> CreateRunAsync(
        string title, string environment, Guid? testPlanId = null, Guid? assignedToUserId = null, Guid? projectId = null)
    {
        Guid project;
        if (testPlanId.HasValue)
        {
            var plan = await _planRepository.FindAsync(testPlanId.Value)
                       ?? throw new BusinessException(TestCaseManagementErrorCodes.TestPlanNotFound).WithData("PlanId", testPlanId.Value);

            // Archived is the end of the plan's life: its reports must not change after that.
            if (plan.Status == PlanStatus.Archived)
            {
                throw new BusinessException(TestCaseManagementErrorCodes.TestPlanArchived).WithData("Name", plan.Name);
            }

            // A run of a plan is in the plan's project; naming another one is refused.
            if (projectId.HasValue)
            {
                await _projectManager.EnsureSameProjectAsync("A run and its plan", projectId.Value, plan.ProjectId);
            }

            project = plan.ProjectId;
        }
        else
        {
            project = (await _projectManager.ResolveForNewAsync(projectId)).Id;
        }

        var run = new TestRun(GuidGenerator.Create(), CurrentTenant.Id, title, environment, testPlanId, assignedToUserId);
        run.SetProject(project);
        return run;
    }

    /// <summary>
    /// Adds an approved library test case to the run, binding the item to the version that is current right now.
    /// Later edits of the test case do not affect the item.
    /// </summary>
    public virtual async Task<TestRunItem> AddTestCaseAsync(TestRun run, Guid testCaseId, Guid? assignedUserId)
    {
        run.EnsureNotCompleted();

        var testCase = await _testCaseRepository.GetAsync(testCaseId, includeDetails: false);
        await _projectManager.EnsureSameProjectAsync("A run and a test case", run.ProjectId, await _projectManager.GetProjectOfSuiteAsync(testCase.SuiteId));

        var version = testCase.HasApprovedVersionInUse()
            ? await _versionRepository.FindAsync(v => v.TestCaseId == testCaseId && v.VersionNumber == testCase.CurrentVersion)
            : null;

        if (version == null)
        {
            throw new BusinessException(TestCaseManagementErrorCodes.TestCaseNotApproved).WithData("Code", testCase.Code);
        }

        // One item per test case: the duplicate check of the run is per version, and every edit of an approved test case makes a new
        // version, so the same test case could otherwise be scheduled twice (and counted twice in the totals and the gate).
        var scheduled = run.Items.Select(i => i.TestCaseVersionId).ToList();
        if (scheduled.Count > 0)
        {
            var already = (await _versionRepository.GetListAsync(v => scheduled.Contains(v.Id) && v.TestCaseId == testCaseId)).FirstOrDefault();
            if (already != null)
            {
                throw new BusinessException(TestCaseManagementErrorCodes.TestCaseAlreadyInRun)
                    .WithData("Code", testCase.Code)
                    .WithData("Version", already.VersionNumber);
            }
        }

        return run.AddItem(version.Id, assignedUserId);
    }

    /// <summary>
    /// Appends a new attempt for <paramref name="runItemId"/> (attempt = highest existing attempt + 1), updates the item's
    /// current status and starts the run. Existing attempts and the master test case are never modified.
    /// <para>
    /// <paramref name="stepResults"/> are the results of single steps, all optional: each names a step of the version the item executes,
    /// at most once, and an attempt is not Passed when one of its steps is Failed or Blocked. The results are read back with the attempt
    /// through <see cref="GetStepResultsAsync"/>.
    /// </para>
    /// </summary>
    public virtual async Task<TestExecution> RecordExecutionAttemptAsync(
        Guid runItemId, TestResultStatus status, string? actualResult, int durationSeconds, IReadOnlyCollection<StepResultInput>? stepResults = null)
    {
        var item = await _itemRepository.FindAsync(runItemId);
        if (item == null)
        {
            throw new BusinessException(TestCaseManagementErrorCodes.TestRunItemNotFound).WithData("ItemId", runItemId);
        }

        // Loads the run with its items, which are the same tracked instances as `item`.
        var run = await _runRepository.GetAsync(item.TestRunId);
        run.EnsureNotCompleted();

        var attempts = await _executionRepository.GetListAsync(x => x.TestRunItemId == runItemId);
        var nextAttempt = attempts.Count == 0 ? 1 : attempts.Max(x => x.AttemptNumber) + 1;

        // Built first: it validates the status, so nothing is changed when the result is not acceptable.
        var execution = new TestExecution(
            GuidGenerator.Create(), item.TenantId, runItemId, nextAttempt, status, actualResult, durationSeconds);

        var steps = stepResults == null || stepResults.Count == 0
            ? new List<TestStepResult>()
            : await BuildStepResultsAsync(item, execution, status, stepResults);

        item.SetCurrentStatus(status);
        run.MarkInProgress();
        await _runRepository.UpdateAsync(run);

        // One save writes the attempt together with the new item and run status. Saving here (not at the end of the
        // unit of work) lets a following attempt in a batch, and any query in the same unit of work, see all of it.
        await _executionRepository.InsertAsync(execution, autoSave: true);
        if (steps.Count > 0)
        {
            await _stepResultRepository.InsertManyAsync(steps, autoSave: true);
        }

        return execution;
    }

    /// <summary>The results of the steps of the given attempts, by attempt, each list in the order of the steps.</summary>
    public virtual async Task<ILookup<Guid, TestStepResult>> GetStepResultsAsync(IReadOnlyCollection<Guid> executionIds)
    {
        if (executionIds.Count == 0)
        {
            return Array.Empty<TestStepResult>().ToLookup(x => x.TestExecutionId);
        }

        var ids = executionIds.ToList();
        return (await _stepResultRepository.GetListAsync(x => ids.Contains(x.TestExecutionId)))
            .OrderBy(x => x.StepOrder)
            .ToLookup(x => x.TestExecutionId);
    }

    private async Task<List<TestStepResult>> BuildStepResultsAsync(
        TestRunItem item, TestExecution execution, TestResultStatus status, IReadOnlyCollection<StepResultInput> stepResults)
    {
        var version = await _versionRepository.GetAsync(item.TestCaseVersionId);
        var stepsOfVersion = TestStepSnapshot.Deserialize(version.StepsJson).ToDictionary(s => s.Id);

        var seen = new HashSet<Guid>();
        var results = new List<TestStepResult>(stepResults.Count);
        foreach (var input in stepResults)
        {
            if (!stepsOfVersion.TryGetValue(input.StepId, out var step))
            {
                throw new BusinessException(TestCaseManagementErrorCodes.TestStepNotInVersion)
                    .WithData("StepId", input.StepId)
                    .WithData("Version", version.VersionNumber);
            }

            if (!seen.Add(input.StepId))
            {
                throw new BusinessException(TestCaseManagementErrorCodes.DuplicateStepResult);
            }

            results.Add(new TestStepResult(GuidGenerator.Create(), item.TenantId, execution.Id, step.Id, step.Order, input.Status, input.ActualResult));
        }

        if (status == TestResultStatus.Passed && results.Any(r => r.Status is TestResultStatus.Failed or TestResultStatus.Blocked))
        {
            throw new BusinessException(TestCaseManagementErrorCodes.PassedWithUnpassedStep);
        }

        return results;
    }
}
