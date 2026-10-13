using Acme.TestCaseManagement.Enums;
using Acme.TestCaseManagement.Suites;
using Acme.TestCaseManagement.Suites.Dtos;
using Acme.TestCaseManagement.TestCases.Dtos;
using Shouldly;
using Xunit;

namespace Acme.TestCaseManagement.TestCases;

/// <summary>Approving many test cases at once, for example after an import: each as if it were approved alone.</summary>
public class ApproveMany_Tests : TestCaseManagementApplicationTestBase
{
    static ApproveMany_Tests()
    {
        System.Globalization.CultureInfo.DefaultThreadCurrentCulture = new System.Globalization.CultureInfo("en");
        System.Globalization.CultureInfo.DefaultThreadCurrentUICulture = new System.Globalization.CultureInfo("en");
    }

    private readonly ITestSuiteAppService _suites;
    private readonly ITestCaseAppService _testCases;
    private Guid? _suiteId;

    public ApproveMany_Tests()
    {
        _suites = GetRequiredService<ITestSuiteAppService>();
        _testCases = GetRequiredService<ITestCaseAppService>();
    }

    private async Task<TestCaseDto> DraftAsync(string code, bool withSteps = true)
    {
        _suiteId ??= (await _suites.CreateAsync(new CreateTestSuiteDto { Name = "Imported" })).Id;
        var input = new CreateUpdateTestCaseDto { SuiteId = _suiteId.Value, Code = code, Title = $"Title of {code}", Priority = PriorityLevel.High };
        if (withSteps)
        {
            input.Steps.Add(new TestStepDto { Action = "Do", ExpectedResult = "Done" });
        }

        return await _testCases.CreateAsync(input);
    }

    [Fact]
    public async Task Every_Draft_Is_Approved_With_Its_Own_First_Version_And_Its_Own_Lines_Of_History()
    {
        var one = await DraftAsync("BULK-1");
        var two = await DraftAsync("BULK-2");
        var three = await DraftAsync("BULK-3");

        var result = await _testCases.ApproveManyAsync(new ApproveTestCasesInput
        {
            TestCaseIds = { one.Id, two.Id, three.Id }, ChangeSummary = "Imported from the old sheet",
        });

        result.Approved.ShouldBe(3);
        result.ApprovedCodes.ShouldBe(new[] { "BULK-1", "BULK-2", "BULK-3" }, ignoreOrder: true);
        result.Failed.ShouldBeEmpty();
        foreach (var id in new[] { one.Id, two.Id, three.Id })
        {
            var approved = await _testCases.GetAsync(id);
            approved.Status.ShouldBe(TestCaseStatus.Approved);
            approved.CurrentVersion.ShouldBe(1);
            (await _testCases.GetVersionAsync(id, 1)).ChangeSummary.ShouldBe("Imported from the old sheet");
            var history = await _testCases.GetHistoryAsync(id);
            history.Select(h => h.Type).ShouldContain(TestCaseHistoryType.VersionPublished);
            history.Single(h => h.Type == TestCaseHistoryType.StatusChanged).ToValue.ShouldBe("Approved");
        }
    }

    [Fact]
    public async Task A_Test_Case_That_Cannot_Be_Approved_Is_Reported_And_Left_As_It_Was_While_The_Others_Are_Approved()
    {
        var good = await DraftAsync("BULK-OK");
        var noSteps = await DraftAsync("BULK-EMPTY", withSteps: false);
        var already = await DraftAsync("BULK-DONE");
        await _testCases.ChangeStatusAsync(already.Id, new ChangeTestCaseStatusDto { TargetStatus = TestCaseStatus.Approved });
        var unknown = Guid.NewGuid();

        var result = await _testCases.ApproveManyAsync(new ApproveTestCasesInput { TestCaseIds = { good.Id, noSteps.Id, already.Id, unknown } });

        result.Approved.ShouldBe(1);
        result.ApprovedCodes.ShouldBe(new[] { "BULK-OK" });
        result.Failed.Count.ShouldBe(3);
        result.Failed.Single(f => f.TestCaseId == noSteps.Id).ErrorCode.ShouldBe(TestCaseManagementErrorCodes.TestCaseHasNoSteps);
        result.Failed.Single(f => f.TestCaseId == noSteps.Id).Message.ShouldContain("BULK-EMPTY");
        result.Failed.Single(f => f.TestCaseId == already.Id).ErrorCode.ShouldBe(TestCaseManagementErrorCodes.InvalidTestCaseStatusTransition);
        var missing = result.Failed.Single(f => f.TestCaseId == unknown);
        (missing.ErrorCode, missing.Code).ShouldBe((TestCaseManagementErrorCodes.TestCaseNotFound, string.Empty));

        (await _testCases.GetAsync(noSteps.Id)).Status.ShouldBe(TestCaseStatus.Draft);
        (await _testCases.GetAsync(already.Id)).CurrentVersion.ShouldBe(1);   // not approved a second time: no version 2
        (await _testCases.GetAsync(good.Id)).Status.ShouldBe(TestCaseStatus.Approved);
    }

    [Fact]
    public async Task A_Test_Case_Waiting_For_Review_Is_Approved_To_Its_Next_Version_And_The_Same_Id_Twice_Counts_Once()
    {
        var created = await DraftAsync("BULK-EDIT");
        await _testCases.ChangeStatusAsync(created.Id, new ChangeTestCaseStatusDto { TargetStatus = TestCaseStatus.Approved });
        await _testCases.UpdateAsync(created.Id, new CreateUpdateTestCaseDto
        {
            SuiteId = _suiteId!.Value, Code = "BULK-EDIT", Title = "Edited after approval",
            Steps = { new TestStepDto { Action = "Do", ExpectedResult = "Done" } },
        });

        var result = await _testCases.ApproveManyAsync(new ApproveTestCasesInput { TestCaseIds = { created.Id, created.Id } });

        result.Approved.ShouldBe(1);
        result.Failed.ShouldBeEmpty();
        (await _testCases.GetAsync(created.Id)).CurrentVersion.ShouldBe(2);
    }
}
