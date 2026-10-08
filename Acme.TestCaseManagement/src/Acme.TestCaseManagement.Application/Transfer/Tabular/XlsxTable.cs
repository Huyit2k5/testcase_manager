using System.IO.Compression;
using System.Text;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;

namespace Acme.TestCaseManagement.Transfer.Tabular;

/// <summary>
/// Excel workbooks (.xlsx) through the Open XML SDK. A table is the first worksheet: its first non-blank row is the
/// header. Cells are written as plain text, which Excel never evaluates, so unlike CSV no formula guard is needed.
/// </summary>
internal static class XlsxTable
{
    private const int HeaderStyle = 1;
    private const int BodyStyle = 0;

    /// <summary>The last row (1,048,576) and column (XFD = 16,384) an Excel sheet can have; a cell outside them is not a real sheet.</summary>
    private const int MaxSheetRow = 1_048_576;
    internal const int MaxSheetColumns = 16_384;

    public static bool LooksLikeXlsx(byte[] content)
    {
        return content.Length > 4 && content[0] == 'P' && content[1] == 'K' && content[2] == 3 && content[3] == 4;
    }

    public static Table Read(byte[] content, int maxRows, long maxUncompressedBytes)
    {
        EnsureNotAZipBomb(content, maxUncompressedBytes);

        try
        {
            using var stream = new MemoryStream(content, writable: false);
            using var document = SpreadsheetDocument.Open(stream, isEditable: false);

            var workbook = document.WorkbookPart ?? throw new TableException(TableProblem.Unreadable);
            var sheet = workbook.Workbook?.Sheets?.Elements<Sheet>().FirstOrDefault() ?? throw new TableException(TableProblem.Empty);
            var worksheet = (WorksheetPart)workbook.GetPartById(sheet.Id!.Value!);
            var sharedStrings = workbook.SharedStringTablePart?.SharedStringTable?.Elements<SharedStringItem>().ToList()
                                ?? new List<SharedStringItem>();

            return ReadRows(worksheet, sharedStrings, maxRows);
        }
        catch (Exception exception) when (exception is not TableException)
        {
            // Corrupt zip, missing parts, malformed XML.
            throw new TableException(TableProblem.Unreadable);
        }
    }

    public static byte[] Write(Table table, string sheetName)
    {
        using var stream = new MemoryStream();

        using (var document = SpreadsheetDocument.Create(stream, SpreadsheetDocumentType.Workbook))
        {
            var workbook = document.AddWorkbookPart();
            workbook.Workbook = new Workbook();

            var styles = workbook.AddNewPart<WorkbookStylesPart>();
            styles.Stylesheet = CreateStylesheet();

            var worksheet = workbook.AddNewPart<WorksheetPart>();
            var sheetData = new SheetData();

            var allRows = new List<IReadOnlyList<string>> { table.Header };
            allRows.AddRange(table.Rows);

            for (var r = 0; r < allRows.Count; r++)
            {
                var row = new Row { RowIndex = (uint)(r + 1) };
                for (var c = 0; c < allRows[r].Count; c++)
                {
                    var value = Sanitize(allRows[r][c]);
                    if (value.Length == 0 && r > 0)
                    {
                        continue;
                    }

                    row.Append(new Cell
                    {
                        CellReference = ColumnName(c) + (r + 1),
                        DataType = CellValues.InlineString,
                        StyleIndex = (uint)(r == 0 ? HeaderStyle : BodyStyle),
                        InlineString = new InlineString(new Text(value) { Space = SpaceProcessingModeValues.Preserve }),
                    });
                }

                sheetData.Append(row);
            }

            worksheet.Worksheet = new Worksheet(
                new SheetViews(new SheetView(
                    new Pane { VerticalSplit = 1D, TopLeftCell = "A2", ActivePane = PaneValues.BottomLeft, State = PaneStateValues.Frozen },
                    new Selection { Pane = PaneValues.BottomLeft })
                { WorkbookViewId = 0U }),
                CreateColumns(allRows),
                sheetData);

            workbook.Workbook.AppendChild(new Sheets(new Sheet
            {
                Id = workbook.GetIdOfPart(worksheet),
                SheetId = 1U,
                Name = sheetName,
            }));

            workbook.Workbook.Save();
        }

        return stream.ToArray();
    }

    private static Table ReadRows(WorksheetPart worksheet, List<SharedStringItem> sharedStrings, int maxRows)
    {
        var rows = new SortedDictionary<int, List<string>>();

        using (var reader = OpenXmlReader.Create(worksheet))
        {
            var count = 0;
            while (reader.Read())
            {
                if (reader.ElementType != typeof(Row) || !reader.IsStartElement)
                {
                    continue;
                }

                var row = (Row)reader.LoadCurrentElement()!;
                var cells = ReadCells(row, sharedStrings);
                if (cells.All(string.IsNullOrWhiteSpace))
                {
                    continue;
                }

                // The row number is data from the file: it decides how many blank rows are kept below, so it must be real.
                var declared = row.RowIndex?.Value ?? (uint)(count + 1);
                if (declared == 0 || declared > MaxSheetRow)
                {
                    throw new TableException(TableProblem.Unreadable);
                }

                var rowNumber = (int)declared;
                rows[rowNumber] = cells;
                count++;
                if (count > maxRows + 1)
                {
                    throw new TableException(TableProblem.TooManyRows);
                }
            }
        }

        if (rows.Count == 0)
        {
            throw new TableException(TableProblem.Empty);
        }

        var headerRowNumber = rows.Keys.First();
        var header = rows[headerRowNumber].Select(cell => cell.Trim()).ToList();

        var data = new List<IReadOnlyList<string>>();
        var last = rows.Keys.Last();

        // Blank rows between the filled ones are kept (so that row numbers stay true), which makes the span, not the number of
        // filled rows, what costs memory: a file with a header and one cell at row 1,000,000 must not become a million rows.
        if (last - headerRowNumber > maxRows)
        {
            throw new TableException(TableProblem.TooManyRows);
        }

        for (var number = headerRowNumber + 1; number <= last; number++)
        {
            // A row that is missing from the sheet is a blank row; keeping it keeps the row numbers true.
            data.Add(rows.TryGetValue(number, out var cells) ? cells : new List<string>());
        }

        return new Table(header, data, headerRowNumber);
    }

    private static List<string> ReadCells(Row row, List<SharedStringItem> sharedStrings)
    {
        var cells = new List<string>();

        foreach (var cell in row.Elements<Cell>())
        {
            var index = cell.CellReference?.Value is { } reference ? ColumnIndex(reference) : cells.Count;
            if (index < 0 || index >= MaxSheetColumns)
            {
                throw new TableException(TableProblem.Unreadable);
            }

            while (cells.Count < index)
            {
                cells.Add(string.Empty);
            }

            var value = ReadCell(cell, sharedStrings);
            if (index < cells.Count)
            {
                cells[index] = value;
            }
            else
            {
                cells.Add(value);
            }
        }

        return cells;
    }

    private static string ReadCell(Cell cell, List<SharedStringItem> sharedStrings)
    {
        var raw = cell.CellValue?.Text ?? string.Empty;

        if (cell.DataType?.Value == CellValues.SharedString)
        {
            return int.TryParse(raw, out var index) && index >= 0 && index < sharedStrings.Count ? sharedStrings[index].InnerText : string.Empty;
        }

        if (cell.DataType?.Value == CellValues.InlineString)
        {
            return cell.InlineString?.InnerText ?? string.Empty;
        }

        if (cell.DataType?.Value == CellValues.Boolean)
        {
            return raw == "1" ? "TRUE" : "FALSE";
        }

        if (cell.DataType?.Value == CellValues.Error)
        {
            return string.Empty;
        }

        // Numbers and cached formula results: the stored text is what Excel shows for a general number.
        return raw;
    }

    private static void EnsureNotAZipBomb(byte[] content, long maxUncompressedBytes)
    {
        try
        {
            using var archive = new ZipArchive(new MemoryStream(content, writable: false), ZipArchiveMode.Read);
            long total = 0;
            foreach (var entry in archive.Entries)
            {
                total += entry.Length;
                if (total > maxUncompressedBytes)
                {
                    throw new TableException(TableProblem.UnzippedTooLarge);
                }
            }

            // The sizes above are what the file says about itself. Count what really comes out, and stop at the limit.
            long actual = 0;
            var buffer = new byte[81920];
            foreach (var entry in archive.Entries)
            {
                using var entryStream = entry.Open();
                int read;
                while ((read = entryStream.Read(buffer, 0, buffer.Length)) > 0)
                {
                    actual += read;
                    if (actual > maxUncompressedBytes)
                    {
                        throw new TableException(TableProblem.UnzippedTooLarge);
                    }
                }
            }
        }
        catch (InvalidDataException)
        {
            throw new TableException(TableProblem.Unreadable);
        }
    }

    private static Stylesheet CreateStylesheet()
    {
        return new Stylesheet(
            new Fonts(
                new Font(new FontSize { Val = 11D }, new FontName { Val = "Calibri" }),
                new Font(new Bold(), new FontSize { Val = 11D }, new FontName { Val = "Calibri" })),
            new Fills(
                new Fill(new PatternFill { PatternType = PatternValues.None }),
                new Fill(new PatternFill { PatternType = PatternValues.Gray125 }),
                new Fill(new PatternFill(new ForegroundColor { Rgb = "FFE2E8F0" }) { PatternType = PatternValues.Solid })),
            new Borders(new Border()),
            new CellStyleFormats(new CellFormat()),
            new CellFormats(
                new CellFormat(new Alignment { WrapText = true, Vertical = VerticalAlignmentValues.Top })
                { FontId = 0U, FillId = 0U, BorderId = 0U, ApplyAlignment = true },
                new CellFormat(new Alignment { WrapText = true, Vertical = VerticalAlignmentValues.Center })
                { FontId = 1U, FillId = 2U, BorderId = 0U, ApplyFont = true, ApplyFill = true, ApplyAlignment = true }));
    }

    private static Columns CreateColumns(List<IReadOnlyList<string>> rows)
    {
        var columns = new Columns();
        var count = rows[0].Count;

        for (var c = 0; c < count; c++)
        {
            var longest = rows.Take(200).Max(row => c < row.Count ? LongestLine(row[c]) : 0);
            columns.Append(new Column
            {
                Min = (uint)(c + 1),
                Max = (uint)(c + 1),
                Width = Math.Clamp(longest + 2, 10, 60),
                CustomWidth = true,
            });
        }

        return columns;
    }

    private static int LongestLine(string text)
    {
        return text.Split('\n').Max(line => line.Length);
    }

    /// <summary>Removes the characters that XML 1.0 forbids; a file that contains one cannot be opened by Excel.</summary>
    private static string Sanitize(string value)
    {
        if (!value.Any(IsInvalidXmlChar))
        {
            return value;
        }

        var builder = new StringBuilder(value.Length);
        foreach (var c in value.Where(c => !IsInvalidXmlChar(c)))
        {
            builder.Append(c);
        }

        return builder.ToString();
    }

    private static bool IsInvalidXmlChar(char c)
    {
        return c < 0x20 && c != '\t' && c != '\n' && c != '\r' || c is '￾' or '￿';
    }

    /// <summary>0 becomes A, 25 Z, 26 AA.</summary>
    internal static string ColumnName(int index)
    {
        var name = new StringBuilder();
        for (var n = index + 1; n > 0; n = (n - 1) / 26)
        {
            name.Insert(0, (char)('A' + (n - 1) % 26));
        }

        return name.ToString();
    }

    /// <summary>The zero-based column of a cell reference such as "C7".</summary>
    internal static int ColumnIndex(string reference)
    {
        var index = 0;
        foreach (var c in reference.TakeWhile(char.IsLetter))
        {
            // Capped: a reference of many letters must not overflow into a small, valid-looking index.
            index = Math.Min(index * 26 + (char.ToUpperInvariant(c) - 'A' + 1), MaxSheetColumns + 1);
        }

        return index - 1;
    }
}
