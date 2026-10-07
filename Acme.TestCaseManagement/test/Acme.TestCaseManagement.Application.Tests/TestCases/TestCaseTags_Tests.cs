using System.Text;
using Acme.TestCaseManagement.Enums;
using Acme.TestCaseManagement.Suites;
using Acme.TestCaseManagement.Suites.Dtos;
using Acme.TestCaseManagement.TestCases.Dtos;
using Acme.TestCaseManagement.Transfer;
using Acme.TestCaseManagement.Transfer.Dtos;
using Shouldly;
using Volo.Abp;
using Volo.Abp.Content;
using Xunit;

namespace Acme.TestCaseManagement.TestCases;

/// <summary>Tags (FR-024): labels on test cases that are filtered, listed, imported and exported, and never versioned.</summary>
public class TestCaseTags_Tests : TestCaseManagementApplicationTestBase
{
    static TestCaseTags_Tests()
    {
        System.Globalization.CultureInfo.DefaultThreadCurrentCulture = new System.Globalization.CultureInfo("en");
        System.Globalization.CultureInfo.DefaultThreadCurrentUICulture = new System.Globalization.CultureInfo("en");
    }

    private readonly ITestSuiteAppService _suites;
    private readonly ITestCaseAppService _testCases;
    private readonly ITestCaseTransferAppService _transfer;
    private Guid? _suiteId;

    public TestCaseTags_Tests()
    {
        _suites = GetRequiredService<ITestSuiteAppService>();
        _testCases = GetRequiredService<ITestCaseAppService>();
        _transfer = GetRequiredService<ITestCaseTransferAppService>();
    }

    private async Task<CreateUpdateTestCaseDto> NewAsync(string code, string[]? tags, string? automationId = null)
    {
        _suiteId ??= (await _suites.CreateAsync(new CreateTestSuiteDto { Name = "Tagged" })).Id;
        return new CreateUpdateTestCaseDto
        {
            SuiteId = _suiteId.Value,
            Code = code,
            Title = $"Title of {code}",
            AutomationId = automationId,
            Tags = tags?.ToList(),
            Steps = { new TestStepDto { Action = "Do it", ExpectedResult = "It works" } },
        };
    }

    private async Task<TestCaseDto> CreateAsync(string code, string[]? tags, string? automationId = null) =>
        await _testCases.CreateAsync(await NewAsync(code, tags, automationId));

    private async Task<string[]> CodesAsync(GetTestCaseListInput input) =>
        (await _testCases.GetListAsync(input)).Items.Select(i => i.Code).Order().ToArray();

    [Fact]
    public async Task A_Test_Case_Is_Created_With_Tags_And_They_Come_Back_Sorted_In_Both_The_Test_Case_And_The_List()
    {
        var created = await CreateAsync("TC-1", new[] { "payments", "Smoke", "api" });

        created.Tags.ShouldBe(new[] { "api", "payments", "Smoke" });
        (await _testCases.GetAsync(created.Id)).Tags.ShouldBe(new[] { "api", "payments", "Smoke" });
        (await _testCases.GetListAsync(new GetTestCaseListInput())).Items.Single().Tags.ShouldBe(new[] { "api", "payments", "Smoke" });
    }

    [Fact]
    public async Task An_Update_Without_Tags_Keeps_Them_And_An_Empty_List_Removes_Them()
    {
        var created = await CreateAsync("TC-1", new[] { "smoke" });

        var keep = await NewAsync("TC-1", null);
        keep.Title = "Renamed";
        (await _testCases.UpdateAsync(created.Id, keep)).Tags.ShouldBe(new[] { "smoke" });

        var clear = await NewAsync("TC-1", Array.Empty<string>());
        (await _testCases.UpdateAsync(created.Id, clear)).Tags.ShouldBeEmpty();
    }

    [Fact]
    public async Task Setting_Tags_On_An_Approved_Test_Case_Publishes_No_Version_And_Keeps_The_Status()
    {
        var created = await CreateAsync("TC-1", null);
        var approved = await _testCases.ChangeStatusAsync(created.Id, new ChangeTestCaseStatusDto { TargetStatus = TestCaseStatus.Approved });
        approved.CurrentVersion.ShouldBe(1);

        var tagged = await _testCases.SetTagsAsync(created.Id, new SetTestCaseTagsDto { Tags = { "regression", "Smoke" } });

        tagged.Tags.ShouldBe(new[] { "regression", "Smoke" });
        tagged.Status.ShouldBe(TestCaseStatus.Approved);
        tagged.CurrentVersion.ShouldBe(1, "labels are not content");
        (await _testCases.GetVersionsAsync(created.Id)).Count.ShouldBe(1);

        // Editing the content of an approved test case still publishes a version, as before.
        var edit = await NewAsync("TC-1", null);
        edit.Title = "New title";
        (await _testCases.UpdateAsync(created.Id, edit)).CurrentVersion.ShouldBe(2);
        (await _testCases.GetAsync(created.Id)).Tags.ShouldBe(new[] { "regression", "Smoke" });
    }

    [Fact]
    public async Task Tags_That_May_Not_Be_Used_Are_Refused_With_The_Tag_Named()
    {
        var created = await CreateAsync("TC-1", new[] { "keep" });

        var exception = await Should.ThrowAsync<BusinessException>(
            () => _testCases.SetTagsAsync(created.Id, new SetTestCaseTagsDto { Tags = { "ok", "bad,tag" } }));
        exception.Code.ShouldBe(TestCaseManagementErrorCodes.InvalidTag);
        exception.Data["Tag"].ShouldBe("bad,tag");

        var tooMany = Enumerable.Range(0, 21).Select(i => $"t{i}").ToList();
        (await Should.ThrowAsync<BusinessException>(() => _testCases.SetTagsAsync(created.Id, new SetTestCaseTagsDto { Tags = tooMany })))
            .Code.ShouldBe(TestCaseManagementErrorCodes.TooManyTags);

        (await _testCases.GetAsync(created.Id)).Tags.ShouldBe(new[] { "keep" });
    }

    [Fact]
    public async Task The_List_Is_Filtered_By_Tags_That_A_Test_Case_Must_All_Have_Ignoring_Case()
    {
        await CreateAsync("TC-1", new[] { "smoke", "payments" });
        await CreateAsync("TC-2", new[] { "smoke" });
        await CreateAsync("TC-3", new[] { "payments", "regression" });
        await CreateAsync("TC-4", null);

        (await CodesAsync(new GetTestCaseListInput { Tags = new List<string> { "SMOKE" } })).ShouldBe(new[] { "TC-1", "TC-2" });
        (await CodesAsync(new GetTestCaseListInput { Tags = new List<string> { "smoke", "Payments" } })).ShouldBe(new[] { "TC-1" });
        (await CodesAsync(new GetTestCaseListInput { Tags = new List<string> { "smoke", "regression" } })).ShouldBeEmpty();
        (await CodesAsync(new GetTestCaseListInput { Tags = new List<string> { "nothing" } })).ShouldBeEmpty();
        (await CodesAsync(new GetTestCaseListInput { Tags = new List<string>() })).Length.ShouldBe(4);
        (await CodesAsync(new GetTestCaseListInput { Tags = new List<string> { " " } })).Length.ShouldBe(4);

        var page = await _testCases.GetListAsync(new GetTestCaseListInput { Tags = new List<string> { "payments" }, MaxResultCount = 1 });
        page.TotalCount.ShouldBe(2);
        page.Items.Count.ShouldBe(1);
    }

    [Fact]
    public async Task The_List_Is_Filtered_By_Whether_An_Automation_Id_Is_Linked()
    {
        await CreateAsync("TC-1", null, "e2e.one");
        await CreateAsync("TC-2", null);
        await CreateAsync("TC-3", null, "e2e.three");

        (await CodesAsync(new GetTestCaseListInput { HasAutomationId = true })).ShouldBe(new[] { "TC-1", "TC-3" });
        (await CodesAsync(new GetTestCaseListInput { HasAutomationId = false })).ShouldBe(new[] { "TC-2" });
        (await CodesAsync(new GetTestCaseListInput { HasAutomationId = null })).Length.ShouldBe(3);
    }

    [Fact]
    public async Task The_Tag_List_Counts_Test_Cases_And_Ignores_Deleted_Ones()
    {
        await CreateAsync("TC-1", new[] { "Smoke", "payments" });
        await CreateAsync("TC-2", new[] { "smoke" });
        var gone = await CreateAsync("TC-3", new[] { "smoke", "legacy" });

        (await _testCases.GetTagsAsync()).Select(t => (t.Name, t.Count)).ShouldBe(new[] { ("smoke", 3), ("legacy", 1), ("payments", 1) }.Select(t => (t.Item1, t.Item2)));
        await _testCases.DeleteAsync(gone.Id);

        var after = await _testCases.GetTagsAsync();
        after.Select(t => (t.Name.ToLowerInvariant(), t.Count)).ShouldBe(new[] { ("smoke", 2), ("payments", 1) });
        after.ShouldNotContain(t => t.Name == "legacy");
    }

    [Fact]
    public async Task The_Tag_List_Shows_The_Spelling_Most_Used()
    {
        await CreateAsync("TC-1", new[] { "Smoke" });
        await CreateAsync("TC-2", new[] { "smoke" });
        await CreateAsync("TC-3", new[] { "smoke" });

        (await _testCases.GetTagsAsync()).Single().Name.ShouldBe("smoke");
    }

    // ---- import and export

    private static ImportTestCasesInput Csv(string text, ImportConflictMode mode = ImportConflictMode.Skip) =>
        new() { File = new RemoteStreamContent(new MemoryStream(Encoding.UTF8.GetBytes(text)), "cases.csv"), OnExisting = mode };

    [Fact]
    public async Task The_Export_Has_A_Tags_Column_And_An_Import_Of_It_Gives_The_Same_Tags()
    {
        await CreateAsync("TC-1", new[] { "smoke", "Payments" });
        var export = await _transfer.ExportAsync(new ExportTestCasesInput { Format = TransferFormat.Csv });
        using var reader = new StreamReader(export.GetStream(), Encoding.UTF8);
        var text = await reader.ReadToEndAsync();

        text.ShouldContain("Tags");
        text.ShouldContain("Payments; smoke");

        // Round trip into an empty library.
        var other = GetRequiredService<ITestCaseAppService>();
        var existing = (await other.GetListAsync(new GetTestCaseListInput())).Items.Single();
        await other.DeleteAsync(existing.Id);
        var input = Csv(text.TrimStart('﻿'), ImportConflictMode.Skip);
        input.DefaultSuiteId = _suiteId;
        var report = await _transfer.ImportAsync(input);
        report.Imported.ShouldBeTrue();
        (await _testCases.GetListAsync(new GetTestCaseListInput())).Items.Single().Tags.ShouldBe(new[] { "Payments", "smoke" });
    }

    [Fact]
    public async Task An_Import_Reads_Tags_Separated_By_Semicolons_And_Updates_Them_Only_When_The_Column_Is_In_The_File()
    {
        var report = await _transfer.ImportAsync(Csv("Suite,Code,Title,Tags\nS,TC-1,First,smoke; Payments;SMOKE\nS,TC-2,Second,\n"));
        report.Imported.ShouldBeTrue();
        (await _testCases.GetListAsync(new GetTestCaseListInput { Filter = "TC-1" })).Items.Single().Tags.ShouldBe(new[] { "Payments", "smoke" });
        (await _testCases.GetListAsync(new GetTestCaseListInput { Filter = "TC-2" })).Items.Single().Tags.ShouldBeEmpty();

        // No Tags column: the tags stay.
        (await _transfer.ImportAsync(Csv("Code,Title\nTC-1,First renamed\n", ImportConflictMode.Update))).Updated.ShouldBe(1);
        (await _testCases.GetListAsync(new GetTestCaseListInput { Filter = "TC-1" })).Items.Single().Tags.ShouldBe(new[] { "Payments", "smoke" });

        // A Tags column with a blank cell clears them; with other tags replaces them.
        (await _transfer.ImportAsync(Csv("Code,Tags\nTC-1,\nTC-2,ui;api\n", ImportConflictMode.Update))).Updated.ShouldBe(2);
        (await _testCases.GetListAsync(new GetTestCaseListInput { Filter = "TC-1" })).Items.Single().Tags.ShouldBeEmpty();
        (await _testCases.GetListAsync(new GetTestCaseListInput { Filter = "TC-2" })).Items.Single().Tags.ShouldBe(new[] { "api", "ui" });

        // The same tags in another case and order are no change.
        var same = await _transfer.ImportAsync(Csv("Code,Tags\nTC-2,API; UI\n", ImportConflictMode.Update));
        same.Updated.ShouldBe(0);
        same.Skipped.ShouldBe(1);
    }

    [Fact]
    public async Task An_Import_Row_With_A_Tag_That_May_Not_Be_Used_Is_Invalid_And_Nothing_Is_Written()
    {
        var long51 = new string('x', 51);

        var report = await _transfer.ImportAsync(Csv($"Suite,Code,Title,Tags\nS,TC-1,Fine,smoke\nS,TC-2,Too long,{long51}\n"));

        report.Imported.ShouldBeFalse();
        report.Items[1].Outcome.ShouldBe(ImportOutcome.Invalid);
        report.Items[1].Messages.Single().ShouldStartWith("Column Tags: the tag 'xxxxxxxx");
        (await _testCases.GetListAsync(new GetTestCaseListInput())).TotalCount.ShouldBe(0);

        var tooMany = string.Join(";", Enumerable.Range(0, 21).Select(i => $"t{i}"));
        var report2 = await _transfer.ImportAsync(Csv($"Suite,Code,Title,Tags\nS,TC-3,Many,{tooMany}\n"));
        report2.Items.Single().Messages.Single().ShouldBe("Column Tags: a test case can have at most 20 tags, and this one has 21.");
    }

    [Fact]
    public async Task Continuation_Rows_May_Leave_Tags_Blank_But_Not_Contradict_The_First_Row()
    {
        var ok = await _transfer.ImportAsync(Csv("Suite,Code,Title,Tags,Action,ExpectedResult\nS,TC-1,T,smoke;api,Do 1,Ok 1\n,TC-1,,,Do 2,Ok 2\n,TC-1,,API; smoke,Do 3,Ok 3\n"));
        ok.Imported.ShouldBeTrue();

        var clash = await _transfer.ImportAsync(Csv("Suite,Code,Title,Tags,Action,ExpectedResult\nS,TC-9,T,smoke,Do 1,Ok 1\n,TC-9,,other,Do 2,Ok 2\n"));
        clash.Imported.ShouldBeFalse();
        clash.Items.Single().Messages.ShouldContain(m => m.Contains("Tags"));
    }
}
