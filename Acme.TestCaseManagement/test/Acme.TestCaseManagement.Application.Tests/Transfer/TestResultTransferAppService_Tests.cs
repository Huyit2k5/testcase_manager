using System.Text;
using Acme.TestCaseManagement.Enums;
using Acme.TestCaseManagement.Runs;
using Acme.TestCaseManagement.Runs.Dtos;
using Acme.TestCaseManagement.Suites;
using Acme.TestCaseManagement.Suites.Dtos;
using Acme.TestCaseManagement.TestCases;
using Acme.TestCaseManagement.TestCases.Dtos;
using Acme.TestCaseManagement.Transfer.Dtos;
using Acme.TestCaseManagement.Transfer.Tabular;
using Shouldly;
using Volo.Abp.Content;
using Volo.Abp.Domain.Entities;
using Volo.Abp.Domain.Repositories;
using Xunit;

namespace Acme.TestCaseManagement.Transfer;

public class TestResultTransferAppService_Tests : TestCaseManagementApplicationTestBase
{
    static TestResultTransferAppService_Tests()
    {
        // The report messages are asserted in English, whatever the language of the machine that runs the tests.
        System.Globalization.CultureInfo.DefaultThreadCurrentCulture = new System.Globalization.CultureInfo("en");
        System.Globalization.CultureInfo.DefaultThreadCurrentUICulture = new System.Globalization.CultureInfo("en");
    }

    private readonly ITestSuiteAppService _suites;
    private readonly ITestCaseAppService _testCases;
    private readonly ITestRunAppService _runs;
    private readonly ITestResultTransferAppService _transfer;
    private Guid? _suiteId;

    public TestResultTransferAppService_Tests()
    {
        _suites = GetRequiredService<ITestSuiteAppService>();
        _testCases = GetRequiredService<ITestCaseAppService>();
        _runs = GetRequiredService<ITestRunAppService>();
        _transfer = GetRequiredService<ITestResultTransferAppService>();
    }

    private async Task<TestCaseDto> ApprovedCaseAsync(string code)
    {
        _suiteId ??= (await _suites.CreateAsync(new CreateTestSuiteDto { Name = "Library" })).Id;
        var created = await _testCases.CreateAsync(new CreateUpdateTestCaseDto
        {
            SuiteId = _suiteId.Value,
            Code = code,
            Title = $"Title of {code}",
            Severity = SeverityLevel.High,
            Steps = { new TestStepDto { Action = "Do it", ExpectedResult = "Done" } },
        });

        return await _testCases.ChangeStatusAsync(created.Id, new ChangeTestCaseStatusDto { TargetStatus = TestCaseStatus.Approved });
    }

    private async Task<TestRunDto> RunWithAsync(params string[] codes)
    {
        var ids = new List<Guid>();
        foreach (var code in codes)
        {
            ids.Add((await ApprovedCaseAsync(code)).Id);
        }

        return await _runs.CreateAsync(new CreateTestRunDto { Title = "Regression", Environment = "Staging", TestCaseIds = ids });
    }

    private static ImportTestResultsInput Csv(string text, bool dryRun = false) =>
        new() { File = new RemoteStreamContent(new MemoryStream(Encoding.UTF8.GetBytes(text)), "results.csv"), DryRun = dryRun };

    private static async Task<Table> TableOf(IRemoteStreamContent content)
    {
        using var stream = new MemoryStream();
        await content.GetStream().CopyToAsync(stream);
        return TableFile.Read(stream.ToArray(), new TestCaseManagementTransferOptions());
    }

    [Fact]
    public async Task Export_Lists_Every_Attempt_Oldest_First_With_Its_Defects_And_Marks_The_Items_Never_Run_As_Untested()
    {
        var run = await RunWithAsync("TC-1", "TC-2");
        var first = run.Items.Single(i => i.TestCaseCode == "TC-1");

        await _runs.ExecuteItemAsync(run.Id, first.Id, new ExecuteTestItemDto
        {
            Status = TestResultStatus.Failed,
            ActualResult = "Crash, \"badly\"",
            DurationSeconds = 45,
            Defects = { new AddDefectLinkDto { ExternalSystem = "Jira", IssueKey = "BUG-1" }, new AddDefectLinkDto { ExternalSystem = "GitHub", IssueKey = "#42" } },
        });
        await _runs.ExecuteItemAsync(run.Id, first.Id, new ExecuteTestItemDto { Status = TestResultStatus.Passed, DurationSeconds = 30 });

        var export = await _transfer.ExportAsync(run.Id, TransferFormat.Csv);
        var table = await TableOf(export);

        export.FileName.ShouldEndWith(".csv");
        table.Header.ShouldBe(TestResultSheet.ExportColumns);
        table.Rows.Count.ShouldBe(3);

        table.Cell(0, "Code").ShouldBe("TC-1");
        table.Cell(0, "Title").ShouldBe("Title of TC-1");
        table.Cell(0, "Version").ShouldBe("1");
        table.Cell(0, "Attempt").ShouldBe("1");
        table.Cell(0, "Result").ShouldBe("Failed");
        table.Cell(0, "ActualResult").ShouldBe("Crash, \"badly\"");
        table.Cell(0, "DurationSeconds").ShouldBe("45");
        table.Cell(0, "Defects").ShouldBe("Jira:BUG-1; GitHub:#42");
        table.Cell(0, "ExecutedAt").ShouldNotBeNullOrWhiteSpace();

        table.Cell(1, "Attempt").ShouldBe("2");
        table.Cell(1, "Result").ShouldBe("Passed");
        table.Cell(1, "Defects").ShouldBe(string.Empty);

        table.Cell(2, "Code").ShouldBe("TC-2");
        table.Cell(2, "Result").ShouldBe("Untested");
        table.Cell(2, "Attempt").ShouldBe(string.Empty);
    }

    [Fact]
    public async Task Export_Of_An_Unknown_Run_Is_Not_Found()
    {
        await Should.ThrowAsync<EntityNotFoundException>(() => _transfer.ExportAsync(Guid.NewGuid(), TransferFormat.Xlsx));
    }

    [Fact]
    public async Task Import_Records_Each_Row_As_A_New_Attempt_And_Links_The_Defects()
    {
        var run = await RunWithAsync("TC-1", "TC-2", "TC-3");

        var report = await _transfer.ImportAsync(run.Id, Csv(
            "Code,Result,ActualResult,DurationSeconds,Defects\n" +
            "TC-1,Failed,Button missing,12,Jira:BUG-7\n" +
            "TC-1,Passed,Fixed,9,\n" +
            "tc-2,blocked,,,\n"));

        report.FileErrors.ShouldBeEmpty();
        report.Imported.ShouldBeTrue();
        (report.Total, report.Recorded, report.Skipped, report.Invalid).ShouldBe((3, 3, 0, 0));
        report.Items.ShouldAllBe(i => i.Outcome == ImportOutcome.Recorded);

        var after = await _runs.GetAsync(run.Id);
        var tc1 = after.Items.Single(i => i.TestCaseCode == "TC-1");
        tc1.AttemptCount.ShouldBe(2);
        tc1.CurrentStatus.ShouldBe(TestResultStatus.Passed);
        after.Items.Single(i => i.TestCaseCode == "TC-2").CurrentStatus.ShouldBe(TestResultStatus.Blocked);
        after.Items.Single(i => i.TestCaseCode == "TC-3").CurrentStatus.ShouldBe(TestResultStatus.Untested);

        var attempts = await _runs.GetExecutionsAsync(run.Id, tc1.Id);
        attempts.Select(a => (a.AttemptNumber, a.Status, a.ActualResult, a.DurationSeconds)).ShouldBe(new[]
        {
            (1, TestResultStatus.Failed, (string?)"Button missing", 12),
            (2, TestResultStatus.Passed, "Fixed", 9),
        });
        var defect = attempts[0].DefectLinks.Single();
        (defect.ExternalSystem, defect.IssueKey, defect.IsResolved).ShouldBe(("Jira", "BUG-7", false));
        defect.Severity.ShouldBe(SeverityLevel.High); // taken from the test case, as for a defect entered by hand
    }

    [Fact]
    public async Task The_Exported_Results_Of_One_Run_Can_Be_Imported_Into_Another_Run_Of_The_Same_Test_Cases()
    {
        var source = await RunWithAsync("TC-1", "TC-2");
        await _runs.ExecuteItemAsync(source.Id, source.Items[0].Id, new ExecuteTestItemDto { Status = TestResultStatus.Passed });
        await _runs.ExecuteItemAsync(source.Id, source.Items[1].Id, new ExecuteTestItemDto
        {
            Status = TestResultStatus.Failed,
            Defects = { new AddDefectLinkDto { ExternalSystem = "Jira", IssueKey = "BUG-3" } },
        });

        var target = await _runs.CreateAsync(new CreateTestRunDto
        {
            Title = "Copy",
            Environment = "Production",
            TestCaseIds = source.Items.Select(i => i.TestCaseId).ToList(),
        });

        var export = await _transfer.ExportAsync(source.Id, TransferFormat.Xlsx);
        using var content = new MemoryStream();
        await export.GetStream().CopyToAsync(content);

        var report = await _transfer.ImportAsync(
            target.Id, new ImportTestResultsInput { File = new RemoteStreamContent(new MemoryStream(content.ToArray()), "r.xlsx") });

        report.Recorded.ShouldBe(2);
        report.IgnoredColumns.ShouldBeEmpty();
        var after = await _runs.GetAsync(target.Id);
        after.Items.Select(i => i.CurrentStatus).ShouldBe(new[] { TestResultStatus.Passed, TestResultStatus.Failed });
    }

    [Fact]
    public async Task A_Dry_Run_Matches_The_Rows_And_Records_Nothing()
    {
        var run = await RunWithAsync("TC-1");

        var report = await _transfer.ImportAsync(run.Id, Csv("Code,Result\nTC-1,Passed\n", dryRun: true));

        report.DryRun.ShouldBeTrue();
        report.Imported.ShouldBeFalse();
        report.Recorded.ShouldBe(1);
        (await _runs.GetAsync(run.Id)).Items.Single().AttemptCount.ShouldBe(0);
    }

    [Fact]
    public async Task Rows_Without_A_Result_And_Untested_Rows_Are_Skipped()
    {
        var run = await RunWithAsync("TC-1", "TC-2");

        var report = await _transfer.ImportAsync(run.Id, Csv("Code,Result\nTC-1,Untested\nTC-2,\nTC-2,Passed\n"));

        (report.Recorded, report.Skipped).ShouldBe((1, 2));
        report.Items.Take(2).ShouldAllBe(i => i.Outcome == ImportOutcome.Skipped);
    }

    [Fact]
    public async Task One_Bad_Row_Blocks_The_Whole_File_And_Every_Problem_Is_Reported()
    {
        var run = await RunWithAsync("TC-1", "TC-2");

        var report = await _transfer.ImportAsync(run.Id, Csv(
            "Code,Result,Version\n" +
            "TC-1,Passed,\n" +
            "NOPE-9,Passed,\n" +
            "TC-2,Passed,7\n" +
            "TC-2,Maybe,\n"));

        report.Imported.ShouldBeFalse();
        (report.Recorded, report.Invalid).ShouldBe((1, 3));
        report.Items[1].Messages.Single().ShouldBe("Test case 'NOPE-9' is not in this run.");
        report.Items[2].Messages.Single().ShouldBe("Test case 'TC-2' is in this run, but not with version 7.");
        report.Items[3].Messages.Single().ShouldContain("Use one of: Passed, Failed, Blocked, Skipped");

        (await _runs.GetAsync(run.Id)).Items.ShouldAllBe(i => i.AttemptCount == 0);
    }

    [Fact]
    public async Task A_Code_That_Is_In_The_Run_With_Two_Versions_Needs_The_Version_Column()
    {
        var run = await RunWithAsync("TC-1");
        var testCaseId = run.Items.Single().TestCaseId;

        // A new version of the test case.
        var current = await _testCases.GetAsync(testCaseId);
        await _testCases.UpdateAsync(testCaseId, new CreateUpdateTestCaseDto
        {
            SuiteId = current.SuiteId,
            Code = current.Code,
            Title = "Second title",
            Steps = current.Steps.Select(s => new TestStepDto { Id = s.Id, Action = s.Action, ExpectedResult = s.ExpectedResult }).ToList(),
        });
        await _testCases.ChangeStatusAsync(testCaseId, new ChangeTestCaseStatusDto { TargetStatus = TestCaseStatus.Approved });

        // A run that holds two versions of one test case exists in data made before a test case could be scheduled only once in a run
        // (the manager refuses it now), so it is built here directly, as the import must still read such a run.
        await WithUnitOfWorkAsync(async () =>
        {
            var runs = GetRequiredService<IRepository<TestRun, Guid>>();
            var versions = GetRequiredService<IRepository<TestCaseVersion, Guid>>();
            var entity = await runs.GetAsync(run.Id);
            var second = await versions.GetAsync(v => v.TestCaseId == testCaseId && v.VersionNumber == 2);
            entity.AddItem(second.Id, null);
            await runs.UpdateAsync(entity, autoSave: true);
        });

        var ambiguous = await _transfer.ImportAsync(run.Id, Csv("Code,Result\nTC-1,Passed\n"));
        ambiguous.Items.Single().Messages.Single().ShouldBe("Test case 'TC-1' is in this run more than once; add the Version column.");

        var precise = await _transfer.ImportAsync(run.Id, Csv("Code,Version,Result\nTC-1,2,Passed\n"));
        precise.Recorded.ShouldBe(1);
        var after = await _runs.GetAsync(run.Id);
        after.Items.Single(i => i.VersionNumber == 2).CurrentStatus.ShouldBe(TestResultStatus.Passed);
        after.Items.Single(i => i.VersionNumber == 1).CurrentStatus.ShouldBe(TestResultStatus.Untested);
    }

    [Fact]
    public async Task A_Completed_Run_Takes_No_Results_And_Missing_Columns_Are_File_Errors()
    {
        var run = await RunWithAsync("TC-1");

        (await _transfer.ImportAsync(run.Id, Csv("Code\nTC-1\n"))).FileErrors.Single().ShouldBe("Required column(s) missing: Result.");

        await _runs.CompleteAsync(run.Id);
        var report = await _transfer.ImportAsync(run.Id, Csv("Code,Result\nTC-1,Passed\n"));

        report.Imported.ShouldBeFalse();
        report.FileErrors.Single().ShouldBe("The run is completed and accepts no more attempts.");
    }
}
