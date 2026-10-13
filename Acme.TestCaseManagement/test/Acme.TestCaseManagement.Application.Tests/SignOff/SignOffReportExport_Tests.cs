using System.Globalization;
using System.Text;
using Acme.TestCaseManagement.Enums;
using Acme.TestCaseManagement.Plans;
using Acme.TestCaseManagement.Plans.Dtos;
using Acme.TestCaseManagement.Projects;
using Acme.TestCaseManagement.QualityGates;
using Acme.TestCaseManagement.QualityGates.Dtos;
using Acme.TestCaseManagement.Reports;
using Acme.TestCaseManagement.Runs;
using Acme.TestCaseManagement.Runs.Dtos;
using Acme.TestCaseManagement.SignOff;
using Acme.TestCaseManagement.SignOff.Dtos;
using Acme.TestCaseManagement.Suites;
using Acme.TestCaseManagement.Suites.Dtos;
using Acme.TestCaseManagement.TestCases;
using Acme.TestCaseManagement.TestCases.Dtos;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using PdfSharp.Pdf.IO;
using Shouldly;
using Volo.Abp.Content;
using Volo.Abp.Domain.Entities;
using Xunit;

namespace Acme.TestCaseManagement.SignOff;

/// <summary>The sign-off report as a file: a PDF to print and sign, and a workbook to work on, built from what was frozen.</summary>
public class SignOffReportExport_Tests : TestCaseManagementApplicationTestBase
{
    private readonly ISignOffAppService _signOff;
    private readonly IQualityGateAppService _gates;
    private readonly ITestSuiteAppService _suites;
    private readonly ITestCaseAppService _testCases;
    private readonly ITestPlanAppService _plans;
    private readonly ITestRunAppService _runs;
    private readonly IProjectAppService _projects;

    public SignOffReportExport_Tests()
    {
        _signOff = GetRequiredService<ISignOffAppService>();
        _gates = GetRequiredService<IQualityGateAppService>();
        _suites = GetRequiredService<ITestSuiteAppService>();
        _testCases = GetRequiredService<ITestCaseAppService>();
        _plans = GetRequiredService<ITestPlanAppService>();
        _runs = GetRequiredService<ITestRunAppService>();
        _projects = GetRequiredService<IProjectAppService>();
    }

    /// <summary>A plan with a run whose results are given, a gate that those results pass, and a sign-off of it.</summary>
    private async Task<(SignOffReportDto Report, ProjectDto Project)> SignedOffAsync(string name, int passed, int failed)
    {
        var project = await _projects.CreateAsync(new CreateProjectDto { Key = "RPT", Name = "Báo cáo thử" });
        var suite = await _suites.CreateAsync(new CreateTestSuiteDto { Name = "Thư viện", ProjectId = project.Id });
        var plan = await _plans.CreateAsync(new CreateTestPlanDto { Name = name, ProjectId = project.Id });

        var ids = new List<Guid>();
        for (var i = 1; i <= passed + failed; i++)
        {
            var created = await _testCases.CreateAsync(new CreateUpdateTestCaseDto
            {
                SuiteId = suite.Id, Code = $"RPT-{i:000}", Title = $"Kiểm tra số {i}", Priority = i == 1 ? PriorityLevel.Urgent : PriorityLevel.Medium,
                Steps = { new TestStepDto { Action = "Làm", ExpectedResult = "Xong" } },
            });
            await _testCases.ChangeStatusAsync(created.Id, new ChangeTestCaseStatusDto { TargetStatus = TestCaseStatus.Approved });
            ids.Add(created.Id);
        }

        var run = await _runs.CreateAsync(new CreateTestRunDto { TestPlanId = plan.Id, Title = $"{name} run", Environment = "Staging", TestCaseIds = ids });
        var batch = new BatchExecuteTestItemsDto();
        for (var i = 0; i < run.Items.Count; i++)
        {
            batch.Items.Add(new BatchExecuteTestItemDto { TestRunItemId = run.Items[i].Id, Status = i < passed ? TestResultStatus.Passed : TestResultStatus.Failed });
        }

        await _runs.BatchExecuteAsync(run.Id, batch);
        await _gates.CreateAsync(new CreateUpdateQualityGateDto { Name = "Cổng thử", MinPassRate = 50m, RequiredApprovals = 1, IsDefault = true });

        SignOffReportDto report;
        using (ChangeUser(Guid.NewGuid(), "qa.lead"))
        {
            report = await _signOff.SignOffAsync(new StartSignOffDto { TestPlanId = plan.Id, Title = $"Nghiệm thu {name}", ApproverRole = "QA Lead", Comment = "Đạt yêu cầu" });
        }

        return (report, project);
    }

    private static async Task<byte[]> BytesAsync(IRemoteStreamContent content)
    {
        using var buffer = new MemoryStream();
        await using var stream = content.GetStream();
        await stream.CopyToAsync(buffer);
        return buffer.ToArray();
    }

    private static Dictionary<string, List<string[]>> Sheets(byte[] workbook)
    {
        using var stream = new MemoryStream(workbook);
        using var document = SpreadsheetDocument.Open(stream, false);
        var part = document.WorkbookPart!;
        var result = new Dictionary<string, List<string[]>>();
        foreach (var sheet in part.Workbook.Sheets!.Elements<Sheet>())
        {
            var worksheet = (WorksheetPart)part.GetPartById(sheet.Id!.Value!);
            result[sheet.Name!.Value!] = worksheet.Worksheet.Descendants<Row>()
                .Select(row => row.Elements<Cell>().Select(cell => cell.InnerText).ToArray())
                .ToList();
        }

        return result;
    }

    [Fact]
    public async Task The_Workbook_Has_The_Report_The_Criteria_The_Defects_And_The_Approvals_As_They_Were_Frozen()
    {
        var (report, project) = await SignedOffAsync("Sprint 24", passed: 3, failed: 1);

        var file = await _signOff.ExportAsync(report.Id, ReportFormat.Xlsx);

        file.FileName.ShouldBe($"signoff-nghiem-thu-sprint-24-{report.CreationTime:yyyyMMdd}.xlsx");
        file.ContentType.ShouldBe("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet");
        var sheets = Sheets(await BytesAsync(file));
        sheets.Count.ShouldBe(4);

        var overview = sheets.Values.First().SelectMany(r => r).ToList();
        overview.ShouldContain("Nghiệm thu Sprint 24");
        overview.ShouldContain("RPT - Báo cáo thử");
        overview.ShouldContain("Cổng thử");
        overview.ShouldContain("Sprint 24");              // the scope: the plan
        overview.ShouldContain(report.SnapshotHash);
        overview.ShouldContain("75%");                    // the pass rate of 3 in 4

        var criteria = sheets.Values.ElementAt(1);
        criteria.Count.ShouldBe(1 + report.Summary!.Criteria.Count);   // header and one row for each
        var approvals = sheets.Values.ElementAt(3);
        approvals.Count.ShouldBe(2);
        approvals[1].ShouldContain("QA Lead");
        approvals[1].ShouldContain("Đạt yêu cầu");
        approvals[1].ShouldContain(report.Approvals[0].Signature);
        project.Key.ShouldBe("RPT");
    }

    [Fact]
    public async Task The_Same_Report_Is_Written_In_The_Language_Of_The_Caller()
    {
        var (report, _) = await SignedOffAsync("Sprint 25", passed: 2, failed: 0);

        var original = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentUICulture = new CultureInfo("en");
            var english = Sheets(await BytesAsync(await _signOff.ExportAsync(report.Id, ReportFormat.Xlsx)));
            CultureInfo.CurrentUICulture = new CultureInfo("vi");
            var vietnamese = Sheets(await BytesAsync(await _signOff.ExportAsync(report.Id, ReportFormat.Xlsx)));

            english.Values.First().SelectMany(r => r).ShouldContain("TEST SIGN-OFF REPORT");
            english.Keys.ShouldContain("3. Quality gate");
            vietnamese.Values.First().SelectMany(r => r).ShouldContain("BIÊN BẢN NGHIỆM THU KIỂM THỬ");
            vietnamese.Keys.ShouldContain("3. Cổng chất lượng");
        }
        finally
        {
            CultureInfo.CurrentUICulture = original;
        }
    }

    [Fact]
    public async Task The_Pdf_Is_A_Document_With_The_Font_Inside_And_Vietnamese_Does_Not_Break_It()
    {
        var (report, _) = await SignedOffAsync("Sprint 26", passed: 3, failed: 1);

        var original = CultureInfo.CurrentUICulture;
        byte[] bytes;
        try
        {
            CultureInfo.CurrentUICulture = new CultureInfo("vi");
            var file = await _signOff.ExportAsync(report.Id, ReportFormat.Pdf);
            file.ContentType.ShouldBe("application/pdf");
            file.FileName.ShouldEndWith(".pdf");
            bytes = await BytesAsync(file);
        }
        finally
        {
            CultureInfo.CurrentUICulture = original;
        }

        Encoding.ASCII.GetString(bytes, 0, 5).ShouldBe("%PDF-");
        using var stream = new MemoryStream(bytes);
        var pdf = PdfReader.Open(stream, PdfDocumentOpenMode.Import);
        pdf.PageCount.ShouldBeGreaterThanOrEqualTo(1);

        // The font is in the file, so it reads the same on any computer: every font of the first page is Noto Sans (subset).
        var fonts = pdf.Pages[0].Resources.Elements.GetDictionary("/Font")!;
        var names = fonts.Elements.Values
            .Select(value => ((value as PdfSharp.Pdf.Advanced.PdfReference)?.Value as PdfSharp.Pdf.PdfDictionary)?.Elements.GetName("/BaseFont"))
            .ToList();
        names.ShouldNotBeEmpty();
        names.ShouldAllBe(name => name != null && name.Contains("Noto Sans"));

        // The test leaves the file where it can be looked at.
        File.WriteAllBytes(Path.Combine(Path.GetTempPath(), "tcm-signoff-report-sample.pdf"), bytes);
    }

    [Fact]
    public async Task A_Report_That_Is_Not_There_Is_Not_Found_And_A_Project_Out_Of_Reach_Hides_Its_Report()
    {
        await Should.ThrowAsync<EntityNotFoundException>(() => _signOff.ExportAsync(Guid.NewGuid(), ReportFormat.Pdf));

        var (report, _) = await SignedOffAsync("Sprint 27", passed: 2, failed: 0);
        using (ProjectAccessScope.Restrict(Array.Empty<Guid>()))
        {
            await Should.ThrowAsync<EntityNotFoundException>(() => _signOff.ExportAsync(report.Id, ReportFormat.Xlsx));
        }

        (await BytesAsync(await _signOff.ExportAsync(report.Id, ReportFormat.Xlsx))).Length.ShouldBeGreaterThan(0);
    }

    [Fact]
    public void A_File_Name_Is_Made_From_The_Title_Without_Accents_And_Without_Characters_That_Break_A_Path()
    {
        SignOffAppService.FileSlug("Biên bản nghiệm thu Đợt 2/3: build 2.6").ShouldBe("bien-ban-nghiem-thu-dot-2-3-build-2-6");
        SignOffAppService.FileSlug("../../etc/passwd").ShouldBe("etc-passwd");
        SignOffAppService.FileSlug("???").ShouldBe("report");
        SignOffAppService.FileSlug(new string('a', 200)).Length.ShouldBe(60);
    }

    [Fact]
    public void A_Report_Whose_Figures_No_Longer_Match_Its_Hash_Says_So_In_The_File()
    {
        var builder = GetRequiredService<SignOffReportBuilder>();
        var report = new SignOffReportDto { Title = "Đã sửa", Status = SignOffStatus.Approved, IntegrityVerified = false, SnapshotHash = new string('a', 64) };

        var document = builder.Build(report, "-");

        document.General.Single(x => x.Label == "Integrity check").Value.ShouldContain("do NOT match");
        document.Results.ShouldBeEmpty();      // a report without its figures writes no figures
        document.Criteria.ShouldBeEmpty();
    }
}
