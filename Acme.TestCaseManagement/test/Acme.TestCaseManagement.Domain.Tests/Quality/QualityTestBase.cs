using Acme.TestCaseManagement.Enums;
using Acme.TestCaseManagement.Plans;
using Acme.TestCaseManagement.Projects;
using Acme.TestCaseManagement.Runs;
using Acme.TestCaseManagement.Suites;
using Acme.TestCaseManagement.TestCases;
using Volo.Abp.Domain.Repositories;

namespace Acme.TestCaseManagement.Quality;

/// <summary>Builds plans, runs and results with the domain services, for the quality gate and sign-off tests.</summary>
public abstract class QualityTestBase : TestCaseManagementDomainTestBase
{
    protected TestSuiteManager SuiteManager => GetRequiredService<TestSuiteManager>();
    protected TestCaseManager TestCaseManager => GetRequiredService<TestCaseManager>();
    protected TestRunManager RunManager => GetRequiredService<TestRunManager>();
    protected DefectLinkManager DefectManager => GetRequiredService<DefectLinkManager>();
    protected IRepository<TestSuite, Guid> SuiteRepository => GetRequiredService<IRepository<TestSuite, Guid>>();
    protected IRepository<TestCase, Guid> TestCaseRepository => GetRequiredService<IRepository<TestCase, Guid>>();
    protected IRepository<TestPlan, Guid> PlanRepository => GetRequiredService<IRepository<TestPlan, Guid>>();
    protected IRepository<TestRun, Guid> RunRepository => GetRequiredService<IRepository<TestRun, Guid>>();

    protected async Task<TestCase> ApprovedTestCaseAsync(string code, PriorityLevel priority = PriorityLevel.Medium)
    {
        var suite = (await SuiteRepository.GetListAsync()).FirstOrDefault()
                    ?? await SuiteRepository.InsertAsync(await SuiteManager.CreateAsync("Library", null), autoSave: true);

        var testCase = await TestCaseManager.CreateAsync(suite.Id, code, $"Title of {code}");
        testCase.SetDetails(
            null, null, null, priority, SeverityLevel.Medium,
            ExecutionType.Manual, TestKind.Functional, TestLayer.Acceptance, null);
        testCase.SetSteps(new[] { new TestStepInput(null, "Act", "Expect", null) });
        await TestCaseRepository.InsertAsync(testCase, autoSave: true);

        await TestCaseManager.ApproveAsync(testCase, null);
        await SaveChangesAsync();
        return testCase;
    }

    protected async Task<TestPlan> PlanAsync(string name, Guid? milestoneId = null)
    {
        var plan = new TestPlan(Guid.NewGuid(), null, name, milestoneId: milestoneId);
        plan.SetProject((await GetRequiredService<ProjectManager>().GetOrCreateDefaultAsync()).Id);
        return await PlanRepository.InsertAsync(plan, autoSave: true);
    }

    protected async Task<TestRun> RunAsync(TestPlan? plan, string environment = "Staging", params TestCase[] testCases)
    {
        var run = await RunManager.CreateRunAsync($"Run {Guid.NewGuid():N}", environment, plan?.Id);
        foreach (var testCase in testCases)
        {
            await RunManager.AddTestCaseAsync(run, testCase.Id, null);
        }

        return await RunRepository.InsertAsync(run, autoSave: true);
    }

    /// <summary>Records an attempt for the run item at the given position (one-based, in the order the test cases were added).</summary>
    protected async Task<TestExecution> ExecuteAsync(TestRun run, int position, TestResultStatus status)
    {
        var item = run.Items.Single(i => i.Sequence == position);
        return await RunManager.RecordExecutionAttemptAsync(item.Id, status, null, 1);
    }

    /// <summary>Convenience: a plan with one run holding the given test cases, already executed with the given results.</summary>
    protected async Task<(TestPlan Plan, TestRun Run)> ExecutedPlanAsync(
        string planName, params (TestCase TestCase, TestResultStatus? Status)[] cases)
    {
        var plan = await PlanAsync(planName);
        var run = await RunAsync(plan, "Staging", cases.Select(c => c.TestCase).ToArray());

        for (var i = 0; i < cases.Length; i++)
        {
            if (cases[i].Status.HasValue)
            {
                await ExecuteAsync(run, i + 1, cases[i].Status!.Value);
            }
        }

        return (plan, run);
    }
}
