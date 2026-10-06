namespace Acme.TestCaseManagement.Transfer.Tabular;

/// <summary>Why a file could not be read as a table. The application service turns it into a localized message.</summary>
internal enum TableProblem
{
    /// <summary>Neither an Excel (.xlsx) nor a text file (for example an old .xls file or an image).</summary>
    UnknownFormat,

    /// <summary>A file that claims to be Excel but cannot be opened.</summary>
    Unreadable,

    FileTooLarge,

    UnzippedTooLarge,

    TooManyRows,

    /// <summary>No header row.</summary>
    Empty,
}

internal sealed class TableException : Exception
{
    public TableException(TableProblem problem)
        : base(problem.ToString())
    {
        Problem = problem;
    }

    public TableProblem Problem { get; }
}

/// <summary>A header and data rows, as read from or written to a CSV or Excel file. Cells are never null.</summary>
internal sealed class Table
{
    public Table(IReadOnlyList<string> header, IReadOnlyList<IReadOnlyList<string>> rows, int headerRowNumber = 1)
    {
        Header = header;
        Rows = rows;
        HeaderRowNumber = headerRowNumber;
    }

    public IReadOnlyList<string> Header { get; }

    /// <summary>The data rows, in order, blank rows included so that row numbers stay true.</summary>
    public IReadOnlyList<IReadOnlyList<string>> Rows { get; }

    /// <summary>Spreadsheet row number of the header (1 unless the file starts with blank rows).</summary>
    public int HeaderRowNumber { get; }

    /// <summary>The spreadsheet row number of the data row at <paramref name="index"/>.</summary>
    public int RowNumber(int index) => HeaderRowNumber + 1 + index;

    /// <summary>The cell of a data row in the named column (case-insensitive), or an empty string.</summary>
    public string Cell(int rowIndex, string column)
    {
        var columnIndex = IndexOf(column);
        var row = Rows[rowIndex];
        return columnIndex >= 0 && columnIndex < row.Count ? row[columnIndex] : string.Empty;
    }

    public bool HasColumn(string column) => IndexOf(column) >= 0;

    public int IndexOf(string column)
    {
        for (var i = 0; i < Header.Count; i++)
        {
            if (string.Equals(Header[i].Trim(), column, StringComparison.OrdinalIgnoreCase))
            {
                return i;
            }
        }

        return -1;
    }

    public bool IsBlank(int rowIndex) => Rows[rowIndex].All(string.IsNullOrWhiteSpace);
}
