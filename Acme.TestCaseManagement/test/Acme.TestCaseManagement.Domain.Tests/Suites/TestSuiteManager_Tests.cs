using Acme.TestCaseManagement.Suites;
using Shouldly;
using Volo.Abp;
using Volo.Abp.Domain.Repositories;
using Xunit;

namespace Acme.TestCaseManagement.Suites;

public class TestSuiteManager_Tests : TestCaseManagementDomainTestBase
{
    private readonly TestSuiteManager _manager;
    private readonly IRepository<TestSuite, Guid> _repository;

    public TestSuiteManager_Tests()
    {
        _manager = GetRequiredService<TestSuiteManager>();
        _repository = GetRequiredService<IRepository<TestSuite, Guid>>();
    }

    private async Task<TestSuite> CreateSuiteAsync(string name, Guid? parentId = null)
    {
        var suite = await _manager.CreateAsync(name, parentId);
        return await _repository.InsertAsync(suite, autoSave: true);
    }

    [Fact]
    public async Task Should_Reject_A_Suite_As_Its_Own_Parent()
    {
        await WithUnitOfWorkAsync(async () =>
        {
            var a = await CreateSuiteAsync("A");

            var exception = await Should.ThrowAsync<CircularSuiteDependencyException>(
                () => _manager.ValidateParentHierarchyAsync(a.Id, a.Id));

            exception.Code.ShouldBe(TestCaseManagementErrorCodes.CircularSuiteDependency);
        });
    }

    [Fact]
    public async Task Should_Reject_Moving_A_Suite_Under_Its_Direct_Child()
    {
        await WithUnitOfWorkAsync(async () =>
        {
            var a = await CreateSuiteAsync("A");
            var b = await CreateSuiteAsync("B", a.Id);

            await Should.ThrowAsync<CircularSuiteDependencyException>(
                () => _manager.ValidateParentHierarchyAsync(a.Id, b.Id));
        });
    }

    [Fact]
    public async Task Should_Reject_Moving_A_Suite_Under_A_Deep_Descendant()
    {
        await WithUnitOfWorkAsync(async () =>
        {
            var a = await CreateSuiteAsync("A");
            var b = await CreateSuiteAsync("B", a.Id);
            var c = await CreateSuiteAsync("C", b.Id);
            var d = await CreateSuiteAsync("D", c.Id);

            await Should.ThrowAsync<CircularSuiteDependencyException>(
                () => _manager.ValidateParentHierarchyAsync(a.Id, d.Id));
        });
    }

    [Fact]
    public async Task Should_Allow_Moving_A_Suite_Under_An_Unrelated_Or_Ancestor_Suite()
    {
        await WithUnitOfWorkAsync(async () =>
        {
            var a = await CreateSuiteAsync("A");
            var b = await CreateSuiteAsync("B", a.Id);
            var c = await CreateSuiteAsync("C", b.Id);
            var other = await CreateSuiteAsync("Other");

            await _manager.ValidateParentHierarchyAsync(c.Id, other.Id);
            await _manager.ValidateParentHierarchyAsync(c.Id, a.Id);
        });
    }

    [Fact]
    public async Task Should_Reject_A_Parent_That_Does_Not_Exist()
    {
        await WithUnitOfWorkAsync(async () =>
        {
            var a = await CreateSuiteAsync("A");

            var exception = await Should.ThrowAsync<BusinessException>(
                () => _manager.ValidateParentHierarchyAsync(a.Id, Guid.NewGuid()));

            exception.Code.ShouldBe(TestCaseManagementErrorCodes.SuiteNotFound);
        });
    }

    [Fact]
    public async Task Create_Should_Append_The_Suite_After_Its_Siblings()
    {
        await WithUnitOfWorkAsync(async () =>
        {
            var root = await CreateSuiteAsync("Root");
            var first = await CreateSuiteAsync("First", root.Id);
            var second = await CreateSuiteAsync("Second", root.Id);

            first.Order.ShouldBe(0);
            second.Order.ShouldBe(1);
        });
    }

    [Fact]
    public async Task Move_Should_Reparent_And_Renumber_Siblings()
    {
        await WithUnitOfWorkAsync(async () =>
        {
            var root = await CreateSuiteAsync("Root");
            var a = await CreateSuiteAsync("A", root.Id);
            var b = await CreateSuiteAsync("B", root.Id);
            var c = await CreateSuiteAsync("C", root.Id);

            // Drag C to the front of its siblings.
            await _manager.MoveAsync(c, root.Id, newOrder: 0);

            var siblings = (await _repository.GetListAsync(x => x.ParentId == root.Id))
                .OrderBy(x => x.Order)
                .Select(x => x.Name)
                .ToList();

            siblings.ShouldBe(new[] { "C", "A", "B" });
            (await _repository.GetListAsync(x => x.ParentId == root.Id))
                .Select(x => x.Order).OrderBy(x => x).ShouldBe(new[] { 0, 1, 2 });
        });
    }

    [Fact]
    public async Task Move_Should_Not_Persist_A_Circular_Parent()
    {
        await WithUnitOfWorkAsync(async () =>
        {
            var a = await CreateSuiteAsync("A");
            var b = await CreateSuiteAsync("B", a.Id);

            await Should.ThrowAsync<CircularSuiteDependencyException>(
                () => _manager.MoveAsync(a, b.Id, newOrder: 0));

            (await _repository.GetAsync(a.Id)).ParentId.ShouldBeNull();
        });
    }
}
