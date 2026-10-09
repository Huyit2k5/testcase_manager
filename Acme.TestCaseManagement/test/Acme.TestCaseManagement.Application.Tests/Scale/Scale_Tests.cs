using System.Diagnostics;
using Acme.TestCaseManagement.EntityFrameworkCore;
using Acme.TestCaseManagement.Enums;
using Acme.TestCaseManagement.Insights;
using Acme.TestCaseManagement.Insights.Dtos;
using Acme.TestCaseManagement.Plans;
using Acme.TestCaseManagement.Plans.Dtos;
using Acme.TestCaseManagement.QualityGates;
using Acme.TestCaseManagement.QualityGates.Dtos;
using Acme.TestCaseManagement.Runs;
using Acme.TestCaseManagement.Suites;
using Acme.TestCaseManagement.TestCases;
using Acme.TestCaseManagement.TestCases.Dtos;
using Microsoft.EntityFrameworkCore;
using Shouldly;
using Volo.Abp.EntityFrameworkCore;
using Xunit;
using Xunit.Abstractions;

namespace Acme.TestCaseManagement;

/// <summary>
/// How the screens that read a lot behave on a large data set. It does nothing unless the environment variable <c>TCM_SCALE</c> is set
/// (to a number of test cases, for example 10000); a run then has 1/10 as many runs, each scheduling a tenth of the test cases, so that
/// 10,000 test cases give 100 runs, 100,000 run items and 200,000 attempts. Meant for a MySQL (or other server) database, see
/// <c>TestCaseManagementTestBaseModule</c>; it prints the time of each operation instead of asserting a budget.
/// </summary>
public class Scale_Tests : TestCaseManagementApplicationTestBase
{
    private readonly ITestOutputHelper _output;

    public Scale_Tests(ITestOutputHelper output)
    {
        _output = output;
    }

    /// <summary>The line goes to the test output, and at once to the file named by TCM_SCALE_LOG (test output only shows when the test ends).</summary>
    private void Log(string line)
    {
        _output.WriteLine(line);
        if (Environment.GetEnvironmentVariable("TCM_SCALE_LOG") is { Length: > 0 } path)
        {
            File.AppendAllText(path, line + Environment.NewLine);
        }
    }

    private static void Set(object target, string property, object? value) =>
        target.GetType().GetProperty(property)!.SetValue(target, value);

    [Fact]
    public async Task The_Reading_Screens_On_A_Large_Data_Set()
    {
        if (!int.TryParse(Environment.GetEnvironmentVariable("TCM_SCALE"), out var testCaseCount))
        {
            return;
        }

        var runCount = Math.Max(1, testCaseCount / 100);
        var itemsPerRun = Math.Max(1, testCaseCount / 10);

        var plan = await GetRequiredService<ITestPlanAppService>().CreateAsync(new CreateTestPlanDto { Name = "Scale", StartDate = DateTime.Today.AddDays(-10), EndDate = DateTime.Today.AddDays(10) });
        var timer = Stopwatch.StartNew();

        // Many small units of work, not one: ABP matches every entity of a unit of work against the others when it ends, which is
        // quadratic and would make this seeding, not the module, the thing that is slow.
        var versionIds = new List<Guid>(testCaseCount);
        var suiteId = Guid.NewGuid();
        await WithUnitOfWorkAsync(async () =>
        {
            var db = await GetRequiredService<IDbContextProvider<ITestCaseManagementDbContext>>().GetDbContextAsync();
            var scaleSuite = new TestSuite(suiteId, null, "Scale", null, 1);
            scaleSuite.SetProject(plan.ProjectId);
            db.TestSuites.Add(scaleSuite);
            await db.SaveChangesAsync();
        });

        for (var batch = 0; batch < testCaseCount; batch += 1000)
        {
            await WithUnitOfWorkAsync(async () =>
            {
                var db = await GetRequiredService<IDbContextProvider<ITestCaseManagementDbContext>>().GetDbContextAsync();
                for (var i = batch; i < Math.Min(batch + 1000, testCaseCount); i++)
                {
                    var testCase = new TestCase(Guid.NewGuid(), null, suiteId, $"SC-{i:000000}", $"Scale test case {i}");
                    Set(testCase, nameof(TestCase.Status), TestCaseStatus.Approved);
                    Set(testCase, nameof(TestCase.CurrentVersion), 1);
                    Set(testCase, nameof(TestCase.AutomationId), $"scale.{i}");
                    var version = new TestCaseVersion(Guid.NewGuid(), null, testCase.Id, 1, testCase.Title, null, "[]", null, null);
                    versionIds.Add(version.Id);
                    db.TestCases.Add(testCase);
                    db.TestCaseVersions.Add(version);
                }

                await db.SaveChangesAsync();
            });
        }

        Log($"{testCaseCount} test cases and versions: {timer.Elapsed.TotalSeconds:0.0}s");

        var random = new Random(42);
        for (var r = 0; r < runCount; r++)
        {
            await WithUnitOfWorkAsync(async () =>
            {
                var db = await GetRequiredService<IDbContextProvider<ITestCaseManagementDbContext>>().GetDbContextAsync();
                var run = new TestRun(Guid.NewGuid(), null, $"Scale run {r}", r % 2 == 0 ? "Staging" : "Production", plan.Id);
                run.SetProject(plan.ProjectId);
                var start = random.Next(versionIds.Count - itemsPerRun + 1);
                var items = new List<TestRunItem>();
                for (var i = 0; i < itemsPerRun; i++)
                {
                    items.Add(run.AddItem(versionIds[start + i], null));
                }

                var executions = new List<TestExecution>(items.Count * 2);
                foreach (var item in items)
                {
                    var finalStatus = random.Next(10) switch { < 7 => TestResultStatus.Passed, < 9 => TestResultStatus.Failed, _ => TestResultStatus.Blocked };
                    var attempts = random.Next(3) == 0 ? 2 : 1;
                    for (var a = 1; a <= attempts; a++)
                    {
                        executions.Add(new TestExecution(Guid.NewGuid(), null, item.Id, a, a == attempts ? finalStatus : TestResultStatus.Failed, null, 5));
                    }

                    Set(item, nameof(TestRunItem.CurrentStatus), finalStatus);
                }

                db.TestRuns.Add(run);
                db.TestExecutions.AddRange(executions);
                await db.SaveChangesAsync();
            });
        }

        Log($"{runCount} runs of {itemsPerRun} items and their attempts: {timer.Elapsed.TotalSeconds:0.0}s since the start");

        async Task<T> Measure<T>(string name, Func<Task<T>> action)
        {
            var started = Stopwatch.StartNew();
            var result = await action();
            Log($"{name}: {started.Elapsed.TotalMilliseconds:0} ms");
            return result;
        }

        var dashboard = GetRequiredService<IDashboardAppService>();
        var flaky = GetRequiredService<IFlakyTestAppService>();
        var testCases = GetRequiredService<ITestCaseAppService>();
        var gates = GetRequiredService<IQualityGateAppService>();

        // Where the time of the dashboard goes: the queries, or the arithmetic on what they return.
        var repository = GetRequiredService<Repositories.IInsightsRepository>();
        var data = await WithUnitOfWorkAsync(() => Measure("  insights queries only (items, attempts, defects)", () => repository.GetScopeDataAsync(null, DateTime.Now.AddDays(-30))));
        var arithmetic = Stopwatch.StartNew();
        DashboardCalculator.Progress(data.Items);
        DashboardCalculator.Velocity(data.Attempts, data.Items, DateTime.Today, 14);
        DashboardCalculator.BurnDown(data.Items, DateTime.Today.AddDays(-13), null, DateTime.Today);
        DashboardCalculator.DefectDensity(data.Items, data.Defects);
        Log($"  calculators of the dashboard: {arithmetic.ElapsedMilliseconds} ms");
        arithmetic.Restart();
        FlakinessCalculator.CalculateAll(data.Attempts, new FlakinessSettings());
        Log($"  flakiness calculator: {arithmetic.ElapsedMilliseconds} ms ({data.Items.Count} items, {data.Attempts.Count} attempts)");

        var all = await Measure("dashboard of every run, 14 days", () => dashboard.GetAsync(new GetDashboardInput { Days = 14 }));
        var scoped = await Measure("dashboard of one plan, 14 days", () => dashboard.GetAsync(new GetDashboardInput { TestPlanId = plan.Id, Days = 14 }));
        await Measure("dashboard of every run, 90 days", () => dashboard.GetAsync(new GetDashboardInput { Days = 90 }));
        await Measure("list of flaky tests", () => flaky.GetListAsync(new GetFlakyTestsInput()));
        await Measure("test cases, first page by code", () => testCases.GetListAsync(new GetTestCaseListInput { MaxResultCount = 50 }));
        await Measure("test cases, filter on text", () => testCases.GetListAsync(new GetTestCaseListInput { Filter = "case 77", MaxResultCount = 50 }));
        await Measure("test cases, page 100 by status", () => testCases.GetListAsync(new GetTestCaseListInput { Sorting = "Status", SkipCount = 5000, MaxResultCount = 50 }));
        await Measure("test cases, with an automation id", () => testCases.GetListAsync(new GetTestCaseListInput { HasAutomationId = true, MaxResultCount = 50 }));
        await Measure("quality gate evaluation of the plan", () => gates.EvaluateAsync(new EvaluateQualityGateInput { TestPlanId = plan.Id }));

        all.Progress.TotalItems.ShouldBe(runCount * itemsPerRun);
        scoped.Progress.TotalItems.ShouldBe(runCount * itemsPerRun);
    }
}
