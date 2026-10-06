using System.Text;
using Acme.TestCaseManagement.Enums;
using Acme.TestCaseManagement.Suites;
using Acme.TestCaseManagement.Suites.Dtos;
using Acme.TestCaseManagement.TestCases;
using Acme.TestCaseManagement.TestCases.Dtos;
using Acme.TestCaseManagement.Transfer.Dtos;
using Acme.TestCaseManagement.Transfer.Tabular;
using Microsoft.Extensions.Options;
using Shouldly;
using Volo.Abp;
using Volo.Abp.Content;
using Xunit;

namespace Acme.TestCaseManagement.Transfer;

public class TestCaseTransferAppService_Tests : TestCaseManagementApplicationTestBase
{
    static TestCaseTransferAppService_Tests()
    {
        // The report messages are asserted in English, whatever the language of the machine that runs the tests.
        System.Globalization.CultureInfo.DefaultThreadCurrentCulture = new System.Globalization.CultureInfo("en");
        System.Globalization.CultureInfo.DefaultThreadCurrentUICulture = new System.Globalization.CultureInfo("en");
    }

    private readonly ITestSuiteAppService _suites;
    private readonly ITestCaseAppService _testCases;
    private readonly ITestCaseTransferAppService _transfer;

    public TestCaseTransferAppService_Tests()
    {
        _suites = GetRequiredService<ITestSuiteAppService>();
        _testCases = GetRequiredService<ITestCaseAppService>();
        _transfer = GetRequiredService<ITestCaseTransferAppService>();
    }

    private const string Header = "Suite,Code,Title,Priority,Action,ExpectedResult,TestData\n";

    private static ImportTestCasesInput Csv(string text, ImportConflictMode mode = ImportConflictMode.Skip, bool dryRun = false, Guid? suiteId = null) =>
        new()
        {
            File = new RemoteStreamContent(new MemoryStream(Encoding.UTF8.GetBytes(text)), "cases.csv", "text/csv"),
            OnExisting = mode,
            DryRun = dryRun,
            DefaultSuiteId = suiteId,
        };

    private static ImportTestCasesInput File(byte[] bytes, ImportConflictMode mode = ImportConflictMode.Skip) =>
        new() { File = new RemoteStreamContent(new MemoryStream(bytes), "cases.bin", "application/octet-stream"), OnExisting = mode };

    private static async Task<byte[]> BytesOf(IRemoteStreamContent content)
    {
        using var stream = new MemoryStream();
        await content.GetStream().CopyToAsync(stream);
        return stream.ToArray();
    }

    private static async Task<Table> TableOf(IRemoteStreamContent content) =>
        TableFile.Read(await BytesOf(content), new TestCaseManagementTransferOptions());

    private async Task<TestCaseDto> CreateCaseAsync(Guid suiteId, string code, int steps = 2, bool approve = false)
    {
        var dto = new CreateUpdateTestCaseDto
        {
            SuiteId = suiteId,
            Code = code,
            Title = $"Title of {code}",
            Description = "Does a thing",
            Priority = PriorityLevel.High,
            Severity = SeverityLevel.Critical,
            Kind = TestKind.Security,
            Layer = TestLayer.Integration,
            ExecutionType = ExecutionType.Hybrid,
            AutomationId = $"auto.{code}",
            IsFlaky = true,
        };

        for (var i = 1; i <= steps; i++)
        {
            dto.Steps.Add(new TestStepDto { Action = $"Action {i} of {code}", ExpectedResult = $"Expected {i}", TestData = i == 1 ? "data" : null });
        }

        var created = await _testCases.CreateAsync(dto);
        return approve
            ? await _testCases.ChangeStatusAsync(created.Id, new ChangeTestCaseStatusDto { TargetStatus = TestCaseStatus.Approved })
            : created;
    }

    private async Task<List<TestCaseDto>> AllCasesAsync() =>
        (await _testCases.GetListAsync(new GetTestCaseListInput { MaxResultCount = 1000 })).Items.ToList();

    // ---- export

    [Fact]
    public async Task Export_Writes_One_Row_Per_Step_With_The_Suite_Path_And_Repeats_The_Test_Case_Columns()
    {
        var payments = await _suites.CreateAsync(new CreateTestSuiteDto { Name = "Payments" });
        var cards = await _suites.CreateAsync(new CreateTestSuiteDto { Name = "Cards/Debit", ParentId = payments.Id });
        await CreateCaseAsync(cards.Id, "PAY-1", steps: 2, approve: true);
        await CreateCaseAsync(payments.Id, "PAY-2", steps: 0);

        var export = await _transfer.ExportAsync(new ExportTestCasesInput { Format = TransferFormat.Csv });

        export.FileName.ShouldEndWith(".csv");
        export.ContentType.ShouldStartWith("text/csv");

        var table = await TableOf(export);
        table.Header.ShouldBe(TestCaseSheet.ExportColumns);
        table.Rows.Count.ShouldBe(3); // two steps of PAY-1 and the one row of PAY-2, which has no step

        table.Cell(0, "Suite").ShouldBe(@"Payments/Cards\/Debit");
        table.Cell(0, "Code").ShouldBe("PAY-1");
        table.Cell(0, "Priority").ShouldBe("High");
        table.Cell(0, "Kind").ShouldBe("Security");
        table.Cell(0, "ExecutionType").ShouldBe("Hybrid");
        table.Cell(0, "Flaky").ShouldBe("true");
        table.Cell(0, "Status").ShouldBe("Approved");
        table.Cell(0, "Version").ShouldBe("1");
        table.Cell(0, "StepNo").ShouldBe("1");
        table.Cell(0, "Action").ShouldBe("Action 1 of PAY-1");
        table.Cell(0, "TestData").ShouldBe("data");
        table.Cell(1, "Code").ShouldBe("PAY-1");
        table.Cell(1, "StepNo").ShouldBe("2");
        table.Cell(1, "TestData").ShouldBe(string.Empty);

        table.Cell(2, "Suite").ShouldBe("Payments");
        table.Cell(2, "Code").ShouldBe("PAY-2");
        table.Cell(2, "Action").ShouldBe(string.Empty);
    }

    [Fact]
    public async Task Export_Applies_The_Filters_Of_The_Test_Case_List()
    {
        var root = await _suites.CreateAsync(new CreateTestSuiteDto { Name = "Root" });
        var child = await _suites.CreateAsync(new CreateTestSuiteDto { Name = "Child", ParentId = root.Id });
        var other = await _suites.CreateAsync(new CreateTestSuiteDto { Name = "Other" });
        await CreateCaseAsync(root.Id, "A-1", approve: true);
        await CreateCaseAsync(child.Id, "A-2");
        await CreateCaseAsync(other.Id, "B-1");

        async Task<string[]> Codes(ExportTestCasesInput input)
        {
            input.Format = TransferFormat.Csv;
            var table = await TableOf(await _transfer.ExportAsync(input));
            return table.Rows.Select((_, i) => table.Cell(i, "Code")).Distinct().OrderBy(c => c).ToArray();
        }

        (await Codes(new ExportTestCasesInput())).ShouldBe(new[] { "A-1", "A-2", "B-1" });
        (await Codes(new ExportTestCasesInput { SuiteId = root.Id })).ShouldBe(new[] { "A-1", "A-2" });
        (await Codes(new ExportTestCasesInput { SuiteId = root.Id, IncludeDescendantSuites = false })).ShouldBe(new[] { "A-1" });
        (await Codes(new ExportTestCasesInput { Status = TestCaseStatus.Approved })).ShouldBe(new[] { "A-1" });
        (await Codes(new ExportTestCasesInput { Filter = "b-1" })).ShouldBe(new[] { "B-1" });
        (await Codes(new ExportTestCasesInput { SuiteId = Guid.NewGuid() })).ShouldBeEmpty();
    }

    [Fact]
    public async Task Export_Is_Refused_When_It_Would_Be_Larger_Than_The_Limit()
    {
        var suite = await _suites.CreateAsync(new CreateTestSuiteDto { Name = "S" });
        await CreateCaseAsync(suite.Id, "X-1");
        await CreateCaseAsync(suite.Id, "X-2");
        var options = GetRequiredService<IOptions<TestCaseManagementTransferOptions>>().Value;

        options.MaxExportTestCases = 1;
        try
        {
            var exception = await Should.ThrowAsync<BusinessException>(() => _transfer.ExportAsync(new ExportTestCasesInput()));
            exception.Code.ShouldBe(TestCaseManagementErrorCodes.ExportTooLarge);
        }
        finally
        {
            options.MaxExportTestCases = new TestCaseManagementTransferOptions().MaxExportTestCases;
        }
    }

    [Theory]
    [InlineData(TransferFormat.Csv)]
    [InlineData(TransferFormat.Xlsx)]
    public async Task What_Is_Exported_Can_Be_Imported_Again_And_Changes_Nothing(TransferFormat format)
    {
        var suite = await _suites.CreateAsync(new CreateTestSuiteDto { Name = "Library" });
        await CreateCaseAsync(suite.Id, "TC-1", steps: 3, approve: true);
        await CreateCaseAsync(suite.Id, "TC-2", steps: 0);
        var before = await AllCasesAsync();

        var export = await BytesOf(await _transfer.ExportAsync(new ExportTestCasesInput { Format = format }));
        var report = await _transfer.ImportAsync(File(export, ImportConflictMode.Update));

        report.FileErrors.ShouldBeEmpty();
        report.Items.ShouldAllBe(item => item.Outcome == ImportOutcome.Skipped);
        report.Total.ShouldBe(2);
        report.Skipped.ShouldBe(2);
        report.Imported.ShouldBeTrue();

        // The approved test case did not get a new version, because nothing changed.
        var after = await AllCasesAsync();
        after.Select(c => (c.Code, c.CurrentVersion, c.LastModificationTime)).ShouldBe(before.Select(c => (c.Code, c.CurrentVersion, c.LastModificationTime)));
    }

    [Fact]
    public async Task An_Export_Imported_Into_An_Empty_Library_Rebuilds_The_Test_Cases()
    {
        var root = await _suites.CreateAsync(new CreateTestSuiteDto { Name = "Payments" });
        var child = await _suites.CreateAsync(new CreateTestSuiteDto { Name = "Cards", ParentId = root.Id });
        var original = await CreateCaseAsync(child.Id, "PAY-1", steps: 3);
        var export = await BytesOf(await _transfer.ExportAsync(new ExportTestCasesInput { Format = TransferFormat.Xlsx }));

        // Delete the test case, then bring it back from the file.
        await _testCases.DeleteAsync(original.Id);
        (await AllCasesAsync()).ShouldBeEmpty();

        var report = await _transfer.ImportAsync(File(export));
        report.Created.ShouldBe(1);

        var restored = (await AllCasesAsync()).Single();
        restored.SuiteId.ShouldBe(child.Id);
        restored.Title.ShouldBe(original.Title);
        restored.Description.ShouldBe(original.Description);
        restored.Priority.ShouldBe(PriorityLevel.High);
        restored.Severity.ShouldBe(SeverityLevel.Critical);
        restored.Kind.ShouldBe(TestKind.Security);
        restored.Layer.ShouldBe(TestLayer.Integration);
        restored.ExecutionType.ShouldBe(ExecutionType.Hybrid);
        restored.AutomationId.ShouldBe("auto.PAY-1");
        restored.IsFlaky.ShouldBeTrue();
        restored.Status.ShouldBe(TestCaseStatus.Draft);

        var full = await _testCases.GetAsync(restored.Id);
        full.Steps.Select(s => (s.StepOrder, s.Action, s.ExpectedResult, s.TestData)).ShouldBe(new[]
        {
            (1, "Action 1 of PAY-1", "Expected 1", (string?)"data"),
            (2, "Action 2 of PAY-1", "Expected 2", null),
            (3, "Action 3 of PAY-1", "Expected 3", null),
        });
    }

    // ---- import: creating

    [Fact]
    public async Task Import_Creates_The_Missing_Suites_And_Draft_Test_Cases_With_Ordered_Steps()
    {
        var report = await _transfer.ImportAsync(Csv(
            Header +
            "Payments/Cards,PAY-1,Pay by card,Urgent,Open checkout,Checkout is shown,\n" +
            "Payments/Cards,PAY-1,Pay by card,Urgent,Enter card,Card accepted,4111\n" +
            "Payments,PAY-2,Pay by cash,,Choose cash,Cash chosen,\n"));

        report.FileErrors.ShouldBeEmpty();
        report.Imported.ShouldBeTrue();
        report.Total.ShouldBe(2);
        report.Created.ShouldBe(2);
        report.CreatedSuites.ShouldBe(2);
        report.Items.Select(i => (i.Row, i.Code, i.Outcome)).ShouldBe(new[]
        {
            (2, (string?)"PAY-1", ImportOutcome.Created),
            (4, "PAY-2", ImportOutcome.Created),
        });

        var tree = await _suites.GetTreeAsync();
        var payments = tree.Single();
        payments.Name.ShouldBe("Payments");
        payments.Children.Single().Name.ShouldBe("Cards");
        payments.TestCaseCount.ShouldBe(1);
        payments.Children.Single().TestCaseCount.ShouldBe(1);

        var pay1 = (await AllCasesAsync()).Single(c => c.Code == "PAY-1");
        pay1.Status.ShouldBe(TestCaseStatus.Draft);
        pay1.Priority.ShouldBe(PriorityLevel.Urgent);
        pay1.SuiteId.ShouldBe(payments.Children.Single().Id);
        (await _testCases.GetAsync(pay1.Id)).Steps.Select(s => s.Action).ShouldBe(new[] { "Open checkout", "Enter card" });
        (await AllCasesAsync()).Single(c => c.Code == "PAY-2").Priority.ShouldBe(PriorityLevel.Medium);
    }

    [Fact]
    public async Task Import_Reads_Excel_Files_Too()
    {
        var table = new Table(
            new[] { "Suite", "Code", "Title", "Action", "ExpectedResult" },
            new IReadOnlyList<string>[] { new[] { "Auth", "AUTH-1", "Đăng nhập thành công", "Nhập tên", "Hiện trang chủ" } });

        var report = await _transfer.ImportAsync(File(XlsxTable.Write(table, "Test cases")));

        report.Created.ShouldBe(1);
        (await AllCasesAsync()).Single().Title.ShouldBe("Đăng nhập thành công");
    }

    [Fact]
    public async Task A_Dry_Run_Reports_What_Would_Happen_And_Writes_Nothing()
    {
        var report = await _transfer.ImportAsync(Csv(Header + "New/Suite,TC-1,One,,A,B,\n", dryRun: true));

        report.DryRun.ShouldBeTrue();
        report.Imported.ShouldBeFalse();
        report.Created.ShouldBe(1);
        report.CreatedSuites.ShouldBe(2);
        (await AllCasesAsync()).ShouldBeEmpty();
        (await _suites.GetTreeAsync()).ShouldBeEmpty();

        // The same file, for real, gives the same numbers.
        var real = await _transfer.ImportAsync(Csv(Header + "New/Suite,TC-1,One,,A,B,\n"));
        real.Imported.ShouldBeTrue();
        (real.Created, real.CreatedSuites).ShouldBe((report.Created, report.CreatedSuites));
    }

    [Fact]
    public async Task One_Invalid_Row_Blocks_The_Whole_File_And_Every_Problem_Is_Reported()
    {
        var report = await _transfer.ImportAsync(Csv(
            Header +
            "Good,TC-1,Fine,,A,B,\n" +
            "Good,TC-2,Bad priority,Huge,A,B,\n" +
            "Good,TC-3,,,A,B,\n" +
            "A//B,TC-4,Bad suite,,A,B,\n"));

        report.Imported.ShouldBeFalse();
        report.Invalid.ShouldBe(3);
        report.Created.ShouldBe(1); // "would create": counted, but not written
        report.CreatedSuites.ShouldBe(0);
        report.Items.Select(i => (i.Code, i.Outcome)).ShouldBe(new[]
        {
            ("TC-1", ImportOutcome.Created), ("TC-2", ImportOutcome.Invalid), ("TC-3", ImportOutcome.Invalid), ("TC-4", ImportOutcome.Invalid),
        }.Select(x => ((string?)x.Item1, x.Item2)));
        report.Items.Where(i => i.Outcome == ImportOutcome.Invalid).ShouldAllBe(i => i.Messages.Count > 0);

        (await AllCasesAsync()).ShouldBeEmpty();
        (await _suites.GetTreeAsync()).ShouldBeEmpty();
    }

    [Fact]
    public async Task The_Messages_Of_The_Report_Are_Localized_And_Name_The_Column_And_The_Value()
    {
        var report = await _transfer.ImportAsync(Csv(Header + "S,TC-1,Title,Huge,A,B,\n"));

        report.Items.Single().Messages.Single().ShouldBe(
            "Column Priority: 'Huge' is not valid. Use one of: Low, Medium, High, Urgent.");

        using (CultureScope("vi"))
        {
            var vietnamese = await _transfer.ImportAsync(Csv(Header + "S,TC-1,Title,Huge,A,B,\n"));
            vietnamese.Items.Single().Messages.Single().ShouldBe(
                "Cột Priority: 'Huge' không hợp lệ. Hãy dùng một trong: Low, Medium, High, Urgent.");
        }
    }

    private static IDisposable CultureScope(string culture)
    {
        var previous = (System.Globalization.CultureInfo.CurrentCulture, System.Globalization.CultureInfo.CurrentUICulture);
        var info = new System.Globalization.CultureInfo(culture);
        System.Globalization.CultureInfo.CurrentCulture = info;
        System.Globalization.CultureInfo.CurrentUICulture = info;
        return new ActionOnDispose(() =>
        {
            System.Globalization.CultureInfo.CurrentCulture = previous.CurrentCulture;
            System.Globalization.CultureInfo.CurrentUICulture = previous.CurrentUICulture;
        });
    }

    private sealed class ActionOnDispose : IDisposable
    {
        private readonly Action _action;

        public ActionOnDispose(Action action) => _action = action;

        public void Dispose() => _action();
    }

    // ---- import: suites

    [Fact]
    public async Task Rows_Without_A_Suite_Go_To_The_Default_Suite_And_Without_One_They_Are_Invalid()
    {
        var suite = await _suites.CreateAsync(new CreateTestSuiteDto { Name = "Inbox" });
        const string text = "Code,Title,Action,ExpectedResult\nTC-1,One,A,B\n";

        var refused = await _transfer.ImportAsync(Csv(text));
        refused.Items.Single().Messages.Single().ShouldBe("No Suite is given and no default suite is chosen.");

        var accepted = await _transfer.ImportAsync(Csv(text, suiteId: suite.Id));
        accepted.Created.ShouldBe(1);
        (await AllCasesAsync()).Single().SuiteId.ShouldBe(suite.Id);

        var unknown = await _transfer.ImportAsync(Csv(text.Replace("TC-1", "TC-2"), suiteId: Guid.NewGuid()));
        unknown.FileErrors.Single().ShouldBe("The default suite does not exist.");
    }

    [Fact]
    public async Task A_Suite_Path_That_Matches_Two_Suites_Is_Ambiguous()
    {
        await _suites.CreateAsync(new CreateTestSuiteDto { Name = "Twin" });
        await _suites.CreateAsync(new CreateTestSuiteDto { Name = "twin" });

        var report = await _transfer.ImportAsync(Csv(Header + "TWIN,TC-1,One,,A,B,\n"));

        report.Items.Single().Messages.Single().ShouldBe("Suite path 'TWIN' matches more than one suite.");
    }

    [Fact]
    public async Task Suite_Names_Match_Without_Regard_To_Case_And_New_Suites_Are_Shared_By_The_Rows_That_Need_Them()
    {
        await _suites.CreateAsync(new CreateTestSuiteDto { Name = "Payments" });

        var report = await _transfer.ImportAsync(Csv(
            Header +
            "payments/Refunds,TC-1,One,,A,B,\n" +
            "PAYMENTS/refunds,TC-2,Two,,A,B,\n" +
            "payments/Refunds/Partial,TC-3,Three,,A,B,\n"));

        report.CreatedSuites.ShouldBe(2); // Refunds and Partial; Payments existed
        var tree = await _suites.GetTreeAsync();
        tree.Single().Name.ShouldBe("Payments");
        tree.Single().Children.Single().Name.ShouldBe("Refunds");
        tree.Single().Children.Single().TestCaseCount.ShouldBe(2);
        tree.Single().Children.Single().Children.Single().Name.ShouldBe("Partial");
    }

    // ---- import: existing test cases

    [Fact]
    public async Task An_Existing_Code_Is_Skipped_By_Default()
    {
        var suite = await _suites.CreateAsync(new CreateTestSuiteDto { Name = "S" });
        var existing = await CreateCaseAsync(suite.Id, "TC-1");

        var report = await _transfer.ImportAsync(Csv(Header + "S,TC-1,A new title,,X,Y,\nS,TC-2,Brand new,,X,Y,\n"));

        report.Skipped.ShouldBe(1);
        report.Created.ShouldBe(1);
        report.Items[0].Messages.Single().ShouldBe("A test case with this code already exists; it is left as it is.");
        (await _testCases.GetAsync(existing.Id)).Title.ShouldBe(existing.Title);
    }

    [Fact]
    public async Task In_Update_Mode_The_Existing_Test_Case_Takes_What_The_File_Says_And_Keeps_The_Identity_Of_Its_Steps()
    {
        var suite = await _suites.CreateAsync(new CreateTestSuiteDto { Name = "S" });
        var existing = await CreateCaseAsync(suite.Id, "TC-1", steps: 2);
        var stepIds = (await _testCases.GetAsync(existing.Id)).Steps.Select(s => s.Id).ToList();

        var report = await _transfer.ImportAsync(Csv(
            Header +
            "S,TC-1,Renamed,Low,New first step,New result,\n" +
            "S,TC-1,Renamed,Low,Second step,Second result,\n" +
            "S,TC-1,Renamed,Low,A third step,Third result,\n",
            ImportConflictMode.Update));

        report.Updated.ShouldBe(1);
        var updated = await _testCases.GetAsync(existing.Id);
        updated.Title.ShouldBe("Renamed");
        updated.Priority.ShouldBe(PriorityLevel.Low);
        updated.Steps.Select(s => s.Action).ShouldBe(new[] { "New first step", "Second step", "A third step" });
        updated.Steps.Take(2).Select(s => s.Id).ShouldBe(stepIds); // same positions, same steps
        updated.Steps[2].Id.ShouldNotBe(stepIds[0]);
        updated.Status.ShouldBe(TestCaseStatus.Draft);
    }

    [Fact]
    public async Task Updating_An_Approved_Test_Case_Publishes_A_New_Version_With_A_Change_Summary_Naming_The_File()
    {
        var suite = await _suites.CreateAsync(new CreateTestSuiteDto { Name = "S" });
        var approved = await CreateCaseAsync(suite.Id, "TC-1", steps: 1, approve: true);
        approved.CurrentVersion.ShouldBe(1);

        var input = Csv(Header + "S,TC-1,Better title,,Action 1 of TC-1,Expected 1,data\n", ImportConflictMode.Update);
        input.File = new RemoteStreamContent(new MemoryStream(Encoding.UTF8.GetBytes(Header + "S,TC-1,Better title,,Action 1 of TC-1,Expected 1,data\n")), "release-2.csv");
        await _transfer.ImportAsync(input);

        var versions = await _testCases.GetVersionsAsync(approved.Id);
        versions.Select(v => v.VersionNumber).ShouldBe(new[] { 2, 1 });
        versions[0].ChangeSummary.ShouldBe("Imported from release-2.csv");
        versions[0].Title.ShouldBe("Better title");
        versions[1].Title.ShouldBe(approved.Title); // the old version is frozen
    }

    [Fact]
    public async Task A_Column_That_The_File_Does_Not_Have_Leaves_The_Existing_Test_Case_Alone()
    {
        var suite = await _suites.CreateAsync(new CreateTestSuiteDto { Name = "S" });
        var existing = await CreateCaseAsync(suite.Id, "TC-1", steps: 2);

        // Only the title is given: the priority, the steps and the rest stay as they are.
        var report = await _transfer.ImportAsync(Csv("Code,Title\nTC-1,Only the title changes\n", ImportConflictMode.Update));

        report.Updated.ShouldBe(1);
        var after = await _testCases.GetAsync(existing.Id);
        after.Title.ShouldBe("Only the title changes");
        after.Priority.ShouldBe(PriorityLevel.High);
        after.Description.ShouldBe("Does a thing");
        after.AutomationId.ShouldBe("auto.TC-1");
        after.IsFlaky.ShouldBeTrue();
        after.SuiteId.ShouldBe(suite.Id);
        after.Steps.Count.ShouldBe(2);
    }

    [Fact]
    public async Task A_Blank_Cell_In_A_Column_The_File_Has_Clears_The_Value()
    {
        var suite = await _suites.CreateAsync(new CreateTestSuiteDto { Name = "S" });
        var existing = await CreateCaseAsync(suite.Id, "TC-1");

        await _transfer.ImportAsync(Csv("Code,Description,AutomationId,Flaky\nTC-1,,,\n", ImportConflictMode.Update));

        var after = await _testCases.GetAsync(existing.Id);
        after.Description.ShouldBeNull();
        after.AutomationId.ShouldBeNull();
        after.IsFlaky.ShouldBeFalse();
    }

    [Fact]
    public async Task An_Existing_Test_Case_Is_Not_Moved_To_The_Default_Suite_But_Can_Be_Moved_By_The_Suite_Column()
    {
        var home = await _suites.CreateAsync(new CreateTestSuiteDto { Name = "Home" });
        var inbox = await _suites.CreateAsync(new CreateTestSuiteDto { Name = "Inbox" });
        var existing = await CreateCaseAsync(home.Id, "TC-1");

        await _transfer.ImportAsync(Csv("Code,Title\nTC-1,Same title\n", ImportConflictMode.Update, suiteId: inbox.Id));
        (await _testCases.GetAsync(existing.Id)).SuiteId.ShouldBe(home.Id);

        await _transfer.ImportAsync(Csv("Suite,Code\nInbox,TC-1\n", ImportConflictMode.Update));
        (await _testCases.GetAsync(existing.Id)).SuiteId.ShouldBe(inbox.Id);
    }

    [Fact]
    public async Task The_Status_Column_Is_Never_Imported()
    {
        var suite = await _suites.CreateAsync(new CreateTestSuiteDto { Name = "S" });

        await _transfer.ImportAsync(Csv("Suite,Code,Title,Status,Version,Action,ExpectedResult\nS,TC-1,One,Approved,7,A,B\n"));

        var created = (await AllCasesAsync()).Single();
        created.Status.ShouldBe(TestCaseStatus.Draft);
        created.CurrentVersion.ShouldBe(0);
    }

    // ---- import: the file itself

    [Fact]
    public async Task A_File_That_Is_Not_A_Spreadsheet_Is_Refused_With_A_Message()
    {
        var oldXls = new byte[] { 0xD0, 0xCF, 0x11, 0xE0, 0xA1, 0xB1, 0x1A, 0xE1, 0, 0, 0, 0, 0, 0, 0, 0 };

        var report = await _transfer.ImportAsync(File(oldXls));

        report.Imported.ShouldBeFalse();
        report.FileErrors.Single().ShouldContain("not an Excel (.xlsx) or CSV file");
    }

    [Fact]
    public async Task A_Missing_Code_Column_An_Empty_File_And_Too_Many_Rows_Are_File_Errors()
    {
        (await _transfer.ImportAsync(Csv("Title,Action\nOne,A\n"))).FileErrors.Single().ShouldBe("Required column(s) missing: Code.");
        (await _transfer.ImportAsync(Csv(""))).FileErrors.Single().ShouldBe("The file has no header row.");

        var options = GetRequiredService<IOptions<TestCaseManagementTransferOptions>>().Value;
        options.MaxRows = 2;
        try
        {
            var report = await _transfer.ImportAsync(Csv(Header + "S,A-1,t,,a,b,\nS,A-2,t,,a,b,\nS,A-3,t,,a,b,\n"));
            report.FileErrors.Single().ShouldBe("The file has more than 2 data rows.");
        }
        finally
        {
            options.MaxRows = new TestCaseManagementTransferOptions().MaxRows;
        }
    }

    [Fact]
    public async Task Columns_That_The_Import_Does_Not_Use_Are_Reported_But_Do_Not_Stop_It()
    {
        var report = await _transfer.ImportAsync(Csv("Suite,Code,Title,Owner,Sprint\nS,TC-1,One,me,12\n"));

        report.Created.ShouldBe(1);
        report.IgnoredColumns.ShouldBe(new[] { "Owner", "Sprint" });
    }

    [Fact]
    public async Task A_Csv_With_Semicolons_And_A_Byte_Order_Mark_As_Excel_Saves_It_In_Some_Regions_Is_Read()
    {
        var text = "Suite;Code;Title;Action;ExpectedResult\r\nS;TC-1;Đăng nhập, rồi thoát;Mở trang;Thấy trang\r\n";
        var bytes = Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(text)).ToArray();

        var report = await _transfer.ImportAsync(File(bytes));

        report.Created.ShouldBe(1);
        (await AllCasesAsync()).Single().Title.ShouldBe("Đăng nhập, rồi thoát");
    }

    [Fact]
    public async Task A_Formula_Guard_In_A_Csv_Cell_Is_Removed_And_Is_Added_Again_On_Export()
    {
        var suite = await _suites.CreateAsync(new CreateTestSuiteDto { Name = "S" });

        await _transfer.ImportAsync(Csv("Suite,Code,Title,Action,ExpectedResult\nS,TC-1,'=HYPERLINK(\"x\"),Do,Done\n"));

        (await AllCasesAsync()).Single().Title.ShouldBe("=HYPERLINK(\"x\")");

        var csv = Encoding.UTF8.GetString(await BytesOf(await _transfer.ExportAsync(new ExportTestCasesInput { Format = TransferFormat.Csv })));
        csv.ShouldContain("'=HYPERLINK");
    }
}
