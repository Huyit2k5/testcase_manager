using System.Globalization;
using System.Text;
using Acme.TestCaseManagement.Enums;
using Acme.TestCaseManagement.Transfer.Tabular;
using Microsoft.Extensions.Localization;
using Shouldly;
using Xunit;

namespace Acme.TestCaseManagement.Transfer;

/// <summary>How the rows of a test case file are grouped into test cases and checked, before the library is consulted.</summary>
public class TestCaseSheet_Tests
{
    /// <summary>Answers every key with itself, so that a test can see which message was chosen.</summary>
    private sealed class KeyLocalizer : IStringLocalizer
    {
        public LocalizedString this[string name] => new(name, name);

        public LocalizedString this[string name, params object[] arguments] => new(name, name);

        public IEnumerable<LocalizedString> GetAllStrings(bool includeParentCultures) => Enumerable.Empty<LocalizedString>();
    }

    private static readonly TransferMessages Messages = new(new KeyLocalizer());

    private static ParsedSheet Parse(string csv) =>
        TestCaseSheet.Parse(CsvTable.Read(Encoding.UTF8.GetBytes(csv), 1000), Messages);

    [Fact]
    public void Rows_That_Share_A_Code_Are_The_Steps_Of_One_Test_Case()
    {
        var sheet = Parse(
            "Code,Title,Priority,Action,ExpectedResult,TestData\n" +
            "TC-1,Login,Urgent,Open page,Page shown,\n" +
            "TC-1,Login,Urgent,Type user,Name appears,bob\n" +
            "TC-2,Logout,,Click out,Signed out,\n");

        sheet.FileErrors.ShouldBeEmpty();
        sheet.Drafts.Select(d => d.Code).ShouldBe(new[] { "TC-1", "TC-2" });
        sheet.Drafts.SelectMany(d => d.Errors).ShouldBeEmpty();

        var first = sheet.Drafts[0];
        first.Row.ShouldBe(2);
        first.Priority.ShouldBe(PriorityLevel.Urgent);
        first.Steps.Select(s => s.Action).ShouldBe(new[] { "Open page", "Type user" });
        first.Steps[1].TestData.ShouldBe("bob");
        first.Steps[0].TestData.ShouldBeNull();

        var second = sheet.Drafts[1];
        second.Row.ShouldBe(4);
        second.Priority.ShouldBe(PriorityLevel.Medium); // a blank cell is the default
    }

    [Fact]
    public void A_Row_Without_A_Code_Continues_The_Test_Case_Above_It_And_May_Leave_The_Test_Case_Columns_Blank()
    {
        var sheet = Parse(
            "Code,Title,Priority,Action,ExpectedResult\n" +
            "TC-1,Login,High,Step one,Result one\n" +
            ",,,Step two,Result two\n" +
            ",Login,high,Step three,Result three\n");

        sheet.Drafts.Single().Errors.ShouldBeEmpty();
        sheet.Drafts.Single().Steps.Count.ShouldBe(3);
    }

    [Fact]
    public void A_Continuation_Row_That_Contradicts_The_First_Row_Is_An_Error()
    {
        var sheet = Parse(
            "Code,Title,Priority,Action,ExpectedResult\n" +
            "TC-1,Login,High,Step one,Result one\n" +
            "TC-1,Sign in,Low,Step two,Result two\n");

        sheet.Drafts.Single().Errors.ShouldBe(new[] { "Import:Conflict", "Import:Conflict" });
    }

    [Fact]
    public void A_Code_That_Returns_After_Another_Test_Case_Is_An_Error_And_A_Row_With_No_Code_At_The_Start_Is_Too()
    {
        var sheet = Parse(
            "Code,Title,Action,ExpectedResult\n" +
            ",Orphan,A,B\n" +
            "TC-1,One,A,B\n" +
            "TC-2,Two,A,B\n" +
            "tc-1,One again,A,B\n");

        sheet.Drafts[0].Errors.ShouldBe(new[] { "Import:CodeMissing" });
        sheet.Drafts[1].Errors.ShouldBeEmpty();
        sheet.Drafts[2].Errors.ShouldBeEmpty();
        sheet.Drafts[3].Errors.ShouldBe(new[] { "Import:CodeRepeated" });
    }

    [Fact]
    public void Values_Are_Checked_Against_Their_Meaning_And_Length()
    {
        var sheet = Parse(
            "Code,Title,Priority,Severity,Flaky,Action,ExpectedResult\n" +
            "TC-1,Fine,2,Critical,yes,A,B\n" +
            "TC-2,Bad,Huge,5,maybe,A,B\n" +
            $"TC-3,{new string('t', 257)},,,,A,B\n" +
            "TC-4,Half a step,,,,Only an action,\n");

        sheet.Drafts[0].Errors.ShouldBeEmpty();
        sheet.Drafts[0].Priority.ShouldBe(PriorityLevel.High);
        sheet.Drafts[0].Severity.ShouldBe(SeverityLevel.Critical);
        sheet.Drafts[0].IsFlaky.ShouldBeTrue();

        sheet.Drafts[1].Errors.ShouldBe(new[] { "Import:InvalidValue", "Import:InvalidValue", "Import:InvalidValue" });
        sheet.Drafts[2].Errors.ShouldBe(new[] { "Import:TooLong" });
        sheet.Drafts[3].Errors.ShouldBe(new[] { "Import:StepIncomplete" });
    }

    [Fact]
    public void A_Test_Case_May_Have_No_Steps()
    {
        var sheet = Parse("Code,Title,Action,ExpectedResult\nTC-1,No steps yet,,\n");

        sheet.Drafts.Single().Errors.ShouldBeEmpty();
        sheet.Drafts.Single().Steps.ShouldBeEmpty();
    }

    [Fact]
    public void Columns_Are_Found_By_Name_In_Any_Order_And_Case_And_The_Unknown_Ones_Are_Reported()
    {
        var sheet = Parse("Notes,title,CODE,ExpectedResult,Action\nignored,Login,TC-1,Shown,Open\n");

        sheet.FileErrors.ShouldBeEmpty();
        sheet.IgnoredColumns.ShouldBe(new[] { "Notes" });
        sheet.Columns.ShouldContain("Title");
        sheet.Columns.ShouldNotContain("Priority");
        sheet.Drafts.Single().Title.ShouldBe("Login");
        sheet.Drafts.Single().Steps.Single().ExpectedResult.ShouldBe("Shown");
    }

    [Fact]
    public void The_Columns_That_An_Export_Writes_For_Information_Are_Not_Reported_As_Ignored()
    {
        var sheet = Parse("Code,Title,Status,Version,StepNo\nTC-1,Login,Approved,3,1\n");

        sheet.IgnoredColumns.ShouldBeEmpty();
        sheet.Columns.ShouldContain("Status");
    }

    [Fact]
    public void A_File_Without_A_Code_Column_Is_Refused_As_A_Whole()
    {
        var sheet = Parse("Title,Action\nLogin,Open\n");

        sheet.FileErrors.ShouldBe(new[] { "Import:MissingColumns" });
        sheet.Drafts.ShouldBeEmpty();
    }

    [Fact]
    public void Line_Breaks_Inside_A_Cell_Are_Normalized_And_Text_Is_Trimmed()
    {
        var sheet = Parse("Code,Title,Description,Action,ExpectedResult\nTC-1,  Login  ,\"line one\r\nline two  \",A,B\n");

        sheet.Drafts.Single().Title.ShouldBe("Login");
        sheet.Drafts.Single().Description.ShouldBe("line one\nline two");
    }

    [Theory]
    [InlineData("Payments/Cards/Refunds", new[] { "Payments", "Cards", "Refunds" })]
    [InlineData("Root", new[] { "Root" })]
    [InlineData(@"A\/B/C", new[] { "A/B", "C" })]
    [InlineData(@"Back\\slash", new[] { @"Back\slash" })]
    [InlineData(" Spaces / Around ", new[] { "Spaces", "Around" })]
    public void A_Suite_Path_Splits_Into_Names_And_Escapes_Its_Own_Separator(string path, string[] names)
    {
        SuitePath.TryParse(path, out var parsed).ShouldBeTrue();
        parsed.ShouldBe(names);
        SuitePath.TryParse(SuitePath.Format(names), out var again).ShouldBeTrue();
        again.ShouldBe(names);
    }

    [Theory]
    [InlineData("A//B")]
    [InlineData("/A")]
    [InlineData("A/")]
    public void A_Suite_Path_With_An_Empty_Name_Is_Not_Valid(string path)
    {
        SuitePath.TryParse(path, out _).ShouldBeFalse();
    }

    [Fact]
    public void Result_Rows_Are_Read_With_Their_Defects_And_Numbers_And_Checked()
    {
        var parsed = TestResultSheet.Parse(
            CsvTable.Read(
                Encoding.UTF8.GetBytes(
                    "Code,Version,Result,ActualResult,DurationSeconds,Defects,Attempt,ExecutedBy\n" +
                    "TC-1,2,Failed,Crash,45,\"Jira:BUG-1; GitHub:#42\",1,someone\n" +
                    "TC-2,,passed,,,,,\n" +
                    "TC-3,,Untested,,,,,\n" +
                    "TC-4,,Done,,,,,\n" +
                    "TC-5,x,Passed,,-3,,,\n" +
                    "TC-6,,Passed,,,Jira:BUG-9,,\n" +
                    "TC-7,,Failed,,,BUG-9,,\n" +
                    ",,Passed,,,,,\n"),
                1000),
            Messages);

        parsed.FileErrors.ShouldBeEmpty();
        parsed.IgnoredColumns.ShouldBeEmpty();
        parsed.Entries.Count.ShouldBe(8);

        var failed = parsed.Entries[0];
        failed.Errors.ShouldBeEmpty();
        failed.Status.ShouldBe(TestResultStatus.Failed);
        failed.Version.ShouldBe(2);
        failed.DurationSeconds.ShouldBe(45);
        failed.Defects.ShouldBe(new[] { ("Jira", "BUG-1"), ("GitHub", "#42") });

        parsed.Entries[1].Status.ShouldBe(TestResultStatus.Passed);
        parsed.Entries[2].Skip.ShouldBeTrue();
        parsed.Entries[3].Errors.ShouldBe(new[] { "Import:InvalidValue" });
        parsed.Entries[4].Errors.ShouldBe(new[] { "Import:InvalidNumber", "Import:InvalidNumber" });
        parsed.Entries[5].Errors.ShouldBe(new[] { "Import:Results:DefectNeedsFailed" });
        parsed.Entries[6].Errors.ShouldBe(new[] { "Import:Results:DefectFormat" });
        parsed.Entries[7].Errors.ShouldBe(new[] { "Import:ValueRequired" });
    }

    [Fact]
    public void A_Result_File_Needs_The_Code_And_Result_Columns()
    {
        var parsed = TestResultSheet.Parse(CsvTable.Read(Encoding.UTF8.GetBytes("Title\nx\n"), 10), Messages);

        parsed.FileErrors.ShouldBe(new[] { "Import:MissingColumns" });
    }

    [Fact]
    public void Numbers_Are_Always_Read_With_The_Invariant_Culture()
    {
        var previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("vi-VN");
            var parsed = TestResultSheet.Parse(CsvTable.Read(Encoding.UTF8.GetBytes("Code,Result,DurationSeconds\nTC-1,Passed,1234\n"), 10), Messages);
            parsed.Entries.Single().DurationSeconds.ShouldBe(1234);
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }
}
