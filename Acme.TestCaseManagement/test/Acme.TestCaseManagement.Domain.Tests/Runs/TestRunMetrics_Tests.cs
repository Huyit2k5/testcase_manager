using Acme.TestCaseManagement.Enums;
using Shouldly;
using Xunit;

namespace Acme.TestCaseManagement.Runs;

public class TestRunMetrics_Tests
{
    private static TestRunItem Item(TestResultStatus status)
    {
        var run = new TestRun(Guid.NewGuid(), null, "Run", "Staging");
        var item = run.AddItem(Guid.NewGuid(), null);
        item.SetCurrentStatus(status);
        return item;
    }

    private static TestExecution Attempt(TestRunItem item, int attempt, TestResultStatus status) =>
        new(Guid.NewGuid(), null, item.Id, attempt, status, null, 1);

    [Fact]
    public void Empty_Run_Should_Have_No_Completion_And_No_First_Time_Pass_Rate()
    {
        var metrics = TestRunMetrics.Calculate(Array.Empty<TestRunItem>(), Array.Empty<TestExecution>());

        metrics.TotalItems.ShouldBe(0);
        metrics.CompletionPercentage.ShouldBe(0);
        metrics.FirstTimePassRate.ShouldBeNull();
    }

    [Fact]
    public void Should_Count_Items_By_Current_Status_And_Compute_Completion()
    {
        var items = new[]
        {
            Item(TestResultStatus.Passed), Item(TestResultStatus.Failed), Item(TestResultStatus.Blocked),
            Item(TestResultStatus.Skipped), Item(TestResultStatus.Untested), Item(TestResultStatus.Untested),
        };

        var metrics = TestRunMetrics.Calculate(items, Array.Empty<TestExecution>());

        metrics.TotalItems.ShouldBe(6);
        (metrics.Passed, metrics.Failed, metrics.Blocked, metrics.Skipped, metrics.Untested).ShouldBe((1, 1, 1, 1, 2));
        metrics.ExecutedItems.ShouldBe(4);
        metrics.CompletionPercentage.ShouldBe(66.67);
        metrics.FirstTimePassRate.ShouldBeNull();
    }

    [Fact]
    public void First_Time_Pass_Rate_Should_Use_Only_The_First_Attempt_Of_Each_Executed_Item()
    {
        var passedFirst = Item(TestResultStatus.Passed);
        var failedThenPassed = Item(TestResultStatus.Passed);
        var failedTwice = Item(TestResultStatus.Failed);
        var neverRun = Item(TestResultStatus.Untested);

        var executions = new[]
        {
            Attempt(passedFirst, 1, TestResultStatus.Passed),
            Attempt(failedThenPassed, 1, TestResultStatus.Failed),
            Attempt(failedThenPassed, 2, TestResultStatus.Passed),
            Attempt(failedTwice, 1, TestResultStatus.Failed),
            Attempt(failedTwice, 2, TestResultStatus.Failed),
        };

        var metrics = TestRunMetrics.Calculate(new[] { passedFirst, failedThenPassed, failedTwice, neverRun }, executions);

        // 1 of the 3 executed items passed on its first attempt; the unexecuted item is not counted.
        metrics.FirstTimePassRate.ShouldBe(33.33);
        metrics.Passed.ShouldBe(2);
    }
}
