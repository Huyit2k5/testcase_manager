using Acme.TestCaseManagement.Enums;
using Shouldly;
using Xunit;

namespace Acme.TestCaseManagement.Requirements;

/// <summary>Executable form of the rules in plan.md, section 4.4.</summary>
public class RequirementCoverageCalculator_Tests
{
    private const TestResultStatus P = TestResultStatus.Passed;
    private const TestResultStatus F = TestResultStatus.Failed;
    private const TestResultStatus B = TestResultStatus.Blocked;
    private const TestResultStatus S = TestResultStatus.Skipped;
    private const TestResultStatus U = TestResultStatus.Untested;

    private static readonly Guid TestCaseId = Guid.NewGuid();
    private static readonly DateTime T0 = new(2026, 10, 1, 8, 0, 0, DateTimeKind.Utc);

    private static LatestTestResult Result(string environment, TestResultStatus status, int minutes) =>
        new(TestCaseId, environment, status, T0.AddMinutes(minutes), Guid.NewGuid(), Guid.NewGuid(), 1);

    // ---- requirement status ------------------------------------------------------------------------------

    [Fact]
    public void No_Linked_Test_Cases_Means_Uncovered()
    {
        RequirementCoverageCalculator.CalculateRequirementStatus(Array.Empty<TestResultStatus>())
            .ShouldBe(RequirementCoverageStatus.Uncovered);
    }

    [Theory]
    // single test case
    [InlineData(new[] { P }, RequirementCoverageStatus.Passed)]
    [InlineData(new[] { F }, RequirementCoverageStatus.Failed)]
    [InlineData(new[] { B }, RequirementCoverageStatus.Blocked)]
    [InlineData(new[] { U }, RequirementCoverageStatus.NotRun)]
    [InlineData(new[] { S }, RequirementCoverageStatus.NotRun)]
    // Failed wins over everything
    [InlineData(new[] { P, F }, RequirementCoverageStatus.Failed)]
    [InlineData(new[] { U, F }, RequirementCoverageStatus.Failed)]
    [InlineData(new[] { B, F, P, U }, RequirementCoverageStatus.Failed)]
    // NotRun wins over Blocked and Passed (cannot be evaluated until everything ran)
    [InlineData(new[] { P, U }, RequirementCoverageStatus.NotRun)]
    [InlineData(new[] { B, U }, RequirementCoverageStatus.NotRun)]
    // Blocked wins over Passed once everything has been executed
    [InlineData(new[] { P, B }, RequirementCoverageStatus.Blocked)]
    [InlineData(new[] { P, P, B, S }, RequirementCoverageStatus.Blocked)]
    // Passed needs every test case to pass; skipped is neutral but cannot be the only evidence
    [InlineData(new[] { P, P, P }, RequirementCoverageStatus.Passed)]
    [InlineData(new[] { P, S }, RequirementCoverageStatus.Passed)]
    [InlineData(new[] { S, S }, RequirementCoverageStatus.NotRun)]
    public void Requirement_Status_Follows_The_Precedence_Table(TestResultStatus[] testCaseStatuses, RequirementCoverageStatus expected)
    {
        RequirementCoverageCalculator.CalculateRequirementStatus(testCaseStatuses).ShouldBe(expected);
    }

    [Fact]
    public void Requirement_Status_Does_Not_Depend_On_The_Order_Of_The_Test_Cases()
    {
        var statuses = new[] { P, S, B, U, F };

        foreach (var permutation in Permutations(statuses))
        {
            RequirementCoverageCalculator.CalculateRequirementStatus(permutation).ShouldBe(RequirementCoverageStatus.Failed);
        }
    }

    // ---- test case status --------------------------------------------------------------------------------

    [Fact]
    public void A_Test_Case_Without_Executed_Results_Is_Untested()
    {
        RequirementCoverageCalculator.CalculateTestCaseStatus(Array.Empty<LatestTestResult>()).ShouldBe(U);
    }

    [Fact]
    public void Within_One_Environment_The_Most_Recent_Result_Wins()
    {
        var results = new[]
        {
            Result("Staging", F, minutes: 0),
            Result("Staging", P, minutes: 30), // re-tested later, after the fix
        };

        RequirementCoverageCalculator.CalculateTestCaseStatus(results).ShouldBe(P);
    }

    [Fact]
    public void An_Older_Pass_Does_Not_Hide_A_Newer_Failure()
    {
        var results = new[]
        {
            Result("Staging", P, minutes: 0),
            Result("Staging", F, minutes: 30),
        };

        RequirementCoverageCalculator.CalculateTestCaseStatus(results).ShouldBe(F);
    }

    [Fact]
    public void A_Failure_In_Any_Environment_Makes_The_Test_Case_Failed()
    {
        var results = new[]
        {
            Result("Chrome", P, minutes: 60),
            Result("Safari", F, minutes: 0), // older, but it is the latest result for Safari
        };

        RequirementCoverageCalculator.CalculateTestCaseStatus(results).ShouldBe(F);
    }

    [Fact]
    public void Environments_Are_Compared_Ignoring_Case()
    {
        var results = new[]
        {
            Result("Staging", F, minutes: 0),
            Result("staging", P, minutes: 10), // same environment, written differently
        };

        RequirementCoverageCalculator.CalculateTestCaseStatus(results).ShouldBe(P);
    }

    [Theory]
    [InlineData(TestResultStatus.Blocked, TestResultStatus.Passed, TestResultStatus.Blocked)]
    [InlineData(TestResultStatus.Passed, TestResultStatus.Skipped, TestResultStatus.Passed)]
    [InlineData(TestResultStatus.Skipped, TestResultStatus.Skipped, TestResultStatus.Skipped)]
    [InlineData(TestResultStatus.Failed, TestResultStatus.Blocked, TestResultStatus.Failed)]
    public void Environments_Are_Combined_Worst_First(TestResultStatus first, TestResultStatus second, TestResultStatus expected)
    {
        var results = new[] { Result("A", first, 0), Result("B", second, 0) };

        RequirementCoverageCalculator.CalculateTestCaseStatus(results).ShouldBe(expected);
    }

    [Fact]
    public void Current_Results_Keep_One_Latest_Result_Per_Environment()
    {
        var older = Result("Staging", F, 0);
        var newer = Result("Staging", P, 5);
        var other = Result("Prod", B, 1);

        var current = RequirementCoverageCalculator.CurrentResults(new[] { older, newer, other });

        current.Count.ShouldBe(2);
        current.ShouldContain(newer);
        current.ShouldContain(other);
        current.ShouldNotContain(older);
    }

    private static IEnumerable<TestResultStatus[]> Permutations(TestResultStatus[] items)
    {
        if (items.Length <= 1)
        {
            yield return items;
            yield break;
        }

        for (var i = 0; i < items.Length; i++)
        {
            var rest = items.Where((_, index) => index != i).ToArray();
            foreach (var tail in Permutations(rest))
            {
                yield return new[] { items[i] }.Concat(tail).ToArray();
            }
        }
    }
}
