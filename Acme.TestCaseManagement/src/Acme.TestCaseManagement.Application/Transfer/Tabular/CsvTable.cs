using System.Text;

namespace Acme.TestCaseManagement.Transfer.Tabular;

/// <summary>
/// CSV as RFC 4180 describes it: fields in double quotes when they hold the delimiter, a quote or a line break, a quote
/// inside a field doubled, and records that may span lines. Reading detects the delimiter (comma, semicolon or tab, as
/// Excel writes it depending on the regional settings) and the encoding from the byte order mark.
/// </summary>
internal static class CsvTable
{
    private static readonly char[] Delimiters = { ',', ';', '\t' };

    /// <summary>Characters that make a spreadsheet read a cell as a formula (OWASP, CSV injection).</summary>
    private static readonly char[] FormulaStarts = { '=', '+', '-', '@', '\t', '\r' };

    private const char GuardPrefix = '\'';

    public static Table Read(byte[] content, int maxRows)
    {
        using var reader = new StreamReader(new MemoryStream(content), new UTF8Encoding(false), detectEncodingFromByteOrderMarks: true);
        var text = reader.ReadToEnd();

        var delimiter = DetectDelimiter(text);
        var records = Parse(text, delimiter, maxRows + 1);

        // Trailing blank records are not data.
        while (records.Count > 0 && records[^1].All(string.IsNullOrWhiteSpace))
        {
            records.RemoveAt(records.Count - 1);
        }

        if (records.Count == 0)
        {
            throw new TableException(TableProblem.Empty);
        }

        if (records.Count - 1 > maxRows)
        {
            throw new TableException(TableProblem.TooManyRows);
        }

        var header = records[0].Select(cell => cell.Trim()).ToList();
        var rows = records.Skip(1).Select(record => (IReadOnlyList<string>)record.Select(RemoveGuard).ToList()).ToList();

        return new Table(header, rows);
    }

    public static byte[] Write(Table table)
    {
        var builder = new StringBuilder();
        AppendRecord(builder, table.Header, guard: false);
        foreach (var row in table.Rows)
        {
            AppendRecord(builder, row, guard: true);
        }

        // The byte order mark is what lets Excel open the file as UTF-8 instead of the local code page.
        var preamble = Encoding.UTF8.GetPreamble();
        var body = Encoding.UTF8.GetBytes(builder.ToString());
        var result = new byte[preamble.Length + body.Length];
        preamble.CopyTo(result, 0);
        body.CopyTo(result, preamble.Length);
        return result;
    }

    /// <summary>The text that goes into a cell of an export: protected against being read as a formula.</summary>
    internal static string Guard(string value)
    {
        return value.Length > 0 && Array.IndexOf(FormulaStarts, value[0]) >= 0 ? GuardPrefix + value : value;
    }

    /// <summary>Takes back <see cref="Guard"/>: a leading quote in front of a formula character is not part of the text.</summary>
    internal static string RemoveGuard(string value)
    {
        return value.Length > 1 && value[0] == GuardPrefix && Array.IndexOf(FormulaStarts, value[1]) >= 0 ? value[1..] : value;
    }

    private static void AppendRecord(StringBuilder builder, IReadOnlyList<string> cells, bool guard)
    {
        for (var i = 0; i < cells.Count; i++)
        {
            if (i > 0)
            {
                builder.Append(',');
            }

            var value = guard ? Guard(cells[i]) : cells[i];
            if (value.AsSpan().IndexOfAny(",\"\r\n") >= 0 || value.StartsWith(' ') || value.EndsWith(' '))
            {
                builder.Append('"').Append(value.Replace("\"", "\"\"")).Append('"');
            }
            else
            {
                builder.Append(value);
            }
        }

        builder.Append("\r\n");
    }

    /// <summary>The delimiter that occurs most (outside quotes) in the first record; a comma when there is none.</summary>
    private static char DetectDelimiter(string text)
    {
        var counts = new int[Delimiters.Length];
        var inQuotes = false;

        foreach (var c in text)
        {
            if (c == '"')
            {
                inQuotes = !inQuotes;
            }
            else if (!inQuotes && (c == '\n' || c == '\r'))
            {
                break;
            }
            else if (!inQuotes)
            {
                var index = Array.IndexOf(Delimiters, c);
                if (index >= 0)
                {
                    counts[index]++;
                }
            }
        }

        var best = Array.IndexOf(counts, counts.Max());
        return counts[best] == 0 ? ',' : Delimiters[best];
    }

    private static List<List<string>> Parse(string text, char delimiter, int maxRecords)
    {
        var records = new List<List<string>>();
        var record = new List<string>();
        var field = new StringBuilder();
        var inQuotes = false;
        var fieldWasQuoted = false;

        void EndField()
        {
            record.Add(field.ToString());
            field.Clear();
            fieldWasQuoted = false;
        }

        void EndRecord()
        {
            EndField();
            records.Add(record);
            record = new List<string>();
            if (records.Count > maxRecords)
            {
                throw new TableException(TableProblem.TooManyRows);
            }
        }

        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];

            if (inQuotes)
            {
                if (c == '"')
                {
                    if (i + 1 < text.Length && text[i + 1] == '"')
                    {
                        field.Append('"');
                        i++;
                    }
                    else
                    {
                        inQuotes = false;
                    }
                }
                else
                {
                    field.Append(c);
                }

                continue;
            }

            if (c == '"' && field.Length == 0 && !fieldWasQuoted)
            {
                inQuotes = true;
                fieldWasQuoted = true;
            }
            else if (c == delimiter)
            {
                EndField();
            }
            else if (c == '\r' || c == '\n')
            {
                if (c == '\r' && i + 1 < text.Length && text[i + 1] == '\n')
                {
                    i++;
                }

                EndRecord();
            }
            else
            {
                field.Append(c);
            }
        }

        // The last record has no line break after it.
        if (field.Length > 0 || fieldWasQuoted || record.Count > 0)
        {
            EndRecord();
        }

        return records;
    }
}
