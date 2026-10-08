using System.Text;
using Acme.TestCaseManagement.Enums;
using Acme.TestCaseManagement.SharedSteps.Dtos;
using Acme.TestCaseManagement.TestCases;
using Acme.TestCaseManagement.Suites;
using Acme.TestCaseManagement.Suites.Dtos;
using Acme.TestCaseManagement.TestCases.Dtos;
using Acme.TestCaseManagement.Transfer;
using Acme.TestCaseManagement.Transfer.Dtos;
using Shouldly;
using Volo.Abp;
using Volo.Abp.Content;
using Xunit;

namespace Acme.TestCaseManagement.SharedSteps;

/// <summary>Reusable steps (FR-005) end to end: the library, the copy in a test case, versions that keep what was tested, and bringing test cases up to date.</summary>
public class SharedStepGroupAppService_Tests : TestCaseManagementApplicationTestBase
{
    static SharedStepGroupAppService_Tests()
    {
        System.Globalization.CultureInfo.DefaultThreadCurrentCulture = new System.Globalization.CultureInfo("en");
        System.Globalization.CultureInfo.DefaultThreadCurrentUICulture = new System.Globalization.CultureInfo("en");
    }

    private readonly ISharedStepGroupAppService _groups;
    private readonly ITestCaseAppService _testCases;
    private readonly ITestSuiteAppService _suites;
    private readonly ITestCaseTransferAppService _transfer;
    private Guid? _suiteId;

    public SharedStepGroupAppService_Tests()
    {
        _groups = GetRequiredService<ISharedStepGroupAppService>();
        _testCases = GetRequiredService<ITestCaseAppService>();
        _suites = GetRequiredService<ITestSuiteAppService>();
        _transfer = GetRequiredService<ITestCaseTransferAppService>();
    }

    private static CreateUpdateSharedStepGroupDto GroupInput(string name, params string[] actions) => new()
    {
        Name = name,
        Description = $"{name} steps",
        Steps = actions.Select(a => new SharedStepDto { Action = a, ExpectedResult = "Ok" }).ToList(),
    };

    private Task<SharedStepGroupDto> GroupAsync(string name = "Log in", params string[] actions) =>
        _groups.CreateAsync(GroupInput(name, actions.Length == 0 ? new[] { "Open the login page", "Sign in" } : actions));

    private async Task<TestCaseDto> CaseAsync(string code, params string[] ownActions)
    {
        _suiteId ??= (await _suites.CreateAsync(new CreateTestSuiteDto { Name = "Shared" })).Id;
        return await _testCases.CreateAsync(new CreateUpdateTestCaseDto
        {
            SuiteId = _suiteId.Value,
            Code = code,
            Title = $"Title of {code}",
            Steps = (ownActions.Length == 0 ? new[] { "Search" } : ownActions).Select(a => new TestStepDto { Action = a, ExpectedResult = "Ok" }).ToList(),
        });
    }

    private Task<TestCaseDto> ApproveAsync(Guid id) =>
        _testCases.ChangeStatusAsync(id, new ChangeTestCaseStatusDto { TargetStatus = TestCaseStatus.Approved });

    private static string[] Actions(TestCaseDto testCase) => testCase.Steps.OrderBy(s => s.StepOrder).Select(s => s.Action).ToArray();

    /// <summary>Changes the second step of the group, which is a change of content and so a new revision.</summary>
    private async Task<SharedStepGroupDto> ChangeGroupAsync(SharedStepGroupDto group, string newSecondAction)
    {
        var input = GroupInput(group.Name, "ignored");
        input.Steps = group.Steps.Select(s => new SharedStepDto { Id = s.Id, Action = s.StepOrder == 2 ? newSecondAction : s.Action, ExpectedResult = s.ExpectedResult }).ToList();
        return await _groups.UpdateAsync(group.Id, input);
    }

    // ---- the library

    [Fact]
    public async Task A_Group_Is_Created_Listed_Read_And_Updated_With_Its_Revision()
    {
        var created = await GroupAsync("Log in", "Open the login page", "Sign in");
        await GroupAsync("Pay", "Pay by card");

        created.Revision.ShouldBe(1);
        created.Steps.Select(s => (s.StepOrder, s.Action)).ShouldBe(new[] { (1, "Open the login page"), (2, "Sign in") });

        var list = await _groups.GetListAsync(new GetSharedStepGroupsInput());
        list.Select(g => (g.Name, g.StepCount, g.UsedByCount, g.Revision)).ShouldBe(new[] { ("Log in", 2, 0, 1), ("Pay", 1, 0, 1) });
        (await _groups.GetListAsync(new GetSharedStepGroupsInput { Filter = "PAY" })).Select(g => g.Name).ShouldBe(new[] { "Pay" });

        var renamed = GroupInput("Log in as a customer", "unused");
        renamed.Steps = created.Steps.Select(s => new SharedStepDto { Id = s.Id, Action = s.Action, ExpectedResult = s.ExpectedResult }).ToList();
        var afterRename = await _groups.UpdateAsync(created.Id, renamed);
        afterRename.Revision.ShouldBe(1, "a name is not a change of steps");
        afterRename.Name.ShouldBe("Log in as a customer");

        (await ChangeGroupAsync(afterRename, "Sign in with the code")).Revision.ShouldBe(2);
        (await _groups.GetAsync(created.Id)).Steps[1].Action.ShouldBe("Sign in with the code");
    }

    [Fact]
    public async Task A_Name_Names_One_Group_Ignoring_Case()
    {
        await GroupAsync("Log in");

        var exception = await Should.ThrowAsync<BusinessException>(() => GroupAsync("  LOG IN "));
        exception.Code.ShouldBe(TestCaseManagementErrorCodes.DuplicateSharedStepGroupName);

        var other = await GroupAsync("Pay");
        var input = GroupInput("log in", "Pay by card");
        (await Should.ThrowAsync<BusinessException>(() => _groups.UpdateAsync(other.Id, input))).Code.ShouldBe(TestCaseManagementErrorCodes.DuplicateSharedStepGroupName);

        // A group may keep its own name, in another case.
        var same = GroupInput("PAY", "Pay by card");
        same.Steps[0].Id = other.Steps[0].Id;
        (await _groups.UpdateAsync(other.Id, same)).Name.ShouldBe("PAY");
    }

    [Fact]
    public async Task A_Group_Needs_Steps_And_A_Deleted_Group_Gives_Its_Name_Back()
    {
        var empty = GroupInput("Empty");
        empty.Steps.Clear();
        // The input is validated first (see also the domain tests for the rule itself).
        await Should.ThrowAsync<Volo.Abp.Validation.AbpValidationException>(() => _groups.CreateAsync(empty));

        var group = await GroupAsync("Log in");
        await _groups.DeleteAsync(group.Id);
        await Should.ThrowAsync<Volo.Abp.Domain.Entities.EntityNotFoundException>(() => _groups.GetAsync(group.Id));
        (await GroupAsync("Log in")).Revision.ShouldBe(1);
    }

    // ---- in a test case

    [Fact]
    public async Task Inserting_A_Group_Into_A_Draft_Copies_Its_Steps_And_Shows_Where_They_Came_From()
    {
        var group = await GroupAsync("Log in");
        var testCase = await CaseAsync("TC-1", "Search", "Add to cart");

        var updated = await _testCases.InsertSharedStepsAsync(testCase.Id, new InsertSharedStepsDto { SharedStepGroupId = group.Id, Position = 1 });

        Actions(updated).ShouldBe(new[] { "Open the login page", "Sign in", "Search", "Add to cart" });
        updated.CurrentVersion.ShouldBe(0, "a draft publishes nothing");
        var linked = updated.Steps.Where(s => s.SharedStepGroupId == group.Id).OrderBy(s => s.StepOrder).ToList();
        linked.Select(s => (s.SharedStepGroupName, s.SharedStepRevision, s.SharedStepOutdated)).ShouldBe(new[] { ("Log in", (int?)1, false), ("Log in", (int?)1, false) });
        updated.Steps.Where(s => s.SharedStepGroupId == null).ShouldAllBe(s => s.SharedStepGroupName == null);

        (await _testCases.GetAsync(testCase.Id)).Steps.Count(s => s.SharedStepGroupId == group.Id).ShouldBe(2);
        (await _groups.GetListAsync(new GetSharedStepGroupsInput())).Single().UsedByCount.ShouldBe(1);
    }

    [Fact]
    public async Task Changing_A_Group_Changes_No_Test_Case_It_Only_Shows_Them_As_Behind()
    {
        var group = await GroupAsync("Log in");
        var draft = await CaseAsync("TC-1");
        var approved = await CaseAsync("TC-2");
        await _testCases.InsertSharedStepsAsync(draft.Id, new InsertSharedStepsDto { SharedStepGroupId = group.Id });
        await _testCases.InsertSharedStepsAsync(approved.Id, new InsertSharedStepsDto { SharedStepGroupId = group.Id });
        await ApproveAsync(approved.Id);

        await ChangeGroupAsync(group, "Sign in with the code");

        foreach (var id in new[] { draft.Id, approved.Id })
        {
            var testCase = await _testCases.GetAsync(id);
            Actions(testCase).ShouldBe(new[] { "Search", "Open the login page", "Sign in" }, "the copy is what was written");
            testCase.Steps.Where(s => s.SharedStepGroupId == group.Id).ShouldAllBe(s => s.SharedStepOutdated && s.SharedStepRevision == 1);
        }

        (await _testCases.GetAsync(approved.Id)).CurrentVersion.ShouldBe(1);
        (await _groups.GetUsageAsync(group.Id)).Select(u => (u.Code, u.LinkedRevision, u.IsOutdated, u.LinkedStepCount))
            .ShouldBe(new[] { ("TC-1", 1, true, 2), ("TC-2", 1, true, 2) });
    }

    [Fact]
    public async Task Inserting_Into_An_Approved_Test_Case_Publishes_A_Version_That_Keeps_The_Copy_Even_After_The_Group_Changes()
    {
        var group = await GroupAsync("Log in");
        var testCase = await CaseAsync("TC-1");
        await ApproveAsync(testCase.Id);

        var updated = await _testCases.InsertSharedStepsAsync(testCase.Id, new InsertSharedStepsDto { SharedStepGroupId = group.Id, ChangeSummary = "Added the login" });
        updated.CurrentVersion.ShouldBe(2);

        await ChangeGroupAsync(group, "Sign in with the code");
        await _testCases.RefreshSharedStepsAsync(testCase.Id, group.Id, new RefreshSharedStepsDto());

        var versions = await _testCases.GetVersionsAsync(testCase.Id);
        versions.Select(v => v.VersionNumber).ShouldBe(new[] { 3, 2, 1 });
        // Version 2 still holds the steps that were approved then; version 3 holds the refreshed ones.
        versions.Single(v => v.VersionNumber == 2).Steps.Select(s => s.Action).ShouldBe(new[] { "Search", "Open the login page", "Sign in" });
        versions.Single(v => v.VersionNumber == 2).ChangeSummary.ShouldBe("Added the login");
        versions.Single(v => v.VersionNumber == 3).Steps.Select(s => s.Action).ShouldBe(new[] { "Search", "Open the login page", "Sign in with the code" });
        versions.Single(v => v.VersionNumber == 3).ChangeSummary.ShouldBe("Shared steps 'Log in' updated to revision 2.");
    }

    [Fact]
    public async Task Refreshing_One_Test_Case_Brings_It_Up_To_Date_And_Detaching_Publishes_Nothing()
    {
        var group = await GroupAsync("Log in");
        var testCase = await CaseAsync("TC-1");
        await _testCases.InsertSharedStepsAsync(testCase.Id, new InsertSharedStepsDto { SharedStepGroupId = group.Id, Position = 1 });
        await ChangeGroupAsync(group, "Sign in with the code");

        var refreshed = await _testCases.RefreshSharedStepsAsync(testCase.Id, group.Id, new RefreshSharedStepsDto());
        Actions(refreshed).ShouldBe(new[] { "Open the login page", "Sign in with the code", "Search" });
        refreshed.Steps.Where(s => s.SharedStepGroupId == group.Id).ShouldAllBe(s => s.SharedStepRevision == 2 && !s.SharedStepOutdated);

        await ApproveAsync(testCase.Id);
        var detached = await _testCases.DetachSharedStepsAsync(testCase.Id, group.Id);
        detached.Steps.ShouldAllBe(s => s.SharedStepGroupId == null && s.SharedStepGroupName == null);
        Actions(detached).ShouldBe(new[] { "Open the login page", "Sign in with the code", "Search" });
        detached.CurrentVersion.ShouldBe(1, "nothing was tested differently");
        (await _groups.GetUsageAsync(group.Id)).ShouldBeEmpty();

        (await Should.ThrowAsync<BusinessException>(() => _testCases.RefreshSharedStepsAsync(testCase.Id, group.Id, new RefreshSharedStepsDto())))
            .Code.ShouldBe(TestCaseManagementErrorCodes.SharedStepsNotLinked);
    }

    [Fact]
    public async Task Editing_A_Linked_Step_In_The_Test_Case_Unlinks_It_And_An_Edit_That_Leaves_It_Alone_Keeps_The_Link()
    {
        var group = await GroupAsync("Log in");
        var testCase = await CaseAsync("TC-1");
        var withGroup = await _testCases.InsertSharedStepsAsync(testCase.Id, new InsertSharedStepsDto { SharedStepGroupId = group.Id });

        Task<TestCaseDto> Save(Action<List<TestStepDto>>? edit = null)
        {
            var steps = withGroup.Steps.OrderBy(s => s.StepOrder).Select(s => new TestStepDto { Id = s.Id, Action = s.Action, ExpectedResult = s.ExpectedResult, TestData = s.TestData }).ToList();
            edit?.Invoke(steps);
            return _testCases.UpdateAsync(testCase.Id, new CreateUpdateTestCaseDto
            {
                SuiteId = withGroup.SuiteId, Code = withGroup.Code, Title = "Renamed", Steps = steps,
            });
        }

        (await Save()).Steps.Count(s => s.SharedStepGroupId == group.Id).ShouldBe(2);

        var edited = await Save(steps => steps[1].Action = "Open the login page, then wait");
        edited.Steps.OrderBy(s => s.StepOrder).Select(s => s.SharedStepGroupId).ShouldBe(new Guid?[] { null, null, group.Id });
    }

    [Fact]
    public async Task A_Group_That_Is_Used_Cannot_Be_Deleted_Until_It_Is_Detached()
    {
        var group = await GroupAsync("Log in");
        var testCase = await CaseAsync("TC-1");
        await _testCases.InsertSharedStepsAsync(testCase.Id, new InsertSharedStepsDto { SharedStepGroupId = group.Id });

        var exception = await Should.ThrowAsync<BusinessException>(() => _groups.DeleteAsync(group.Id));
        exception.Code.ShouldBe(TestCaseManagementErrorCodes.SharedStepGroupInUse);
        exception.Data["Count"].ShouldBe(1);

        await _testCases.DetachSharedStepsAsync(testCase.Id, group.Id);
        await _groups.DeleteAsync(group.Id);
    }

    [Fact]
    public async Task A_Deleted_Test_Case_Does_Not_Count_As_A_User_Of_A_Group()
    {
        var group = await GroupAsync("Log in");
        var testCase = await CaseAsync("TC-1");
        await _testCases.InsertSharedStepsAsync(testCase.Id, new InsertSharedStepsDto { SharedStepGroupId = group.Id });
        await _testCases.DeleteAsync(testCase.Id);

        (await _groups.GetListAsync(new GetSharedStepGroupsInput())).Single().UsedByCount.ShouldBe(0);
        (await _groups.GetUsageAsync(group.Id)).ShouldBeEmpty();
        await _groups.DeleteAsync(group.Id);
    }

    // ---- bringing test cases up to date

    [Fact]
    public async Task One_Action_Brings_Every_Test_Case_That_Is_Behind_Up_To_Date_And_Approved_Ones_Get_A_Version()
    {
        var group = await GroupAsync("Log in");
        var draft = await CaseAsync("TC-DRAFT");
        var approved = await CaseAsync("TC-APPROVED");
        var current = await CaseAsync("TC-CURRENT");
        foreach (var id in new[] { draft.Id, approved.Id })
        {
            await _testCases.InsertSharedStepsAsync(id, new InsertSharedStepsDto { SharedStepGroupId = group.Id, Position = 1 });
        }

        await ApproveAsync(approved.Id);
        var changed = await ChangeGroupAsync(group, "Sign in with the code");
        await _testCases.InsertSharedStepsAsync(current.Id, new InsertSharedStepsDto { SharedStepGroupId = group.Id });   // already at revision 2

        var result = await _groups.UpdateTestCasesAsync(group.Id, new UpdateSharedStepUsersInput());

        result.Codes.Order().ToArray().ShouldBe(new[] { "TC-APPROVED", "TC-DRAFT" });
        result.Updated.ShouldBe(2);
        result.NewVersions.ShouldBe(1);
        foreach (var id in new[] { draft.Id, approved.Id })
        {
            var testCase = await _testCases.GetAsync(id);
            Actions(testCase).ShouldBe(new[] { "Open the login page", "Sign in with the code", "Search" });
            testCase.Steps.Where(s => s.SharedStepGroupId == group.Id).ShouldAllBe(s => s.SharedStepRevision == 2 && !s.SharedStepOutdated);
        }

        (await _testCases.GetAsync(approved.Id)).CurrentVersion.ShouldBe(2);
        (await _testCases.GetAsync(draft.Id)).CurrentVersion.ShouldBe(0);
        (await _groups.GetUsageAsync(changed.Id)).ShouldAllBe(u => !u.IsOutdated);

        // Nothing left to do the second time.
        (await _groups.UpdateTestCasesAsync(group.Id, new UpdateSharedStepUsersInput())).Updated.ShouldBe(0);
    }

    [Fact]
    public async Task The_Bulk_Update_Can_Be_Limited_To_Chosen_Test_Cases()
    {
        var group = await GroupAsync("Log in");
        var first = await CaseAsync("TC-1");
        var second = await CaseAsync("TC-2");
        var unrelated = await CaseAsync("TC-3");
        await _testCases.InsertSharedStepsAsync(first.Id, new InsertSharedStepsDto { SharedStepGroupId = group.Id });
        await _testCases.InsertSharedStepsAsync(second.Id, new InsertSharedStepsDto { SharedStepGroupId = group.Id });
        await ChangeGroupAsync(group, "Sign in with the code");

        var result = await _groups.UpdateTestCasesAsync(group.Id, new UpdateSharedStepUsersInput { TestCaseIds = new List<Guid> { first.Id, unrelated.Id } });

        result.Codes.ShouldBe(new[] { "TC-1" });
        (await _groups.GetUsageAsync(group.Id)).Single(u => u.Code == "TC-2").IsOutdated.ShouldBeTrue();
    }

    // ---- import and export

    [Fact]
    public async Task The_Export_Holds_The_Steps_As_Plain_Steps_And_An_Import_That_Leaves_Them_Alone_Keeps_The_Link()
    {
        var group = await GroupAsync("Log in");
        var testCase = await CaseAsync("TC-1", "Search");
        await _testCases.InsertSharedStepsAsync(testCase.Id, new InsertSharedStepsDto { SharedStepGroupId = group.Id, Position = 1 });

        var export = await _transfer.ExportAsync(new ExportTestCasesInput { Format = TransferFormat.Csv });
        using var reader = new StreamReader(export.GetStream(), Encoding.UTF8);
        var text = (await reader.ReadToEndAsync()).TrimStart('﻿');
        text.ShouldContain("Open the login page");
        text.ShouldContain("Sign in");

        // The same file back in: nothing changed, so the steps are still the copy of the group.
        var input = new ImportTestCasesInput { File = new RemoteStreamContent(new MemoryStream(Encoding.UTF8.GetBytes(text)), "cases.csv"), OnExisting = ImportConflictMode.Update };
        var report = await _transfer.ImportAsync(input);
        report.Imported.ShouldBeTrue();
        report.Updated.ShouldBe(0);
        (await _testCases.GetAsync(testCase.Id)).Steps.Count(s => s.SharedStepGroupId == group.Id).ShouldBe(2);

        // A file that changes the text of a copied step makes it the test case's own.
        var changed = new ImportTestCasesInput
        {
            File = new RemoteStreamContent(new MemoryStream(Encoding.UTF8.GetBytes(text.Replace("Open the login page", "Open the new login page"))), "cases.csv"),
            OnExisting = ImportConflictMode.Update,
        };
        (await _transfer.ImportAsync(changed)).Updated.ShouldBe(1);
        (await _testCases.GetAsync(testCase.Id)).Steps.OrderBy(s => s.StepOrder).Select(s => s.SharedStepGroupId).ShouldBe(new Guid?[] { null, group.Id, null });
    }

    [Fact]
    public async Task A_Group_Is_Used_Once_By_A_Test_Case_And_To_Use_It_Again_The_First_Copy_Is_Detached()
    {
        var group = await GroupAsync("Log in");
        var testCase = await CaseAsync("TC-TWICE");
        var once = await _testCases.InsertSharedStepsAsync(testCase.Id, new InsertSharedStepsDto { SharedStepGroupId = group.Id, Position = 1 });
        var linked = once.Steps.Count(s => s.SharedStepGroupId == group.Id);

        // A second copy could not be told from the first when the group is refreshed, so it is refused and nothing changes.
        (await Should.ThrowAsync<BusinessException>(() => _testCases.InsertSharedStepsAsync(testCase.Id, new InsertSharedStepsDto { SharedStepGroupId = group.Id })))
            .Code.ShouldBe(TestCaseManagementErrorCodes.SharedStepGroupAlreadyUsed);
        (await _testCases.GetAsync(testCase.Id)).Steps.Count.ShouldBe(once.Steps.Count);

        // Detached, the first copy is the test case's own steps and the group can be inserted again; a refresh then keeps both parts.
        await _testCases.DetachSharedStepsAsync(testCase.Id, group.Id);
        var twice = await _testCases.InsertSharedStepsAsync(testCase.Id, new InsertSharedStepsDto { SharedStepGroupId = group.Id });
        twice.Steps.Count.ShouldBe(once.Steps.Count + linked);

        var refreshed = await _testCases.RefreshSharedStepsAsync(testCase.Id, group.Id, new RefreshSharedStepsDto());
        refreshed.Steps.Count.ShouldBe(twice.Steps.Count);
        refreshed.Steps.Count(s => s.SharedStepGroupId == group.Id).ShouldBe(linked);
    }
}
