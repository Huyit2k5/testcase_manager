using Acme.TestCaseManagement.SharedSteps;
using Acme.TestCaseManagement.TestCases;
using Shouldly;
using Volo.Abp;
using Xunit;

namespace Acme.TestCaseManagement;

/// <summary>The rules of shared steps (FR-005) that need no database: the revision of a group and the link of a test case to it.</summary>
public class SharedSteps_Tests
{
    private static TestStepInput Step(string action, string expected = "Ok", string? data = null, Guid? id = null) => new(id, action, expected, data);

    private static SharedStepGroup NewGroup(params string[] actions)
    {
        var group = new SharedStepGroup(Guid.NewGuid(), null, "Log in", "The usual login");
        group.SetSteps(actions.Select(a => Step(a)).ToList());
        return group;
    }

    private static TestCase NewTestCase(params string[] ownActions)
    {
        var testCase = new TestCase(Guid.NewGuid(), null, Guid.NewGuid(), "TC-1", "A test");
        testCase.SetSteps(ownActions.Select(a => Step(a)).ToList());
        return testCase;
    }

    private static string[] Actions(TestCase testCase) => testCase.Steps.OrderBy(s => s.StepOrder).Select(s => s.Action).ToArray();

    private static int[] Orders(TestCase testCase) => testCase.Steps.OrderBy(s => s.StepOrder).Select(s => s.StepOrder).ToArray();

    // ---- the group

    [Fact]
    public void A_New_Group_Starts_At_Revision_One_And_Its_First_Steps_Do_Not_Raise_It()
    {
        var group = NewGroup("Open the login page", "Sign in");

        group.Revision.ShouldBe(1);
        group.Steps.OrderBy(s => s.StepOrder).Select(s => s.Action).ShouldBe(new[] { "Open the login page", "Sign in" });
    }

    [Fact]
    public void Changing_The_Steps_Raises_The_Revision_And_Saving_The_Same_Steps_Does_Not()
    {
        var group = NewGroup("Open the login page", "Sign in");
        var ids = group.Steps.OrderBy(s => s.StepOrder).Select(s => s.Id).ToArray();

        group.SetSteps(new[] { Step("Open the login page", id: ids[0]), Step("Sign in", id: ids[1]) }).ShouldBeFalse();
        group.Revision.ShouldBe(1);

        group.SetSteps(new[] { Step("Open the login page", id: ids[0]), Step("Sign in", "Dashboard shown", id: ids[1]) }).ShouldBeTrue();
        group.Revision.ShouldBe(2);

        group.SetSteps(new[] { Step("Open the login page", id: ids[0]) }).ShouldBeTrue();
        group.Revision.ShouldBe(3);
        group.Steps.Count.ShouldBe(1);

        group.SetSteps(new[] { Step("Sign in", id: ids[1]), Step("Open the login page", id: ids[0]) }).ShouldBeTrue();
        group.Revision.ShouldBe(4);
        group.Steps.Single(s => s.Id == ids[0]).StepOrder.ShouldBe(2);
    }

    [Fact]
    public void A_Name_Or_A_Description_Does_Not_Raise_The_Revision()
    {
        var group = NewGroup("Sign in");

        group.SetName("Log in as a customer");
        group.SetDescription("Changed");

        group.Revision.ShouldBe(1);
    }

    [Fact]
    public void A_Group_Needs_At_Least_One_Step_And_At_Most_Fifty()
    {
        var group = NewGroup("Sign in");

        Should.Throw<BusinessException>(() => group.SetSteps(Array.Empty<TestStepInput>())).Code.ShouldBe(TestCaseManagementErrorCodes.SharedStepGroupHasNoSteps);
        Should.Throw<BusinessException>(() => group.SetSteps(Enumerable.Range(0, 51).Select(i => Step($"s{i}")).ToList()))
            .Code.ShouldBe(TestCaseManagementErrorCodes.SharedStepGroupTooLarge);
        group.Revision.ShouldBe(1);
        group.Steps.Count.ShouldBe(1);
    }

    // ---- a test case that uses it

    [Fact]
    public void Inserting_Copies_The_Steps_At_The_Position_And_Links_Them_To_The_Revision()
    {
        var group = NewGroup("Open the login page", "Sign in");
        var testCase = NewTestCase("Search", "Add to cart");

        testCase.InsertSharedSteps(group, 1);

        Actions(testCase).ShouldBe(new[] { "Open the login page", "Sign in", "Search", "Add to cart" });
        Orders(testCase).ShouldBe(new[] { 1, 2, 3, 4 });
        var linked = testCase.Steps.Where(s => s.SharedStepGroupId == group.Id).ToList();
        linked.Count.ShouldBe(2);
        linked.ShouldAllBe(s => s.SharedStepRevision == 1);
        // A copy, not the step of the group.
        linked.Select(s => s.Id).ShouldNotContain(group.Steps.First().Id);
        testCase.SharedStepGroupIds().ShouldBe(new[] { group.Id });
    }

    [Theory]
    [InlineData(null, new[] { "Search", "Add to cart", "G1", "G2" })]
    [InlineData(99, new[] { "Search", "Add to cart", "G1", "G2" })]
    [InlineData(2, new[] { "Search", "G1", "G2", "Add to cart" })]
    [InlineData(0, new[] { "G1", "G2", "Search", "Add to cart" })]
    public void The_Position_Is_Clamped_Into_The_List(int? position, string[] expected)
    {
        var testCase = NewTestCase("Search", "Add to cart");

        testCase.InsertSharedSteps(NewGroup("G1", "G2"), position);

        Actions(testCase).ShouldBe(expected);
        Orders(testCase).ShouldBe(Enumerable.Range(1, 4).ToArray());
    }

    [Fact]
    public void Refreshing_Replaces_The_Copy_At_The_Same_Place_With_The_Current_Steps_And_Revision()
    {
        var group = NewGroup("Open the login page", "Sign in");
        var testCase = NewTestCase("Search");
        testCase.InsertSharedSteps(group, 1);
        testCase.SetSteps(testCase.Steps.OrderBy(s => s.StepOrder).Select(s => Step(s.Action, s.ExpectedResult, s.TestData, s.Id)).Append(Step("Pay")).ToList());
        // Search stays last-but-one: group at 1-2, then Search, then Pay.

        var ids = group.Steps.OrderBy(s => s.StepOrder).Select(s => s.Id).ToArray();
        group.SetSteps(new[] { Step("Open the login page", id: ids[0]), Step("Enter the code", id: null), Step("Sign in", id: ids[1]) });
        group.Revision.ShouldBe(2);

        testCase.RefreshSharedSteps(group);

        Actions(testCase).ShouldBe(new[] { "Open the login page", "Enter the code", "Sign in", "Search", "Pay" });
        Orders(testCase).ShouldBe(new[] { 1, 2, 3, 4, 5 });
        testCase.Steps.Where(s => s.SharedStepGroupId == group.Id).ShouldAllBe(s => s.SharedStepRevision == 2);
        testCase.Steps.Count(s => s.SharedStepGroupId == null).ShouldBe(2);
    }

    [Fact]
    public void Refreshing_Puts_The_Group_Where_Its_First_Remaining_Step_Was_Even_If_The_Steps_Were_Spread_Out()
    {
        var group = NewGroup("G1", "G2");
        var testCase = NewTestCase("A", "B");
        testCase.InsertSharedSteps(group, 2);                          // A G1 G2 B
        var g2 = testCase.Steps.Single(s => s.Action == "G2");
        testCase.ReorderSteps(testCase.Steps.OrderBy(s => s.StepOrder).Select(s => s.Id)
            .Where(id => id != g2.Id).Concat(new[] { g2.Id }).ToList());   // A G1 B G2

        testCase.RefreshSharedSteps(group);

        Actions(testCase).ShouldBe(new[] { "A", "G1", "G2", "B" });
    }

    [Fact]
    public void A_Test_Case_That_Does_Not_Use_The_Group_Cannot_Refresh_Or_Detach_It()
    {
        var testCase = NewTestCase("Search");
        var group = NewGroup("Sign in");

        Should.Throw<BusinessException>(() => testCase.RefreshSharedSteps(group)).Code.ShouldBe(TestCaseManagementErrorCodes.SharedStepsNotLinked);
        Should.Throw<BusinessException>(() => testCase.DetachSharedSteps(group.Id)).Code.ShouldBe(TestCaseManagementErrorCodes.SharedStepsNotLinked);
    }

    [Fact]
    public void Detaching_Keeps_The_Steps_And_Drops_The_Link()
    {
        var group = NewGroup("Open the login page", "Sign in");
        var testCase = NewTestCase("Search");
        testCase.InsertSharedSteps(group, null);

        testCase.DetachSharedSteps(group.Id);

        Actions(testCase).ShouldBe(new[] { "Search", "Open the login page", "Sign in" });
        testCase.Steps.ShouldAllBe(s => s.SharedStepGroupId == null && s.SharedStepRevision == null);
        testCase.SharedStepGroupIds().ShouldBeEmpty();
    }

    [Fact]
    public void Editing_A_Linked_Step_Makes_It_The_Test_Cases_Own_And_Leaving_It_Alone_Keeps_The_Link()
    {
        var group = NewGroup("Open the login page", "Sign in");
        var testCase = NewTestCase();
        testCase.InsertSharedSteps(group, null);
        var steps = testCase.Steps.OrderBy(s => s.StepOrder).ToList();

        // The same content (and a reorder of nothing) keeps the link.
        testCase.SetSteps(steps.Select(s => Step(s.Action, s.ExpectedResult, s.TestData, s.Id)).ToList());
        testCase.Steps.ShouldAllBe(s => s.SharedStepGroupId == group.Id);

        // A changed step is unlinked; the other one stays linked.
        testCase.SetSteps(new[] { Step("Open the login page", "Ok", null, steps[0].Id), Step("Sign in with a token", "Ok", null, steps[1].Id) });
        testCase.Steps.Single(s => s.Id == steps[0].Id).SharedStepGroupId.ShouldBe(group.Id);
        testCase.Steps.Single(s => s.Id == steps[1].Id).SharedStepGroupId.ShouldBeNull();
        testCase.Steps.Single(s => s.Id == steps[1].Id).SharedStepRevision.ShouldBeNull();
    }

    [Fact]
    public void A_New_Step_Is_Never_Linked_Whatever_It_Says_And_Reordering_Keeps_The_Links()
    {
        var group = NewGroup("G1", "G2");
        var testCase = NewTestCase("A");
        testCase.InsertSharedSteps(group, null);
        var ids = testCase.Steps.OrderBy(s => s.StepOrder).Select(s => s.Id).ToList();

        testCase.ReorderSteps(ids.AsEnumerable().Reverse().ToList());

        testCase.Steps.Count(s => s.SharedStepGroupId == group.Id).ShouldBe(2);
        testCase.SetSteps(testCase.Steps.OrderBy(s => s.StepOrder).Select(s => Step(s.Action, s.ExpectedResult, s.TestData, s.Id)).Append(Step("G1", "Ok")).ToList());
        testCase.Steps.Count(s => s.SharedStepGroupId == group.Id).ShouldBe(2);
    }
}
