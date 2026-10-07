using System.Text;
using Acme.TestCaseManagement.Enums;
using Acme.TestCaseManagement.Suites;
using Acme.TestCaseManagement.Suites.Dtos;
using Acme.TestCaseManagement.TestCases;
using Acme.TestCaseManagement.TestCases.Dtos;
using Acme.TestCaseManagement.Transfer;
using Acme.TestCaseManagement.Transfer.Dtos;
using Shouldly;
using Volo.Abp;
using Volo.Abp.Content;
using Volo.Abp.Domain.Repositories;
using Xunit;

namespace Acme.TestCaseManagement;

/// <summary>FR-019: the Automation ID is the key that links a test case to its script, so it names one test case.</summary>
public class AutomationId_Tests : TestCaseManagementApplicationTestBase
{
    static AutomationId_Tests()
    {
        System.Globalization.CultureInfo.DefaultThreadCurrentCulture = new System.Globalization.CultureInfo("en");
        System.Globalization.CultureInfo.DefaultThreadCurrentUICulture = new System.Globalization.CultureInfo("en");
    }

    private readonly ITestSuiteAppService _suites;
    private readonly ITestCaseAppService _testCases;
    private readonly ITestCaseTransferAppService _transfer;
    private Guid? _suiteId;

    public AutomationId_Tests()
    {
        _suites = GetRequiredService<ITestSuiteAppService>();
        _testCases = GetRequiredService<ITestCaseAppService>();
        _transfer = GetRequiredService<ITestCaseTransferAppService>();
    }

    private async Task<CreateUpdateTestCaseDto> NewAsync(string code, string? automationId)
    {
        _suiteId ??= (await _suites.CreateAsync(new CreateTestSuiteDto { Name = "S" })).Id;
        return new CreateUpdateTestCaseDto { SuiteId = _suiteId.Value, Code = code, Title = $"Title of {code}", AutomationId = automationId };
    }

    private async Task<TestCaseDto> CreateAsync(string code, string? automationId) =>
        await _testCases.CreateAsync(await NewAsync(code, automationId));

    private async Task AssertDuplicateAsync(Func<Task> action, string automationId)
    {
        var exception = await Should.ThrowAsync<BusinessException>(action);
        exception.Code.ShouldBe(TestCaseManagementErrorCodes.DuplicateAutomationId);
        exception.Data["AutomationId"].ShouldBe(automationId);
    }

    [Fact]
    public async Task A_Second_Test_Case_Cannot_Take_An_Automation_Id_Ignoring_Case_And_Spaces()
    {
        await CreateAsync("TC-1", "e2e.login");

        await AssertDuplicateAsync(async () => await CreateAsync("TC-2", "e2e.login"), "e2e.login");
        await AssertDuplicateAsync(async () => await CreateAsync("TC-3", "E2E.LOGIN"), "E2E.LOGIN");
        await AssertDuplicateAsync(async () => await CreateAsync("TC-4", "  e2e.login  "), "e2e.login");

        (await _testCases.GetListAsync(new GetTestCaseListInput())).TotalCount.ShouldBe(1);
    }

    [Fact]
    public async Task Test_Cases_Without_An_Automation_Id_Are_Any_Number()
    {
        await CreateAsync("TC-1", null);
        await CreateAsync("TC-2", "");
        await CreateAsync("TC-3", "   ");

        var all = (await _testCases.GetListAsync(new GetTestCaseListInput())).Items;
        all.Count.ShouldBe(3);
        all.ShouldAllBe(testCase => testCase.AutomationId == null);
    }

    [Fact]
    public async Task The_Automation_Id_Is_Stored_Without_Surrounding_Spaces()
    {
        var created = await CreateAsync("TC-1", "  spaced.id ");

        created.AutomationId.ShouldBe("spaced.id");
    }

    [Fact]
    public async Task An_Update_May_Keep_Its_Own_Id_And_May_Not_Take_Another_One()
    {
        var first = await CreateAsync("TC-1", "id.one");
        await CreateAsync("TC-2", "id.two");

        // Saving with its own ID (even in another case) is no conflict.
        var keep = await NewAsync("TC-1", "ID.ONE");
        (await _testCases.UpdateAsync(first.Id, keep)).AutomationId.ShouldBe("ID.ONE");

        var take = await NewAsync("TC-1", "id.two");
        await AssertDuplicateAsync(async () => await _testCases.UpdateAsync(first.Id, take), "id.two");

        // It can be cleared, and then be taken by another.
        var clear = await NewAsync("TC-1", null);
        (await _testCases.UpdateAsync(first.Id, clear)).AutomationId.ShouldBeNull();
        var second = (await _testCases.GetListAsync(new GetTestCaseListInput { Filter = "TC-2" })).Items.Single();
        var move = await NewAsync("TC-2", "id.one");
        (await _testCases.UpdateAsync(second.Id, move)).AutomationId.ShouldBe("id.one");
    }

    [Fact]
    public async Task A_Test_Case_That_Already_Shares_An_Id_Can_Still_Be_Edited_Until_The_Id_Is_Changed()
    {
        await CreateAsync("TC-1", "legacy.id");
        var twin = await CreateAsync("TC-2", null);

        // Data from before the rule: the twin is given the same ID directly.
        await WithUnitOfWorkAsync(async () =>
        {
            var repository = GetRequiredService<IRepository<TestCase, Guid>>();
            var entity = await repository.GetAsync(twin.Id);
            entity.SetDetails(null, null, null, entity.Priority, entity.Severity, entity.ExecutionType, entity.Kind, entity.Layer, "legacy.id");
            await repository.UpdateAsync(entity, autoSave: true);
        });

        var edit = await NewAsync("TC-2", "legacy.id");
        edit.Title = "Renamed while sharing the ID";
        (await _testCases.UpdateAsync(twin.Id, edit)).Title.ShouldBe("Renamed while sharing the ID");

        var change = await NewAsync("TC-2", "legacy.id.v2");
        (await _testCases.UpdateAsync(twin.Id, change)).AutomationId.ShouldBe("legacy.id.v2");
    }

    // ---- through an import

    private static ImportTestCasesInput Csv(string text, ImportConflictMode mode = ImportConflictMode.Skip) =>
        new() { File = new RemoteStreamContent(new MemoryStream(Encoding.UTF8.GetBytes(text)), "cases.csv"), OnExisting = mode };

    [Fact]
    public async Task An_Import_Reports_An_Automation_Id_That_Is_Taken_And_Writes_Nothing()
    {
        await CreateAsync("TC-1", "taken.id");

        var report = await _transfer.ImportAsync(Csv(
            "Suite,Code,Title,AutomationId\n" +
            "S,TC-2,Fine,free.id\n" +
            "S,TC-3,Clash with the library,TAKEN.ID\n" +
            "S,TC-4,Clash within the file,free.id\n"));

        report.Imported.ShouldBeFalse();
        report.Items.Select(i => (i.Code, i.Outcome)).ShouldBe(new[]
        {
            ((string?)"TC-2", ImportOutcome.Created), ("TC-3", ImportOutcome.Invalid), ("TC-4", ImportOutcome.Invalid),
        });
        report.Items[1].Messages.Single().ShouldBe("Another test case already uses the automation id 'TAKEN.ID'.");
        (await _testCases.GetListAsync(new GetTestCaseListInput())).TotalCount.ShouldBe(1);
    }

    [Fact]
    public async Task An_Import_Update_May_Keep_The_Id_Of_Its_Test_Case_And_Take_A_Free_One()
    {
        var existing = await CreateAsync("TC-1", "keep.id");
        await CreateAsync("TC-2", "other.id");

        var kept = await _transfer.ImportAsync(Csv("Code,Title,AutomationId\nTC-1,New title,keep.id\n", ImportConflictMode.Update));
        kept.Updated.ShouldBe(1);

        var moved = await _transfer.ImportAsync(Csv("Code,AutomationId\nTC-1,free.id\n", ImportConflictMode.Update));
        moved.Updated.ShouldBe(1);
        (await _testCases.GetAsync(existing.Id)).AutomationId.ShouldBe("free.id");

        var clash = await _transfer.ImportAsync(Csv("Code,AutomationId\nTC-1,other.id\n", ImportConflictMode.Update));
        clash.Invalid.ShouldBe(1);
    }
}
