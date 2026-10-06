using Acme.TestCaseManagement.Enums;
using Acme.TestCaseManagement.Plans;
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
    private readonly ITestCaseRepository _testCaseRepository;

    public TestRunManager(
        IRepository<TestPlan, Guid> planRepository,
        IRepository<TestRun, Guid> runRepository,
        IRepository<TestRunItem, Guid> itemRepository,
        IRepository<TestExecution, Guid> executionRepository,
        IRepository<TestCaseVersion, Guid> versionRepository,
        ITestCaseRepository testCaseRepository)
    {
        _planRepository = planRepository;
        _runRepository = runRepository;
        _itemRepository = itemRepository;
        _executionRepository = executionRepository;
        _versionRepository = versionRepository;
        _testCaseRepository = testCaseRepository;
    }

    /// <summary>Builds a new run after checking that the plan (when given) exists. The caller inserts it.</summary>
    public virtual async Task<TestRun> CreateRunAsync(
        string title, string environment, Guid? testPlanId = null, Guid? assignedToUserId = null)
    {
        if (testPlanId.HasValue && await _planRepository.FindAsync(testPlanId.Value) == null)
        {
            throw new BusinessException(TestCaseManagementErrorCodes.TestPlanNotFound).WithData("PlanId", testPlanId.Value);
        }

        return new TestRun(GuidGenerator.Create(), CurrentTenant.Id, title, environment, testPlanId, assignedToUserId);
    }

    /// <summary>
    /// Adds an approved library test case to the run, binding the item to the version that is current right now.
    /// Later edits of the test case do not affect the item.
    /// </summary>
    public virtual async Task<TestRunItem> AddTestCaseAsync(TestRun run, Guid testCaseId, Guid? assignedUserId)
    {
        run.EnsureNotCompleted();

        var testCase = await _testCaseRepository.GetAsync(testCaseId, includeDetails: false);

        var version = testCase.Status == TestCaseStatus.Approved && testCase.CurrentVersion > 0
            ? await _versionRepository.FindAsync(v => v.TestCaseId == testCaseId && v.VersionNumber == testCase.CurrentVersion)
            : null;

        if (version == null)
        {
            throw new BusinessException(TestCaseManagementErrorCodes.TestCaseNotApproved).WithData("Code", testCase.Code);
        }

        return run.AddItem(version.Id, assignedUserId);
    }

    /// <summary>
    /// Appends a new attempt for <paramref name="runItemId"/> (attempt = highest existing attempt + 1), updates the item's
    /// current status and starts the run. Existing attempts and the master test case are never modified.
    /// </summary>
    public virtual async Task<TestExecution> RecordExecutionAttemptAsync(
        Guid runItemId, TestResultStatus status, string? actualResult, int durationSeconds)
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

        item.SetCurrentStatus(status);
        run.MarkInProgress();
        await _runRepository.UpdateAsync(run);

        // One save writes the attempt together with the new item and run status. Saving here (not at the end of the
        // unit of work) lets a following attempt in a batch, and any query in the same unit of work, see all of it.
        await _executionRepository.InsertAsync(execution, autoSave: true);

        return execution;
    }
}
