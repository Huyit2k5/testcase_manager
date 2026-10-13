using Acme.TestCaseManagement.Enums;
using Acme.TestCaseManagement.Runs;
using Acme.TestCaseManagement.Runs.Dtos;
using Acme.TestCaseManagement.Suites;
using Acme.TestCaseManagement.Suites.Dtos;
using Acme.TestCaseManagement.TestCases;
using Acme.TestCaseManagement.TestCases.Dtos;
using Shouldly;
using Volo.Abp;
using Xunit;

namespace Acme.TestCaseManagement;

/// <summary>The result of single steps in an attempt: what is accepted, what is refused, and what is read back.</summary>
public class TestStepResults_Tests : TestCaseManagementApplicationTestBase
{
    static TestStepResults_Tests()
    {
        System.Globalization.CultureInfo.DefaultThreadCurrentCulture = new System.Globalization.CultureInfo("en");
        System.Globalization.CultureInfo.DefaultThreadCurrentUICulture = new System.Globalization.CultureInfo("en");
    }

    private readonly ITestSuiteAppService _suites;
    private readonly ITestCaseAppService _testCases;
    private readonly ITestRunAppService _runs;

    public TestStepResults_Tests()
    {
        _suites = GetRequiredService<ITestSuiteAppService>();
        _testCases = GetRequiredService<ITestCaseAppService>();
        _runs = GetRequiredService<ITestRunAppService>();
    }

    private async Task<(TestCaseDto TestCase, TestRunDto Run, TestRunItemDto Item)> RunOfThreeStepsAsync(string code)
    {
        var suite = await _suites.CreateAsync(new CreateTestSuiteDto { Name = "Library " + code });
        var created = await _testCases.CreateAsync(new CreateUpdateTestCaseDto
        {
            SuiteId = suite.Id, Code = code, Title = "Pay",
            Steps =
            {
                new TestStepDto { Action = "Open the cart", ExpectedResult = "The cart opens" },
                new TestStepDto { Action = "Enter the card", ExpectedResult = "The card is accepted" },
                new TestStepDto { Action = "Press Pay", ExpectedResult = "The order is paid" },
            },
        });
        var approved = await _testCases.ChangeStatusAsync(created.Id, new ChangeTestCaseStatusDto { TargetStatus = TestCaseStatus.Approved });
        var run = await _runs.CreateAsync(new CreateTestRunDto { Title = "Run " + code, Environment = "Staging", TestCaseIds = { approved.Id } });
        return (approved, run, run.Items.Single());
    }

    private static StepResultInputDto Step(TestCaseDto testCase, int position, TestResultStatus status, string? actual = null) =>
        new() { StepId = testCase.Steps[position - 1].Id!.Value, Status = status, ActualResult = actual };

    [Fact]
    public async Task The_Results_Of_Steps_Are_Recorded_With_The_Attempt_And_Read_Back_In_The_Order_Of_The_Steps()
    {
        var (testCase, run, item) = await RunOfThreeStepsAsync("STEP-1");

        var attempt = await _runs.ExecuteItemAsync(run.Id, item.Id, new ExecuteTestItemDto
        {
            Status = TestResultStatus.Failed, ActualResult = "Payment refused", DurationSeconds = 20,
            StepResults =
            {
                Step(testCase, 3, TestResultStatus.Failed, "Error 402"),
                Step(testCase, 1, TestResultStatus.Passed),
                Step(testCase, 2, TestResultStatus.Passed, "Visa accepted"),
            },
        });
        attempt.StepResults.Count.ShouldBe(3);

        var read = (await _runs.GetExecutionsAsync(run.Id, item.Id)).Single();
        read.StepResults.Select(r => (r.StepOrder, r.Status, r.ActualResult)).ShouldBe(new (int, TestResultStatus, string?)[]
        {
            (1, TestResultStatus.Passed, null), (2, TestResultStatus.Passed, "Visa accepted"), (3, TestResultStatus.Failed, "Error 402"),
        });
        read.StepResults.Select(r => r.StepId).ShouldBe(testCase.Steps.Select(s => s.Id!.Value));
    }

    [Fact]
    public async Task Only_The_Steps_That_Were_Run_Need_A_Result_And_An_Attempt_Without_Any_Still_Works()
    {
        var (testCase, run, item) = await RunOfThreeStepsAsync("STEP-2");

        await _runs.ExecuteItemAsync(run.Id, item.Id, new ExecuteTestItemDto { Status = TestResultStatus.Blocked, StepResults = { Step(testCase, 1, TestResultStatus.Passed), Step(testCase, 2, TestResultStatus.Blocked, "Card service is down") } });
        await _runs.ExecuteItemAsync(run.Id, item.Id, new ExecuteTestItemDto { Status = TestResultStatus.Passed });

        var attempts = await _runs.GetExecutionsAsync(run.Id, item.Id);
        attempts.Select(a => a.StepResults.Count).ShouldBe(new[] { 2, 0 });
    }

    [Fact]
    public async Task A_Re_Test_Keeps_The_Step_Results_Of_Each_Attempt_On_Their_Own()
    {
        var (testCase, run, item) = await RunOfThreeStepsAsync("STEP-3");

        await _runs.ExecuteItemAsync(run.Id, item.Id, new ExecuteTestItemDto { Status = TestResultStatus.Failed, StepResults = { Step(testCase, 2, TestResultStatus.Failed) } });
        await _runs.ExecuteItemAsync(run.Id, item.Id, new ExecuteTestItemDto { Status = TestResultStatus.Passed, StepResults = { Step(testCase, 2, TestResultStatus.Passed) } });

        var attempts = await _runs.GetExecutionsAsync(run.Id, item.Id);
        attempts.Select(a => a.StepResults.Single().Status).ShouldBe(new[] { TestResultStatus.Failed, TestResultStatus.Passed });
    }

    [Fact]
    public async Task An_Attempt_Cannot_Pass_While_A_Step_Failed_Or_Was_Blocked_And_Nothing_Is_Recorded()
    {
        var (testCase, run, item) = await RunOfThreeStepsAsync("STEP-4");

        foreach (var bad in new[] { TestResultStatus.Failed, TestResultStatus.Blocked })
        {
            var error = await Should.ThrowAsync<BusinessException>(() => _runs.ExecuteItemAsync(run.Id, item.Id, new ExecuteTestItemDto
            {
                Status = TestResultStatus.Passed, StepResults = { Step(testCase, 1, TestResultStatus.Passed), Step(testCase, 2, bad) },
            }));
            error.Code.ShouldBe(TestCaseManagementErrorCodes.PassedWithUnpassedStep);
        }

        (await _runs.GetExecutionsAsync(run.Id, item.Id)).ShouldBeEmpty();
        (await _runs.GetAsync(run.Id)).Items.Single().CurrentStatus.ShouldBe(TestResultStatus.Untested);
    }

    [Fact]
    public async Task A_Step_That_Is_Skipped_Does_Not_Stop_The_Attempt_From_Passing()
    {
        var (testCase, run, item) = await RunOfThreeStepsAsync("STEP-5");

        var attempt = await _runs.ExecuteItemAsync(run.Id, item.Id, new ExecuteTestItemDto
        {
            Status = TestResultStatus.Passed, StepResults = { Step(testCase, 1, TestResultStatus.Passed), Step(testCase, 2, TestResultStatus.Skipped) },
        });

        attempt.Status.ShouldBe(TestResultStatus.Passed);
    }

    [Fact]
    public async Task A_Step_Of_Another_Test_Case_Or_A_Step_Given_Twice_Or_A_Step_Without_A_Result_Is_Refused()
    {
        var (testCase, run, item) = await RunOfThreeStepsAsync("STEP-6");
        var (other, _, _) = await RunOfThreeStepsAsync("STEP-7");

        (await Should.ThrowAsync<BusinessException>(() => _runs.ExecuteItemAsync(run.Id, item.Id, new ExecuteTestItemDto
        {
            Status = TestResultStatus.Failed, StepResults = { Step(other, 1, TestResultStatus.Failed) },
        }))).Code.ShouldBe(TestCaseManagementErrorCodes.TestStepNotInVersion);

        (await Should.ThrowAsync<BusinessException>(() => _runs.ExecuteItemAsync(run.Id, item.Id, new ExecuteTestItemDto
        {
            Status = TestResultStatus.Failed, StepResults = { Step(testCase, 1, TestResultStatus.Passed), Step(testCase, 1, TestResultStatus.Failed) },
        }))).Code.ShouldBe(TestCaseManagementErrorCodes.DuplicateStepResult);

        (await Should.ThrowAsync<BusinessException>(() => _runs.ExecuteItemAsync(run.Id, item.Id, new ExecuteTestItemDto
        {
            Status = TestResultStatus.Failed, StepResults = { Step(testCase, 1, TestResultStatus.Untested) },
        }))).Code.ShouldBe(TestCaseManagementErrorCodes.InvalidExecutionStatus);

        (await _runs.GetExecutionsAsync(run.Id, item.Id)).ShouldBeEmpty();
    }

    [Fact]
    public async Task The_Steps_Are_Those_Of_The_Version_Being_Run_Not_Of_The_Test_Case_As_It_Is_Now()
    {
        var (testCase, run, item) = await RunOfThreeStepsAsync("STEP-8");
        var stepOne = testCase.Steps[0].Id!.Value;

        // The test case is edited and approved again: a step is added and the first is removed. The run still has version 1.
        var edit = new CreateUpdateTestCaseDto { SuiteId = testCase.SuiteId, Code = testCase.Code, Title = testCase.Title, Steps = testCase.Steps.Skip(1).ToList() };
        edit.Steps.Add(new TestStepDto { Action = "New", ExpectedResult = "New" });
        var edited = await _testCases.UpdateAsync(testCase.Id, edit);
        await _testCases.ChangeStatusAsync(testCase.Id, new ChangeTestCaseStatusDto { TargetStatus = TestCaseStatus.Approved });

        var attempt = await _runs.ExecuteItemAsync(run.Id, item.Id, new ExecuteTestItemDto
        {
            Status = TestResultStatus.Failed, StepResults = { new StepResultInputDto { StepId = stepOne, Status = TestResultStatus.Failed } },
        });
        attempt.StepResults.Single().StepOrder.ShouldBe(1);

        (await Should.ThrowAsync<BusinessException>(() => _runs.ExecuteItemAsync(run.Id, item.Id, new ExecuteTestItemDto
        {
            Status = TestResultStatus.Failed, StepResults = { new StepResultInputDto { StepId = edited.Steps.Last().Id!.Value, Status = TestResultStatus.Failed } },
        }))).Code.ShouldBe(TestCaseManagementErrorCodes.TestStepNotInVersion);
    }

    [Fact]
    public async Task A_Batch_Takes_The_Step_Results_Of_Each_Entry_And_Records_None_When_One_Is_Refused()
    {
        var (testCase, run, item) = await RunOfThreeStepsAsync("STEP-9");

        await Should.ThrowAsync<BusinessException>(() => _runs.BatchExecuteAsync(run.Id, new BatchExecuteTestItemsDto
        {
            Items =
            {
                new BatchExecuteTestItemDto { TestRunItemId = item.Id, Status = TestResultStatus.Failed, StepResults = { Step(testCase, 1, TestResultStatus.Failed) } },
                new BatchExecuteTestItemDto { TestRunItemId = item.Id, Status = TestResultStatus.Passed, StepResults = { Step(testCase, 1, TestResultStatus.Failed) } },
            },
        }));
        (await _runs.GetExecutionsAsync(run.Id, item.Id)).ShouldBeEmpty();

        var recorded = await _runs.BatchExecuteAsync(run.Id, new BatchExecuteTestItemsDto
        {
            Items = { new BatchExecuteTestItemDto { TestRunItemId = item.Id, Status = TestResultStatus.Failed, StepResults = { Step(testCase, 1, TestResultStatus.Failed, "Broken") } } },
        });
        recorded.Single().StepResults.Single().ActualResult.ShouldBe("Broken");
        (await _runs.GetExecutionsAsync(run.Id, item.Id)).Single().StepResults.Single().ActualResult.ShouldBe("Broken");
    }
}
