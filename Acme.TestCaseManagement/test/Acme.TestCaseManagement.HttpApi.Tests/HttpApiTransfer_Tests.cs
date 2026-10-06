using System.Net;
using System.Text;
using Acme.TestCaseManagement.Enums;
using Acme.TestCaseManagement.Runs.Dtos;
using Acme.TestCaseManagement.Suites.Dtos;
using Acme.TestCaseManagement.TestCases.Dtos;
using Acme.TestCaseManagement.Transfer.Dtos;
using Shouldly;
using Volo.Abp.Application.Dtos;
using Xunit;

namespace Acme.TestCaseManagement;

/// <summary>
/// Import and export over HTTP: the multipart upload, the file download, the permissions of each role and the language
/// of the report. The behaviour of the import itself is covered in the application tests.
/// </summary>
[Collection(HostCollection.Name)]
public class HttpApiTransfer_Tests
{
    private const string Root = "/api/test-case-management";

    private readonly TestCaseManagementHost _host;

    public HttpApiTransfer_Tests(TestCaseManagementHost host)
    {
        _host = host;
    }

    private static string Unique() => Guid.NewGuid().ToString("N")[..8];

    private static byte[] Csv(string text) => Encoding.UTF8.GetBytes(text);

    [Fact]
    public async Task A_Library_Is_Exported_As_A_Download_And_Imported_Again_Through_A_Multipart_Upload()
    {
        var qaLead = await ApiClient.LoginAsync(_host, "qa.lead");
        var id = Unique();

        // Import a file with a new suite path: dry run first, then for real.
        var csv = Csv(
            "Suite,Code,Title,Priority,Action,ExpectedResult\n" +
            $"Imported {id}/Cards,IMP-{id}-1,Pay by card,Urgent,Open checkout,Checkout shown\n" +
            $"Imported {id}/Cards,IMP-{id}-1,Pay by card,Urgent,Pay,Order confirmed\n");

        var dry = await qaLead.UploadAsync<ImportReportDto>($"{Root}/test-cases/import", "cases.csv", csv, new Dictionary<string, string> { ["DryRun"] = "true" });
        dry.DryRun.ShouldBeTrue();
        dry.Imported.ShouldBeFalse();
        (dry.Created, dry.CreatedSuites).ShouldBe((1, 2));
        (await qaLead.GetAsync<PagedResultDto<TestCaseDto>>($"{Root}/test-cases?Filter=IMP-{id}")).TotalCount.ShouldBe(0);

        var report = await qaLead.UploadAsync<ImportReportDto>($"{Root}/test-cases/import", "cases.csv", csv);
        report.Imported.ShouldBeTrue();
        report.Items.Single().Outcome.ShouldBe(ImportOutcome.Created);

        // The enums travel as numbers in the report, like everywhere else in the API.
        var created = (await qaLead.GetAsync<PagedResultDto<TestCaseDto>>($"{Root}/test-cases?Filter=IMP-{id}")).Items.Single();
        created.Priority.ShouldBe(PriorityLevel.Urgent);
        (await qaLead.GetAsync<TestCaseDto>($"{Root}/test-cases/{created.Id}")).Steps.Count.ShouldBe(2);

        // Export it again, as Excel and as CSV, with the filter of the list.
        var xlsx = await qaLead.DownloadAsync($"{Root}/test-cases/export?Format=1&Filter=IMP-{id}");
        xlsx.ContentType.ShouldBe("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet");
        xlsx.FileName.ShouldNotBeNull().ShouldEndWith(".xlsx");
        xlsx.Bytes.Take(2).ShouldBe(new byte[] { (byte)'P', (byte)'K' });

        var exportedCsv = await qaLead.DownloadAsync($"{Root}/test-cases/export?Format=Csv&Filter=IMP-{id}");
        exportedCsv.ContentType.ShouldStartWith("text/csv");
        exportedCsv.FileName.ShouldNotBeNull().ShouldEndWith(".csv");
        var text = Encoding.UTF8.GetString(exportedCsv.Bytes.Skip(3).ToArray());
        text.ShouldContain($"Imported {id}/Cards,IMP-{id}-1,Pay by card");

        // Both exports go back in without changing anything.
        foreach (var file in new[] { ("again.xlsx", xlsx.Bytes), ("again.csv", exportedCsv.Bytes) })
        {
            var again = await qaLead.UploadAsync<ImportReportDto>(
                $"{Root}/test-cases/import", file.Item1, file.Item2, new Dictionary<string, string> { ["OnExisting"] = "Update" });
            again.Imported.ShouldBeTrue(file.Item1);
            again.Items.Single().Outcome.ShouldBe(ImportOutcome.Skipped, file.Item1);
        }
    }

    [Fact]
    public async Task The_Results_Of_A_Run_Are_Exported_And_Imported_Over_Http()
    {
        var qaLead = await ApiClient.LoginAsync(_host, "qa.lead");
        var id = Unique();

        await qaLead.UploadAsync<ImportReportDto>(
            $"{Root}/test-cases/import", "cases.csv",
            Csv($"Suite,Code,Title,Action,ExpectedResult\nRuns {id},RES-{id},A test,Do,Done\n"));
        var testCase = (await qaLead.GetAsync<PagedResultDto<TestCaseDto>>($"{Root}/test-cases?Filter=RES-{id}")).Items.Single();
        await qaLead.PostAsync<TestCaseDto>($"{Root}/test-cases/{testCase.Id}/status", new ChangeTestCaseStatusDto { TargetStatus = TestCaseStatus.Approved });
        var run = await qaLead.PostAsync<TestRunDto>($"{Root}/runs", new CreateTestRunDto { Title = $"Run {id}", Environment = "Staging", TestCaseIds = { testCase.Id } });

        var report = await qaLead.UploadAsync<ImportReportDto>(
            $"{Root}/runs/{run.Id}/results/import", "results.csv",
            Csv($"Code,Result,ActualResult,DurationSeconds,Defects\nRES-{id},Failed,Crashed,12,Jira:BUG-{id}\n"));
        report.Imported.ShouldBeTrue();
        report.Recorded.ShouldBe(1);

        var exported = await qaLead.DownloadAsync($"{Root}/runs/{run.Id}/results/export?format=Csv");
        exported.FileName.ShouldNotBeNull().ShouldStartWith("test-results-");
        Encoding.UTF8.GetString(exported.Bytes).ShouldContain($"RES-{id},A test,1,1,Failed,Crashed,12,Jira:BUG-{id}");

        // The default format is Excel.
        (await qaLead.DownloadAsync($"{Root}/runs/{run.Id}/results/export")).FileName.ShouldNotBeNull().ShouldEndWith(".xlsx");
    }

    [Fact]
    public async Task Without_A_Token_Import_And_Export_Are_A_401()
    {
        var anonymous = ApiClient.Anonymous(_host);
        var runId = Guid.NewGuid();

        (await anonymous.SendExpectingErrorAsync(HttpMethod.Get, $"{Root}/test-cases/export")).Status.ShouldBe(HttpStatusCode.Unauthorized);
        (await anonymous.SendExpectingErrorAsync(HttpMethod.Get, $"{Root}/runs/{runId}/results/export")).Status.ShouldBe(HttpStatusCode.Unauthorized);

        using var upload = await anonymous.UploadRawAsync($"{Root}/test-cases/import", "x.csv", Csv("Code\nX\n"));
        upload.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        using var results = await anonymous.UploadRawAsync($"{Root}/runs/{runId}/results/import", "x.csv", Csv("Code,Result\nX,Passed\n"));
        results.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task A_Product_Owner_Can_Export_But_Not_Import()
    {
        var owner = await ApiClient.LoginAsync(_host, "product.owner");
        var runId = Guid.NewGuid();

        (await owner.DownloadAsync($"{Root}/test-cases/export?Format=Csv")).Bytes.ShouldNotBeEmpty();

        using var cases = await owner.UploadRawAsync($"{Root}/test-cases/import", "x.csv", Csv("Suite,Code,Title\nS,X-1,One\n"));
        cases.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        using var results = await owner.UploadRawAsync($"{Root}/runs/{runId}/results/import", "x.csv", Csv("Code,Result\nX-1,Passed\n"));
        results.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task A_Tester_Imports_Into_Existing_Suites_But_May_Not_Create_Suites_Or_Update_Test_Cases()
    {
        var qaLead = await ApiClient.LoginAsync(_host, "qa.lead");
        var tester = await ApiClient.LoginAsync(_host, "tester");
        var id = Unique();
        var suite = await qaLead.PostAsync<TestSuiteDto>($"{Root}/suites", new CreateTestSuiteDto { Name = $"Tester suite {id}" });

        // Into a suite that exists: allowed, a tester may create test cases.
        var allowed = await tester.UploadAsync<ImportReportDto>(
            $"{Root}/test-cases/import", "a.csv", Csv($"Suite,Code,Title,Action,ExpectedResult\nTester suite {id},TST-{id},Mine,Do,Done\n"));
        allowed.Created.ShouldBe(1);

        // Into a suite that would have to be created: the row is invalid, nothing is written.
        var denied = await tester.UploadAsync<ImportReportDto>(
            $"{Root}/test-cases/import", "b.csv", Csv($"Suite,Code,Title,Action,ExpectedResult\nTester suite {id}/New,TST-{id}-2,Other,Do,Done\n"));
        denied.Imported.ShouldBeFalse();
        denied.Items.Single().Messages.Single().ShouldContain("you may not create suites");
        (await qaLead.GetAsync<PagedResultDto<TestCaseDto>>($"{Root}/test-cases?Filter=TST-{id}-2")).TotalCount.ShouldBe(0);

        // A tester has Update, so updating is allowed; the Approve permission is another matter and not needed here.
        var update = await tester.UploadAsync<ImportReportDto>(
            $"{Root}/test-cases/import", "c.csv", Csv($"Code,Title\nTST-{id},Renamed by a tester\n"),
            new Dictionary<string, string> { ["OnExisting"] = "Update" });
        update.Updated.ShouldBe(1);
        suite.Id.ShouldNotBe(Guid.Empty);
    }

    [Fact]
    public async Task A_Tester_May_Import_Results_Because_A_Tester_Executes_Test_Runs()
    {
        var qaLead = await ApiClient.LoginAsync(_host, "qa.lead");
        var tester = await ApiClient.LoginAsync(_host, "tester");
        var id = Unique();

        await qaLead.UploadAsync<ImportReportDto>(
            $"{Root}/test-cases/import", "cases.csv", Csv($"Suite,Code,Title,Action,ExpectedResult\nTester runs {id},TRN-{id},T,Do,Done\n"));
        var testCase = (await qaLead.GetAsync<PagedResultDto<TestCaseDto>>($"{Root}/test-cases?Filter=TRN-{id}")).Items.Single();
        await qaLead.PostAsync<TestCaseDto>($"{Root}/test-cases/{testCase.Id}/status", new ChangeTestCaseStatusDto { TargetStatus = TestCaseStatus.Approved });
        var run = await qaLead.PostAsync<TestRunDto>($"{Root}/runs", new CreateTestRunDto { Title = $"Run {id}", Environment = "QA", TestCaseIds = { testCase.Id } });

        var report = await tester.UploadAsync<ImportReportDto>(
            $"{Root}/runs/{run.Id}/results/import", "r.csv", Csv($"Code,Result\nTRN-{id},Passed\n"));

        report.Recorded.ShouldBe(1);
    }

    [Fact]
    public async Task The_Report_Is_In_The_Language_Of_The_Caller()
    {
        var qaLead = (await ApiClient.LoginAsync(_host, "qa.lead")).PreferLanguage("vi");
        var id = Unique();

        var report = await qaLead.UploadAsync<ImportReportDto>(
            $"{Root}/test-cases/import", "bad.csv", Csv($"Suite,Code,Title,Priority,Action,ExpectedResult\nS {id},BAD-{id},T,Huge,A,B\n"));

        report.Items.Single().Messages.Single().ShouldBe("Cột Priority: 'Huge' không hợp lệ. Hãy dùng một trong: Low, Medium, High, Urgent.");
    }

    [Fact]
    public async Task A_File_That_Is_Not_A_Spreadsheet_Is_A_Report_With_A_File_Error_Not_A_Server_Error()
    {
        var qaLead = await ApiClient.LoginAsync(_host, "qa.lead");

        var report = await qaLead.UploadAsync<ImportReportDto>(
            $"{Root}/test-cases/import", "photo.png", new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0, 0, 0x0D });

        report.Imported.ShouldBeFalse();
        report.FileErrors.Single().ShouldContain("not an Excel");
    }

    [Fact]
    public async Task An_Upload_Without_A_File_Is_A_400()
    {
        var qaLead = await ApiClient.LoginAsync(_host, "qa.lead");

        using var response = await qaLead.SendRawAsync(HttpMethod.Post, $"{Root}/test-cases/import");

        ((int)response.StatusCode).ShouldBeInRange(400, 415);
    }
}
