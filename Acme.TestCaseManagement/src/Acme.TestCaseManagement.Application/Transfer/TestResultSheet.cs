using System.Globalization;
using Acme.TestCaseManagement.Enums;
using Acme.TestCaseManagement.Transfer.Tabular;

namespace Acme.TestCaseManagement.Transfer;

/// <summary>
/// The file layout of the execution results of a run: one row per attempt. An import reads Code, Result and, when
/// present, Version, ActualResult, DurationSeconds and Defects; the other columns of an export are for information.
/// </summary>
internal static class TestResultSheet
{
    public const string Code = "Code";
    public const string Title = "Title";
    public const string Version = "Version";
    public const string Attempt = "Attempt";
    public const string Result = "Result";
    public const string ActualResult = "ActualResult";
    public const string DurationSeconds = "DurationSeconds";
    public const string Defects = "Defects";
    public const string ExecutedAt = "ExecutedAt";
    public const string ExecutedBy = "ExecutedBy";

    public static readonly IReadOnlyList<string> ExportColumns = new[]
    {
        Code, Title, Version, Attempt, Result, ActualResult, DurationSeconds, Defects, ExecutedAt, ExecutedBy,
    };

    /// <summary>What a Defects cell holds: "Jira:BUG-88; GitHub:#42".</summary>
    public static string FormatDefects(IEnumerable<(string System, string Key)> defects)
    {
        return string.Join("; ", defects.Select(d => $"{d.System}:{d.Key}"));
    }

    public static Table Export(IReadOnlyList<ResultRow> rows)
    {
        var cells = rows.Select(row => (IReadOnlyList<string>)new[]
        {
            row.Code, row.Title, row.VersionNumber.ToString(CultureInfo.InvariantCulture),
            row.Attempt?.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
            row.Status.ToString(), row.ActualResult ?? string.Empty,
            row.Attempt.HasValue ? row.DurationSeconds.ToString(CultureInfo.InvariantCulture) : string.Empty,
            row.Defects, row.ExecutedAt?.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture) ?? string.Empty,
            row.ExecutedBy ?? string.Empty,
        }).ToList();

        return new Table(ExportColumns, cells);
    }

    public static ParsedResults Parse(Table table, TransferMessages messages)
    {
        var parsed = new ParsedResults();

        var missing = new[] { Code, Result }.Where(column => !table.HasColumn(column)).ToList();
        if (missing.Count > 0)
        {
            parsed.FileErrors.Add(messages.Get("Import:MissingColumns", ("Columns", string.Join(", ", missing))));
            return parsed;
        }

        var known = ExportColumns.ToHashSet(StringComparer.OrdinalIgnoreCase);
        parsed.IgnoredColumns.AddRange(
            table.Header.Where(h => h.Length > 0 && !known.Contains(h)).Distinct(StringComparer.OrdinalIgnoreCase));

        for (var i = 0; i < table.Rows.Count; i++)
        {
            if (table.IsBlank(i))
            {
                continue;
            }

            var row = new ResultEntry(table.RowNumber(i), TestCaseSheet.Normalize(table.Cell(i, Code)));
            parsed.Entries.Add(row);
            ReadRow(table, i, row, messages);
        }

        return parsed;
    }

    private static void ReadRow(Table table, int i, ResultEntry row, TransferMessages messages)
    {
        if (row.Code.Length == 0)
        {
            row.Errors.Add(messages.Get("Import:ValueRequired", ("Column", Code)));
        }

        var result = TestCaseSheet.Normalize(table.Cell(i, Result));
        if (result.Length == 0 || result.Equals(nameof(TestResultStatus.Untested), StringComparison.OrdinalIgnoreCase))
        {
            // An export lists items that were never executed as Untested; there is nothing to record for them.
            row.Skip = true;
        }
        else if (TestCaseSheet.TryParseEnum<TestResultStatus>(result, out var status) && status != TestResultStatus.Untested)
        {
            row.Status = status;
        }
        else
        {
            row.Errors.Add(messages.Get(
                "Import:InvalidValue",
                ("Column", Result),
                ("Value", result),
                ("Allowed", string.Join(", ", Enum.GetNames<TestResultStatus>().Where(n => n != nameof(TestResultStatus.Untested))))));
        }

        var version = TestCaseSheet.Normalize(table.Cell(i, Version));
        if (version.Length > 0)
        {
            if (int.TryParse(version, NumberStyles.None, CultureInfo.InvariantCulture, out var number) && number >= 1)
            {
                row.Version = number;
            }
            else
            {
                row.Errors.Add(messages.Get("Import:InvalidNumber", ("Column", Version), ("Value", version)));
            }
        }

        var duration = TestCaseSheet.Normalize(table.Cell(i, DurationSeconds));
        if (duration.Length > 0)
        {
            if (int.TryParse(duration, NumberStyles.None, CultureInfo.InvariantCulture, out var seconds))
            {
                row.DurationSeconds = seconds;
            }
            else
            {
                row.Errors.Add(messages.Get("Import:InvalidNumber", ("Column", DurationSeconds), ("Value", duration)));
            }
        }

        var actual = TestCaseSheet.Normalize(table.Cell(i, ActualResult));
        if (actual.Length > TestExecutionConsts.MaxActualResultLength)
        {
            row.Errors.Add(messages.Get("Import:TooLong", ("Column", ActualResult), ("Max", TestExecutionConsts.MaxActualResultLength)));
        }

        row.ActualResult = actual.Length == 0 ? null : actual;

        ReadDefects(TestCaseSheet.Normalize(table.Cell(i, Defects)), row, messages);

        if (row.Defects.Count > 0 && !row.Skip && row.Status != TestResultStatus.Failed && row.Errors.Count == 0)
        {
            row.Errors.Add(messages.Get("Import:Results:DefectNeedsFailed"));
        }
    }

    private static void ReadDefects(string cell, ResultEntry row, TransferMessages messages)
    {
        if (cell.Length == 0)
        {
            return;
        }

        foreach (var part in cell.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var colon = part.IndexOf(':');
            var system = colon > 0 ? part[..colon].Trim() : string.Empty;
            var key = colon > 0 ? part[(colon + 1)..].Trim() : string.Empty;

            if (system.Length == 0 || key.Length == 0 || system.Length > DefectLinkConsts.MaxExternalSystemLength || key.Length > DefectLinkConsts.MaxIssueKeyLength)
            {
                row.Errors.Add(messages.Get("Import:Results:DefectFormat", ("Value", part)));
                continue;
            }

            row.Defects.Add((system, key));
        }
    }
}

/// <summary>One row of an export: an attempt, or an item that has none.</summary>
internal sealed record ResultRow(
    string Code,
    string Title,
    int VersionNumber,
    int? Attempt,
    TestResultStatus Status,
    string? ActualResult,
    int DurationSeconds,
    string Defects,
    DateTime? ExecutedAt,
    string? ExecutedBy);

internal sealed class ParsedResults
{
    public List<ResultEntry> Entries { get; } = new();

    public List<string> FileErrors { get; } = new();

    public List<string> IgnoredColumns { get; } = new();
}

/// <summary>One row of an import file, before it is matched to an item of the run.</summary>
internal sealed class ResultEntry
{
    public ResultEntry(int row, string code)
    {
        Row = row;
        Code = code;
    }

    public int Row { get; }

    public string Code { get; }

    /// <summary>No result to record: blank or Untested.</summary>
    public bool Skip { get; set; }

    public TestResultStatus Status { get; set; }

    public int? Version { get; set; }

    public string? ActualResult { get; set; }

    public int DurationSeconds { get; set; }

    public List<(string System, string Key)> Defects { get; } = new();

    public List<string> Errors { get; } = new();
}
