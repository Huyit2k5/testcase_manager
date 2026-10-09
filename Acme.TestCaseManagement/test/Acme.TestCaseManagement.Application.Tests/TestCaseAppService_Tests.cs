using Acme.TestCaseManagement.Enums;
using Acme.TestCaseManagement.Suites;
using Acme.TestCaseManagement.Suites.Dtos;
using Acme.TestCaseManagement.TestCases;
using Acme.TestCaseManagement.TestCases.Dtos;
using Shouldly;
using Volo.Abp;
using Volo.Abp.Domain.Entities;
using Xunit;

namespace Acme.TestCaseManagement;

public class TestCaseAppService_Tests : TestCaseManagementApplicationTestBase
{
    private readonly ITestSuiteAppService _suites;
    private readonly ITestCaseAppService _testCases;

    public TestCaseAppService_Tests()
    {
        _suites = GetRequiredService<ITestSuiteAppService>();
        _testCases = GetRequiredService<ITestCaseAppService>();
    }

    private async Task<TestSuiteDto> CreateSuiteAsync(string name, Guid? parentId = null) =>
        await _suites.CreateAsync(new CreateTestSuiteDto { Name = name, ParentId = parentId });

    private static CreateUpdateTestCaseDto NewTestCase(Guid suiteId, string code = "TC-AUTH-001", int steps = 3)
    {
        var dto = new CreateUpdateTestCaseDto
        {
            SuiteId = suiteId,
            Code = code,
            Title = "Verify successful login with valid credentials",
            Priority = PriorityLevel.High,
            Severity = SeverityLevel.Critical,
        };

        for (var i = 1; i <= steps; i++)
        {
            dto.Steps.Add(new TestStepDto { Action = $"Action {i}", ExpectedResult = $"Expected {i}" });
        }

        return dto;
    }

    [Fact]
    public async Task Create_Should_Persist_A_Draft_Test_Case_With_Ordered_Steps()
    {
        var suite = await CreateSuiteAsync("Authentication");

        var created = await _testCases.CreateAsync(NewTestCase(suite.Id, steps: 2));

        created.Id.ShouldNotBe(Guid.Empty);
        created.SuiteId.ShouldBe(suite.Id);
        created.Status.ShouldBe(TestCaseStatus.Draft);
        created.CurrentVersion.ShouldBe(0);
        created.Priority.ShouldBe(PriorityLevel.High);
        created.Severity.ShouldBe(SeverityLevel.Critical);

        var loaded = await _testCases.GetAsync(created.Id);
        loaded.Steps.Select(s => s.StepOrder).ShouldBe(new[] { 1, 2 });
        loaded.Steps.Select(s => s.Action).ShouldBe(new[] { "Action 1", "Action 2" });
    }

    [Fact]
    public async Task Us1_Independent_Test_Suite_Tree_Test_Case_Approval_And_Version_One()
    {
        var authentication = await CreateSuiteAsync("Authentication");
        var login = await CreateSuiteAsync("Login", authentication.Id);

        var created = await _testCases.CreateAsync(NewTestCase(login.Id));
        var approved = await _testCases.ChangeStatusAsync(
            created.Id, new ChangeTestCaseStatusDto { TargetStatus = TestCaseStatus.Approved, ChangeSummary = "QA Lead approval" });

        approved.Status.ShouldBe(TestCaseStatus.Approved);
        approved.CurrentVersion.ShouldBe(1);

        var versions = await _testCases.GetVersionsAsync(created.Id);
        versions.Count.ShouldBe(1);
        versions[0].VersionNumber.ShouldBe(1);
        versions[0].ChangeSummary.ShouldBe("QA Lead approval");
        versions[0].Steps.Count.ShouldBe(3);
        versions[0].Steps.Select(s => s.StepOrder).ShouldBe(new[] { 1, 2, 3 });

        var tree = await _suites.GetTreeAsync();
        tree.Single().Children.Single().TestCaseCount.ShouldBe(1);
    }

    [Fact]
    public async Task Updating_An_Approved_Test_Case_Should_Send_It_To_Review_And_Publish_The_New_Version_Only_When_Approved()
    {
        var suite = await CreateSuiteAsync("S");
        var created = await _testCases.CreateAsync(NewTestCase(suite.Id));
        await _testCases.ChangeStatusAsync(created.Id, new ChangeTestCaseStatusDto { TargetStatus = TestCaseStatus.Approved });

        var update = NewTestCase(suite.Id, steps: 0);
        update.Title = "Renamed";
        update.Steps.Add(new TestStepDto { Action = "Only step", ExpectedResult = "Only result" });
        var updated = await _testCases.UpdateAsync(created.Id, update);

        updated.Status.ShouldBe(TestCaseStatus.UnderReview);
        updated.CurrentVersion.ShouldBe(1);
        (await _testCases.GetVersionsAsync(created.Id)).Count.ShouldBe(1);

        var approved = await _testCases.ChangeStatusAsync(created.Id, new ChangeTestCaseStatusDto { TargetStatus = TestCaseStatus.Approved, ChangeSummary = "Reduced to one step" });
        approved.CurrentVersion.ShouldBe(2);

        var versions = await _testCases.GetVersionsAsync(created.Id);
        versions.Select(v => v.VersionNumber).ShouldBe(new[] { 2, 1 });

        var v1 = await _testCases.GetVersionAsync(created.Id, 1);
        v1.Title.ShouldBe("Verify successful login with valid credentials");
        v1.Steps.Count.ShouldBe(3);

        var v2 = await _testCases.GetVersionAsync(created.Id, 2);
        v2.Title.ShouldBe("Renamed");
        v2.ChangeSummary.ShouldBe("Reduced to one step");
        v2.Steps.Single().Action.ShouldBe("Only step");
    }

    [Fact]
    public async Task Updating_A_Draft_Test_Case_Should_Not_Publish_A_Version()
    {
        var suite = await CreateSuiteAsync("S");
        var created = await _testCases.CreateAsync(NewTestCase(suite.Id));

        var update = NewTestCase(suite.Id);
        update.Title = "Edited draft";
        var updated = await _testCases.UpdateAsync(created.Id, update);

        updated.CurrentVersion.ShouldBe(0);
        (await _testCases.GetVersionsAsync(created.Id)).ShouldBeEmpty();
    }

    [Fact]
    public async Task Update_Should_Keep_Ids_Of_Listed_Steps_And_Remove_The_Others()
    {
        var suite = await CreateSuiteAsync("S");
        var created = await _testCases.CreateAsync(NewTestCase(suite.Id));
        var original = (await _testCases.GetAsync(created.Id)).Steps;

        var update = NewTestCase(suite.Id, steps: 0);
        update.Steps.Add(new TestStepDto { Id = original[2].Id, Action = "Moved to front", ExpectedResult = "x" });
        update.Steps.Add(new TestStepDto { Id = original[0].Id, Action = "Second now", ExpectedResult = "y" });
        update.Steps.Add(new TestStepDto { Action = "Brand new", ExpectedResult = "z" });
        await _testCases.UpdateAsync(created.Id, update);

        var steps = (await _testCases.GetAsync(created.Id)).Steps;
        steps.Select(s => s.Action).ShouldBe(new[] { "Moved to front", "Second now", "Brand new" });
        steps[0].Id.ShouldBe(original[2].Id);
        steps[1].Id.ShouldBe(original[0].Id);
        steps.Select(s => s.Id).ShouldNotContain(original[1].Id);
        steps.Select(s => s.StepOrder).ShouldBe(new[] { 1, 2, 3 });
    }

    [Fact]
    public async Task ReorderSteps_Should_Apply_The_New_Order()
    {
        var suite = await CreateSuiteAsync("S");
        var created = await _testCases.CreateAsync(NewTestCase(suite.Id));
        var ids = (await _testCases.GetAsync(created.Id)).Steps.Select(s => s.Id!.Value).ToList();

        var reordered = await _testCases.ReorderStepsAsync(
            created.Id, new ReorderTestStepsDto { StepIds = new List<Guid> { ids[1], ids[2], ids[0] } });

        reordered.Steps.Select(s => s.Id!.Value).ShouldBe(new[] { ids[1], ids[2], ids[0] });
        reordered.Steps.Select(s => s.StepOrder).ShouldBe(new[] { 1, 2, 3 });

        (await Should.ThrowAsync<BusinessException>(() => _testCases.ReorderStepsAsync(
                created.Id, new ReorderTestStepsDto { StepIds = new List<Guid> { ids[0] } })))
            .Code.ShouldBe(TestCaseManagementErrorCodes.InvalidStepOrder);
    }

    [Fact]
    public async Task Create_Should_Reject_A_Duplicate_Code_And_An_Unknown_Suite()
    {
        var suite = await CreateSuiteAsync("S");
        await _testCases.CreateAsync(NewTestCase(suite.Id, "TC-DUP"));

        (await Should.ThrowAsync<BusinessException>(() => _testCases.CreateAsync(NewTestCase(suite.Id, "TC-DUP"))))
            .Code.ShouldBe(TestCaseManagementErrorCodes.DuplicateTestCaseCode);
        (await Should.ThrowAsync<BusinessException>(() => _testCases.CreateAsync(NewTestCase(Guid.NewGuid(), "TC-NEW"))))
            .Code.ShouldBe(TestCaseManagementErrorCodes.SuiteNotFound);
    }

    [Fact]
    public async Task Approving_A_Test_Case_Without_Steps_Should_Fail()
    {
        var suite = await CreateSuiteAsync("S");
        var created = await _testCases.CreateAsync(NewTestCase(suite.Id, steps: 0));

        (await Should.ThrowAsync<BusinessException>(() => _testCases.ChangeStatusAsync(
                created.Id, new ChangeTestCaseStatusDto { TargetStatus = TestCaseStatus.Approved })))
            .Code.ShouldBe(TestCaseManagementErrorCodes.TestCaseHasNoSteps);
    }

    [Fact]
    public async Task GetList_Should_Filter_By_Suite_Subtree_Status_And_Text()
    {
        var root = await CreateSuiteAsync("Root");
        var child = await CreateSuiteAsync("Child", root.Id);
        var other = await CreateSuiteAsync("Other");

        var inRoot = await _testCases.CreateAsync(NewTestCase(root.Id, "TC-ROOT"));
        var inChild = await _testCases.CreateAsync(NewTestCase(child.Id, "TC-CHILD"));
        await _testCases.CreateAsync(NewTestCase(other.Id, "TC-OTHER"));
        await _testCases.ChangeStatusAsync(inChild.Id, new ChangeTestCaseStatusDto { TargetStatus = TestCaseStatus.Approved });

        var subtree = await _testCases.GetListAsync(new GetTestCaseListInput { SuiteId = root.Id });
        subtree.TotalCount.ShouldBe(2);
        subtree.Items.Select(x => x.Code).ShouldBe(new[] { "TC-CHILD", "TC-ROOT" });

        var direct = await _testCases.GetListAsync(new GetTestCaseListInput { SuiteId = root.Id, IncludeDescendantSuites = false });
        direct.Items.Select(x => x.Id).ShouldBe(new[] { inRoot.Id });

        var approved = await _testCases.GetListAsync(new GetTestCaseListInput { Status = TestCaseStatus.Approved });
        approved.Items.Select(x => x.Id).ShouldBe(new[] { inChild.Id });

        var search = await _testCases.GetListAsync(new GetTestCaseListInput { Filter = "other" });
        search.Items.Select(x => x.Code).ShouldBe(new[] { "TC-OTHER" });
    }

    [Fact]
    public async Task GetList_Should_Page_Sort_And_Ignore_Unknown_Sort_Columns()
    {
        var suite = await CreateSuiteAsync("S");
        foreach (var code in new[] { "TC-B", "TC-A", "TC-C" })
        {
            await _testCases.CreateAsync(NewTestCase(suite.Id, code));
        }

        var page = await _testCases.GetListAsync(new GetTestCaseListInput { Sorting = "code desc", SkipCount = 1, MaxResultCount = 1 });
        page.TotalCount.ShouldBe(3);
        page.Items.Single().Code.ShouldBe("TC-B");

        var fallback = await _testCases.GetListAsync(new GetTestCaseListInput { Sorting = "Id; DROP TABLE x" });
        fallback.Items.Select(x => x.Code).ShouldBe(new[] { "TC-A", "TC-B", "TC-C" });
    }

    [Fact]
    public async Task Delete_Should_Soft_Delete_The_Test_Case()
    {
        var suite = await CreateSuiteAsync("S");
        var created = await _testCases.CreateAsync(NewTestCase(suite.Id));

        await _testCases.DeleteAsync(created.Id);

        await Should.ThrowAsync<EntityNotFoundException>(() => _testCases.GetAsync(created.Id));
        (await _testCases.GetListAsync(new GetTestCaseListInput())).TotalCount.ShouldBe(0);
    }

    [Theory]
    [InlineData("Priority")]
    [InlineData("Severity")]
    [InlineData("Status")]
    [InlineData("Priority desc")]
    public async Task Paging_Over_A_Column_With_Many_Equal_Values_Neither_Repeats_Nor_Skips_A_Test_Case(string sorting)
    {
        var suite = await CreateSuiteAsync("Paging");
        for (var i = 1; i <= 13; i++)
        {
            // Every test case has the same priority, severity and status, so the sort column alone cannot order them.
            await _testCases.CreateAsync(NewTestCase(suite.Id, $"TC-PAGE-{i:000}", steps: 1));
        }

        var seen = new List<string>();
        for (var page = 0; page < 3; page++)
        {
            var result = await _testCases.GetListAsync(new GetTestCaseListInput { Sorting = sorting, SkipCount = page * 5, MaxResultCount = 5 });
            result.TotalCount.ShouldBe(13);
            seen.AddRange(result.Items.Select(i => i.Code));
        }

        seen.Count.ShouldBe(13);
        seen.Distinct().Count().ShouldBe(13);

        // The same request gives the same order every time.
        var again = (await _testCases.GetListAsync(new GetTestCaseListInput { Sorting = sorting, MaxResultCount = 13 })).Items.Select(i => i.Code).ToList();
        again.Take(5).ShouldBe(seen.Take(5));
    }
}
