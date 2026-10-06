using System.IO.Compression;
using System.Text;
using Acme.TestCaseManagement.Enums;
using Acme.TestCaseManagement.Transfer;
using Acme.TestCaseManagement.Transfer.Tabular;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using Shouldly;
using Xunit;
using Table = Acme.TestCaseManagement.Transfer.Tabular.Table;

namespace Acme.TestCaseManagement.Transfer;

/// <summary>The Excel and CSV readers and writers, without any application service around them.</summary>
public class Tabular_Tests
{
    private static readonly TestCaseManagementTransferOptions Limits = new();

    private static Table TableOf(string[] header, params string[][] rows) =>
        new(header, rows.Select(row => (IReadOnlyList<string>)row).ToList());

    private static byte[] Bytes(string text, bool bom = false) =>
        (bom ? Encoding.UTF8.GetPreamble() : Array.Empty<byte>()).Concat(Encoding.UTF8.GetBytes(text)).ToArray();

    private static string Text(IReadOnlyList<string> row, int index) => index < row.Count ? row[index] : string.Empty;

    // ---- CSV

    [Fact]
    public void Csv_Reads_Quoted_Fields_With_Delimiters_Quotes_And_Line_Breaks()
    {
        var table = CsvTable.Read(Bytes("Code,Title\r\nTC-1,\"Say \"\"hi\"\", then\r\nleave\"\r\nTC-2,plain\r\n"), 100);

        table.Header.ShouldBe(new[] { "Code", "Title" });
        table.Rows.Count.ShouldBe(2);
        table.Rows[0][1].ShouldBe("Say \"hi\", then\r\nleave");
        table.Rows[1][0].ShouldBe("TC-2");
    }

    [Theory]
    [InlineData("Code;Title\nTC-1;a,b\n", "a,b")]
    [InlineData("Code\tTitle\nTC-1\ta,b\n", "a,b")]
    [InlineData("Code,Title\nTC-1,a;b\n", "a;b")]
    public void Csv_Detects_The_Delimiter_Of_The_Header(string csv, string expectedTitle)
    {
        var table = CsvTable.Read(Bytes(csv), 100);

        table.Header.ShouldBe(new[] { "Code", "Title" });
        table.Rows.Single()[1].ShouldBe(expectedTitle);
    }

    [Fact]
    public void Csv_Skips_The_Byte_Order_Mark_And_Keeps_Blank_Rows_So_That_Row_Numbers_Stay_True()
    {
        var table = CsvTable.Read(Bytes("Code,Title\r\nTC-1,a\r\n\r\nTC-2,b\r\n\r\n\r\n", bom: true), 100);

        table.Header[0].ShouldBe("Code");
        table.Rows.Count.ShouldBe(3);
        table.IsBlank(1).ShouldBeTrue();
        table.RowNumber(2).ShouldBe(4);
        table.Cell(2, "title").ShouldBe("b");
    }

    [Fact]
    public void Csv_Reads_Utf16_With_A_Byte_Order_Mark()
    {
        var content = Encoding.Unicode.GetPreamble().Concat(Encoding.Unicode.GetBytes("Code,Title\r\nTC-1,Đăng nhập\r\n")).ToArray();

        var table = CsvTable.Read(content, 100);

        table.Rows.Single()[1].ShouldBe("Đăng nhập");
    }

    [Fact]
    public void Csv_Writes_A_Byte_Order_Mark_And_Round_Trips_Awkward_Text()
    {
        var awkward = new[] { "a,b", "say \"hi\"", "two\r\nlines", " padded ", "Đăng nhập – thành công", string.Empty };
        var written = CsvTable.Write(TableOf(new[] { "A", "B", "C", "D", "E", "F" }, awkward));

        written.Take(3).ShouldBe(Encoding.UTF8.GetPreamble());

        var read = CsvTable.Read(written, 100);
        read.Rows.Single().ShouldBe(awkward);
    }

    [Theory]
    [InlineData("=1+1")]
    [InlineData("+cmd|' /C calc'!A0")]
    [InlineData("-2+3")]
    [InlineData("@SUM(A1)")]
    public void Csv_Guards_Cells_That_A_Spreadsheet_Would_Run_As_A_Formula_And_Reads_Them_Back_Unchanged(string value)
    {
        var written = Encoding.UTF8.GetString(CsvTable.Write(TableOf(new[] { "A" }, new[] { value })));

        // The line that holds the value starts with a quote, so Excel shows text instead of running it.
        written.Split("\r\n")[1].ShouldStartWith("'");

        CsvTable.Read(Encoding.UTF8.GetBytes(written), 100).Rows.Single()[0].ShouldBe(value);
    }

    [Fact]
    public void Csv_Does_Not_Touch_A_Leading_Quote_That_Is_Not_A_Guard()
    {
        CsvTable.RemoveGuard("'hello").ShouldBe("'hello");
        CsvTable.RemoveGuard("'").ShouldBe("'");
        CsvTable.Guard("plain").ShouldBe("plain");
    }

    [Fact]
    public void Csv_Refuses_Too_Many_Rows_And_An_Empty_File()
    {
        var many = "Code\r\n" + string.Join("\r\n", Enumerable.Range(1, 11));
        Should.Throw<TableException>(() => CsvTable.Read(Bytes(many), 10)).Problem.ShouldBe(TableProblem.TooManyRows);
        CsvTable.Read(Bytes(many), 11).Rows.Count.ShouldBe(11);

        Should.Throw<TableException>(() => CsvTable.Read(Bytes("\r\n\r\n"), 10)).Problem.ShouldBe(TableProblem.Empty);
        Should.Throw<TableException>(() => CsvTable.Read(Array.Empty<byte>(), 10)).Problem.ShouldBe(TableProblem.Empty);
    }

    // ---- XLSX

    [Fact]
    public void Xlsx_Round_Trips_Text_Including_Diacritics_Line_Breaks_And_Empty_Cells()
    {
        var row = new[] { "TC-1", "Đăng nhập thành công", "line one\nline two", string.Empty, "  padded  ", "=1+1", new string('x', 4000) };
        var written = XlsxTable.Write(TableOf(new[] { "A", "B", "C", "D", "E", "F", "G" }, row), "Test cases");

        XlsxTable.LooksLikeXlsx(written).ShouldBeTrue();

        var read = XlsxTable.Read(written, 100, Limits.MaxUncompressedBytes);
        read.Header.ShouldBe(new[] { "A", "B", "C", "D", "E", "F", "G" });
        read.Rows.Single().Select((cell, i) => Text(read.Rows.Single(), i)).ShouldBe(row);
    }

    [Fact]
    public void Xlsx_Leaves_Out_Characters_That_Xml_Forbids_Instead_Of_Writing_A_Broken_File()
    {
        var written = XlsxTable.Write(TableOf(new[] { "A" }, new[] { "ab\u0001\u0008c" }), "Test cases");

        XlsxTable.Read(written, 100, Limits.MaxUncompressedBytes).Rows.Single()[0].ShouldBe("abc");
    }

    [Fact]
    public void Xlsx_Reads_A_Workbook_As_Excel_Saves_It_With_Shared_Strings_Numbers_Gaps_And_Leading_Blank_Rows()
    {
        var content = BuildWorkbookWithSharedStrings();

        var table = XlsxTable.Read(content, 100, Limits.MaxUncompressedBytes);

        // The header is the first row that has anything in it: row 3 here.
        table.HeaderRowNumber.ShouldBe(3);
        table.Header.ShouldBe(new[] { "Code", "Title", "DurationSeconds" });

        table.Rows.Count.ShouldBe(3); // rows 4, 5 (missing) and 6
        table.RowNumber(2).ShouldBe(6);
        table.Cell(0, "Code").ShouldBe("TC-1");
        table.Cell(0, "Title").ShouldBe("Đăng nhập");
        table.Cell(0, "DurationSeconds").ShouldBe("30");
        table.IsBlank(1).ShouldBeTrue();
        // A cell that is missing in the middle of a row is empty, and the columns after it keep their place.
        table.Cell(2, "Code").ShouldBe("TC-2");
        table.Cell(2, "Title").ShouldBe(string.Empty);
        table.Cell(2, "DurationSeconds").ShouldBe("12");
    }

    [Theory]
    [InlineData(0, "A")]
    [InlineData(25, "Z")]
    [InlineData(26, "AA")]
    [InlineData(701, "ZZ")]
    [InlineData(702, "AAA")]
    public void Xlsx_Column_Names_And_Indexes_Are_Inverse(int index, string name)
    {
        XlsxTable.ColumnName(index).ShouldBe(name);
        XlsxTable.ColumnIndex(name + "17").ShouldBe(index);
    }

    [Fact]
    public void Xlsx_Refuses_A_Small_File_That_Unzips_To_Something_Huge()
    {
        using var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            using var entry = archive.CreateEntry("xl/worksheets/sheet1.xml").Open();
            entry.Write(new byte[8 * 1024 * 1024]);
        }

        var bomb = stream.ToArray();
        bomb.Length.ShouldBeLessThan(100 * 1024);

        Should.Throw<TableException>(() => XlsxTable.Read(bomb, 100, 1024 * 1024)).Problem.ShouldBe(TableProblem.UnzippedTooLarge);
    }

    [Fact]
    public void Xlsx_Refuses_A_Zip_That_Is_Not_A_Workbook_And_A_Damaged_File()
    {
        using var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            using var writer = new StreamWriter(archive.CreateEntry("readme.txt").Open());
            writer.Write("not a workbook");
        }

        Should.Throw<TableException>(() => XlsxTable.Read(stream.ToArray(), 100, Limits.MaxUncompressedBytes))
            .Problem.ShouldBe(TableProblem.Unreadable);

        var damaged = XlsxTable.Write(TableOf(new[] { "A" }, new[] { "x" }), "S");
        Array.Fill(damaged, (byte)0x42, damaged.Length / 2, damaged.Length / 4);
        Should.Throw<TableException>(() => XlsxTable.Read(damaged, 100, Limits.MaxUncompressedBytes))
            .Problem.ShouldBe(TableProblem.Unreadable);
    }

    [Fact]
    public void Xlsx_Refuses_Too_Many_Rows()
    {
        var rows = Enumerable.Range(1, 11).Select(i => new[] { i.ToString() }).ToArray();
        var written = XlsxTable.Write(TableOf(new[] { "A" }, rows), "S");

        Should.Throw<TableException>(() => XlsxTable.Read(written, 10, Limits.MaxUncompressedBytes)).Problem.ShouldBe(TableProblem.TooManyRows);
        XlsxTable.Read(written, 11, Limits.MaxUncompressedBytes).Rows.Count.ShouldBe(11);
    }

    // ---- TableFile

    [Fact]
    public void The_Format_Is_Taken_From_The_Content_Not_From_A_Name_Or_A_Content_Type()
    {
        var table = TableOf(new[] { "Code", "Title" }, new[] { "TC-1", "a" });

        foreach (var format in new[] { TransferFormat.Csv, TransferFormat.Xlsx })
        {
            var read = TableFile.Read(TableFile.Write(table, format, "S"), Limits);
            read.Cell(0, "Title").ShouldBe("a");
        }
    }

    [Fact]
    public void An_Old_Excel_File_Or_Any_Binary_File_Is_Not_Read_As_Csv()
    {
        var oldXls = new byte[] { 0xD0, 0xCF, 0x11, 0xE0, 0xA1, 0xB1, 0x1A, 0xE1, 0, 0, 0, 0, 0, 0, 0, 0 };

        Should.Throw<TableException>(() => TableFile.Read(oldXls, Limits)).Problem.ShouldBe(TableProblem.UnknownFormat);
    }

    [Fact]
    public void A_File_Over_The_Size_Limit_Is_Refused()
    {
        var limits = new TestCaseManagementTransferOptions { MaxFileSizeBytes = 100 };

        Should.Throw<TableException>(() => TableFile.Read(Bytes("Code\r\n" + new string('x', 200)), limits))
            .Problem.ShouldBe(TableProblem.FileTooLarge);
    }

    /// <summary>A workbook written the way Excel does: strings in a shared table, numbers as such, empty cells left out.</summary>
    private static byte[] BuildWorkbookWithSharedStrings()
    {
        using var stream = new MemoryStream();
        using (var document = SpreadsheetDocument.Create(stream, SpreadsheetDocumentType.Workbook))
        {
            var workbook = document.AddWorkbookPart();
            workbook.Workbook = new Workbook();

            var shared = workbook.AddNewPart<SharedStringTablePart>();
            var strings = new[] { "Code", "Title", "DurationSeconds", "TC-1", "Đăng nhập", "TC-2" };
            shared.SharedStringTable = new SharedStringTable(strings.Select(s => new SharedStringItem(new Text(s))));

            Cell Str(string reference, int index) => new() { CellReference = reference, DataType = CellValues.SharedString, CellValue = new CellValue(index.ToString()) };
            Cell Num(string reference, string value) => new() { CellReference = reference, CellValue = new CellValue(value) };

            var sheetData = new SheetData(
                // Rows 1 and 2 are empty; row 5 is missing altogether.
                new Row(Str("A3", 0), Str("B3", 1), Str("C3", 2)) { RowIndex = 3U },
                new Row(Str("A4", 3), Str("B4", 4), Num("C4", "30")) { RowIndex = 4U },
                new Row(Str("A6", 5), Num("C6", "12")) { RowIndex = 6U });

            var worksheet = workbook.AddNewPart<WorksheetPart>();
            worksheet.Worksheet = new Worksheet(sheetData);
            workbook.Workbook.AppendChild(new Sheets(new Sheet { Id = workbook.GetIdOfPart(worksheet), SheetId = 1U, Name = "Sheet1" }));
            workbook.Workbook.Save();
        }

        return stream.ToArray();
    }
}
