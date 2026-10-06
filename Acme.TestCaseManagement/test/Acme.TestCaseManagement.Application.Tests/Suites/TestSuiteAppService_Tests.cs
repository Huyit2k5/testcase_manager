using Acme.TestCaseManagement.Suites.Dtos;
using Acme.TestCaseManagement.TestCases;
using Acme.TestCaseManagement.TestCases.Dtos;
using Shouldly;
using Volo.Abp;
using Xunit;

namespace Acme.TestCaseManagement.Suites;

public class TestSuiteAppService_Tests : TestCaseManagementApplicationTestBase
{
    private readonly ITestSuiteAppService _suites;
    private readonly ITestCaseAppService _testCases;

    public TestSuiteAppService_Tests()
    {
        _suites = GetRequiredService<ITestSuiteAppService>();
        _testCases = GetRequiredService<ITestCaseAppService>();
    }

    private Task<TestSuiteDto> CreateAsync(string name, Guid? parentId = null) =>
        _suites.CreateAsync(new CreateTestSuiteDto { Name = name, ParentId = parentId });

    [Fact]
    public async Task Child_Suite_Should_Appear_Nested_Under_Its_Parent_In_The_Tree()
    {
        var orders = await CreateAsync("Orders");
        var checkout = await CreateAsync("Checkout", orders.Id);
        var payment = await CreateAsync("Payment", checkout.Id);
        await CreateAsync("Auth");

        var tree = await _suites.GetTreeAsync();

        tree.Select(x => x.Name).ShouldBe(new[] { "Orders", "Auth" });
        var ordersNode = tree.Single(x => x.Id == orders.Id);
        ordersNode.Children.Single().Id.ShouldBe(checkout.Id);
        ordersNode.Children.Single().Children.Single().Id.ShouldBe(payment.Id);
        checkout.ParentId.ShouldBe(orders.Id);
    }

    [Fact]
    public async Task Tree_Should_Report_Direct_Test_Case_Counts()
    {
        var suite = await CreateAsync("Login");
        await _testCases.CreateAsync(NewTestCase(suite.Id, "TC-1"));
        await _testCases.CreateAsync(NewTestCase(suite.Id, "TC-2"));

        var tree = await _suites.GetTreeAsync();

        tree.Single(x => x.Id == suite.Id).TestCaseCount.ShouldBe(2);
    }

    [Fact]
    public async Task Move_Should_Reject_A_Circular_Hierarchy_And_Keep_The_Tree_Intact()
    {
        var a = await CreateAsync("A");
        var b = await CreateAsync("B", a.Id);

        var exception = await Should.ThrowAsync<BusinessException>(
            () => _suites.MoveAsync(a.Id, new MoveTestSuiteDto { NewParentId = b.Id, NewOrder = 0 }));

        exception.Code.ShouldBe(TestCaseManagementErrorCodes.CircularSuiteDependency);
        (await _suites.GetAsync(a.Id)).ParentId.ShouldBeNull();
    }

    [Fact]
    public async Task Move_Should_Support_Drag_And_Drop_Reordering()
    {
        var root = await CreateAsync("Root");
        await CreateAsync("A", root.Id);
        await CreateAsync("B", root.Id);
        var c = await CreateAsync("C", root.Id);

        await _suites.MoveAsync(c.Id, new MoveTestSuiteDto { NewParentId = root.Id, NewOrder = 0 });

        var children = (await _suites.GetTreeAsync()).Single(x => x.Id == root.Id).Children;
        children.Select(x => x.Name).ShouldBe(new[] { "C", "A", "B" });
        children.Select(x => x.Order).ShouldBe(new[] { 0, 1, 2 });
    }

    [Fact]
    public async Task Delete_Should_Reject_A_Suite_That_Still_Has_Children_Or_Test_Cases()
    {
        var parent = await CreateAsync("Parent");
        var child = await CreateAsync("Child", parent.Id);
        await _testCases.CreateAsync(NewTestCase(child.Id, "TC-X"));

        (await Should.ThrowAsync<BusinessException>(() => _suites.DeleteAsync(parent.Id)))
            .Code.ShouldBe(TestCaseManagementErrorCodes.SuiteNotEmpty);
        (await Should.ThrowAsync<BusinessException>(() => _suites.DeleteAsync(child.Id)))
            .Code.ShouldBe(TestCaseManagementErrorCodes.SuiteNotEmpty);
    }

    [Fact]
    public async Task Delete_Should_Soft_Delete_An_Empty_Suite()
    {
        var suite = await CreateAsync("Temp");

        await _suites.DeleteAsync(suite.Id);

        (await _suites.GetTreeAsync()).ShouldBeEmpty();
    }

    [Fact]
    public async Task Update_Should_Change_Name_And_Description()
    {
        var suite = await CreateAsync("Old");

        var updated = await _suites.UpdateAsync(suite.Id, new UpdateTestSuiteDto { Name = "New", Description = "Desc" });

        updated.Name.ShouldBe("New");
        updated.Description.ShouldBe("Desc");
    }

    private static CreateUpdateTestCaseDto NewTestCase(Guid suiteId, string code) => new()
    {
        SuiteId = suiteId,
        Code = code,
        Title = code,
        Steps = { new TestStepDto { Action = "Do", ExpectedResult = "Done" } },
    };
}
