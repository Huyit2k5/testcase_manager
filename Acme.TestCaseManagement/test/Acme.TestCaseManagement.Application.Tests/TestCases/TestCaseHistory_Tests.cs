using Acme.TestCaseManagement.Enums;
using Acme.TestCaseManagement.Suites;
using Acme.TestCaseManagement.Suites.Dtos;
using Acme.TestCaseManagement.TestCases.Dtos;
using Shouldly;
using Volo.Abp.Domain.Repositories;
using Xunit;

namespace Acme.TestCaseManagement.TestCases;

/// <summary>The history of a test case: what was done to it, newest first, with the fields of an edit and the versions.</summary>
public class TestCaseHistory_Tests : TestCaseManagementApplicationTestBase
{
    private readonly ITestSuiteAppService _suites;
    private readonly ITestCaseAppService _testCases;
    private Guid? _suiteId;

    public TestCaseHistory_Tests()
    {
        _suites = GetRequiredService<ITestSuiteAppService>();
        _testCases = GetRequiredService<ITestCaseAppService>();
    }

    private async Task<CreateUpdateTestCaseDto> InputAsync(string code = "TC-H", string title = "Original title", PriorityLevel priority = PriorityLevel.Medium)
    {
        _suiteId ??= (await _suites.CreateAsync(new CreateTestSuiteDto { Name = "History" })).Id;
        return new CreateUpdateTestCaseDto
        {
            SuiteId = _suiteId.Value,
            Code = code,
            Title = title,
            Priority = priority,
            Steps =
            {
                new TestStepDto { Action = "Open", ExpectedResult = "Opened" },
                new TestStepDto { Action = "Save", ExpectedResult = "Saved" },
            },
        };
    }

    private async Task<List<TestCaseHistoryDto>> HistoryAsync(Guid id) => await _testCases.GetHistoryAsync(id);

    private static List<(TestCaseHistoryType Type, string? Note)> Lines(IEnumerable<TestCaseHistoryDto> history) =>
        history.Select(h => (h.Type, h.Note)).ToList();

    [Fact]
    public async Task A_Test_Case_Starts_Its_History_With_Its_Creation()
    {
        var created = await _testCases.CreateAsync(await InputAsync());

        var history = await HistoryAsync(created.Id);

        history.Count.ShouldBe(1);
        history[0].Type.ShouldBe(TestCaseHistoryType.Created);
    }

    [Fact]
    public async Task An_Edit_Records_The_Fields_That_Changed_And_Nothing_When_Nothing_Changed()
    {
        var created = await _testCases.CreateAsync(await InputAsync());

        // The same content again: no line.
        await _testCases.UpdateAsync(created.Id, await InputAsync());
        (await HistoryAsync(created.Id)).Count.ShouldBe(1);

        var edited = await InputAsync(title: "A better title", priority: PriorityLevel.Urgent);
        edited.Steps.Add(new TestStepDto { Action = "Close", ExpectedResult = "Closed" });
        await _testCases.UpdateAsync(created.Id, edited);

        var history = await HistoryAsync(created.Id);
        history.Count.ShouldBe(2);
        history[0].Type.ShouldBe(TestCaseHistoryType.Edited);
        history[0].Note.ShouldBe("Title, Priority, Steps");
    }

    [Fact]
    public async Task Approving_Records_The_Status_And_The_Version_With_Its_Summary_And_An_Edit_After_That_Sends_It_Back_To_Review()
    {
        var created = await _testCases.CreateAsync(await InputAsync());

        await _testCases.ChangeStatusAsync(created.Id, new ChangeTestCaseStatusDto { TargetStatus = TestCaseStatus.Approved, ChangeSummary = "First release" });

        var history = await HistoryAsync(created.Id);
        history.Select(h => h.Type).ShouldContain(TestCaseHistoryType.VersionPublished);
        var status = history.Single(h => h.Type == TestCaseHistoryType.StatusChanged);
        (status.FromValue, status.ToValue).ShouldBe(("Draft", "Approved"));
        var version = history.Single(h => h.Type == TestCaseHistoryType.VersionPublished);
        (version.VersionNumber, version.Note).ShouldBe((1, "First release"));

        await _testCases.UpdateAsync(created.Id, await InputAsync(title: "Edited after approval"));

        var after = await HistoryAsync(created.Id);
        var newest = after.Take(2).ToList();
        newest.Select(h => h.Type).ShouldBe(new[] { TestCaseHistoryType.Edited, TestCaseHistoryType.StatusChanged }, ignoreOrder: true);
        var back = after.First(h => h.Type == TestCaseHistoryType.StatusChanged && h.ToValue == "UnderReview");
        back.FromValue.ShouldBe("Approved");
    }

    [Fact]
    public async Task Tags_And_The_Order_Of_The_Steps_Are_Recorded_And_The_Same_Tags_Are_Not()
    {
        var created = await _testCases.CreateAsync(await InputAsync());

        await _testCases.SetTagsAsync(created.Id, new SetTestCaseTagsDto { Tags = new List<string> { "smoke", "api" } });
        await _testCases.SetTagsAsync(created.Id, new SetTestCaseTagsDto { Tags = new List<string> { "API", "Smoke" } });   // the same tags, in other letters and order

        var reversed = created.Steps.OrderByDescending(s => s.StepOrder).Select(s => s.Id!.Value).ToList();
        await _testCases.ReorderStepsAsync(created.Id, new ReorderTestStepsDto { StepIds = reversed });

        var history = await HistoryAsync(created.Id);
        history.Count(h => h.Type == TestCaseHistoryType.TagsChanged).ShouldBe(1);
        history.Single(h => h.Type == TestCaseHistoryType.TagsChanged).Note.ShouldBe("api, smoke");
        history.Count(h => h.Type == TestCaseHistoryType.StepsReordered).ShouldBe(1);
    }

    [Fact]
    public async Task A_Test_Case_Made_Before_The_History_Was_Kept_Still_Shows_Its_Creation_And_Its_Versions()
    {
        var created = await _testCases.CreateAsync(await InputAsync());
        await _testCases.ChangeStatusAsync(created.Id, new ChangeTestCaseStatusDto { TargetStatus = TestCaseStatus.Approved, ChangeSummary = "Old release" });

        // As in a database that existed before: no line of history at all.
        await WithUnitOfWorkAsync(async () =>
        {
            var lines = GetRequiredService<IRepository<TestCaseHistoryEntry, Guid>>();
            await lines.DeleteAsync(l => l.TestCaseId == created.Id, autoSave: true);
        });

        var history = await HistoryAsync(created.Id);

        history.Select(h => h.Type).OrderBy(t => t).ToList().ShouldBe(new List<TestCaseHistoryType> { TestCaseHistoryType.Created, TestCaseHistoryType.VersionPublished });
        history.Single(h => h.Type == TestCaseHistoryType.VersionPublished).Note.ShouldBe("Old release");
    }

    [Fact]
    public async Task The_Newest_Line_Comes_First()
    {
        var created = await _testCases.CreateAsync(await InputAsync());
        await _testCases.UpdateAsync(created.Id, await InputAsync(title: "One"));
        await _testCases.ChangeStatusAsync(created.Id, new ChangeTestCaseStatusDto { TargetStatus = TestCaseStatus.UnderReview });

        var history = await HistoryAsync(created.Id);

        history.Select(h => h.CreationTime).ShouldBe(history.Select(h => h.CreationTime).OrderByDescending(t => t).ToList());
        history[0].Type.ShouldBe(TestCaseHistoryType.StatusChanged);
        history[^1].Type.ShouldBe(TestCaseHistoryType.Created);
    }
}
