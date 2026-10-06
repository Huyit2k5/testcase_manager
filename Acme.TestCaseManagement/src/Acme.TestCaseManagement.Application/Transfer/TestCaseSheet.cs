using System.Globalization;
using Acme.TestCaseManagement.Enums;
using Acme.TestCaseManagement.TestCases;
using Acme.TestCaseManagement.Transfer.Tabular;

namespace Acme.TestCaseManagement.Transfer;

/// <summary>
/// The file layout of a test case library: one row per step, the test case columns repeated on every row of the case
/// (as Xray and TestRail export it). On import the rows of one case must follow each other; the test case columns of
/// the later rows may be left blank.
/// </summary>
internal static class TestCaseSheet
{
    public const string Suite = "Suite";
    public const string Code = "Code";
    public const string Title = "Title";
    public const string Description = "Description";
    public const string Preconditions = "Preconditions";
    public const string Postconditions = "Postconditions";
    public const string Priority = "Priority";
    public const string Severity = "Severity";
    public const string Kind = "Kind";
    public const string Layer = "Layer";
    public const string ExecutionType = "ExecutionType";
    public const string AutomationId = "AutomationId";
    public const string Flaky = "Flaky";
    public const string Status = "Status";
    public const string Version = "Version";
    public const string StepNo = "StepNo";
    public const string Action = "Action";
    public const string ExpectedResult = "ExpectedResult";
    public const string TestData = "TestData";

    /// <summary>The columns of an export, in order.</summary>
    public static readonly IReadOnlyList<string> ExportColumns = new[]
    {
        Suite, Code, Title, Description, Preconditions, Postconditions, Priority, Severity, Kind, Layer, ExecutionType,
        AutomationId, Flaky, Status, Version, StepNo, Action, ExpectedResult, TestData,
    };

    /// <summary>Columns that an export writes for information and an import reads past: status and version are set by the workflow.</summary>
    private static readonly string[] InformationalColumns = { Status, Version, StepNo };

    private static readonly string[] StepColumns = { Action, ExpectedResult, TestData };

    private static readonly string[] TestCaseColumns =
    {
        Suite, Title, Description, Preconditions, Postconditions, Priority, Severity, Kind, Layer, ExecutionType, AutomationId, Flaky,
    };

    public static bool HasStepColumns(Table table) => StepColumns.Any(table.HasColumn);

    public static Table Export(IReadOnlyList<TestCase> testCases, Func<Guid, string> suitePath)
    {
        var rows = new List<IReadOnlyList<string>>();

        foreach (var testCase in testCases)
        {
            var steps = testCase.Steps.OrderBy(s => s.StepOrder).ToList();
            var head = new[]
            {
                suitePath(testCase.SuiteId), testCase.Code, testCase.Title, testCase.Description ?? string.Empty,
                testCase.Preconditions ?? string.Empty, testCase.Postconditions ?? string.Empty, testCase.Priority.ToString(),
                testCase.Severity.ToString(), testCase.Kind.ToString(), testCase.Layer.ToString(), testCase.ExecutionType.ToString(),
                testCase.AutomationId ?? string.Empty, testCase.IsFlaky ? "true" : "false", testCase.Status.ToString(),
                testCase.CurrentVersion.ToString(CultureInfo.InvariantCulture),
            };

            // A test case without steps is still one row, so that it is not lost.
            if (steps.Count == 0)
            {
                rows.Add(head.Concat(new[] { string.Empty, string.Empty, string.Empty, string.Empty }).ToList());
                continue;
            }

            foreach (var step in steps)
            {
                rows.Add(head.Concat(new[]
                {
                    step.StepOrder.ToString(CultureInfo.InvariantCulture), step.Action, step.ExpectedResult, step.TestData ?? string.Empty,
                }).ToList());
            }
        }

        return new Table(ExportColumns, rows);
    }

    /// <summary>Reads the test cases of a table. Nothing is looked up or written here.</summary>
    public static ParsedSheet Parse(Table table, TransferMessages messages)
    {
        var result = new ParsedSheet();

        if (!table.HasColumn(Code))
        {
            result.FileErrors.Add(messages.Get("Import:MissingColumns", ("Columns", Code)));
            return result;
        }

        var known = ExportColumns.ToHashSet(StringComparer.OrdinalIgnoreCase);
        result.IgnoredColumns.AddRange(
            table.Header.Where(h => h.Length > 0 && !known.Contains(h)).Distinct(StringComparer.OrdinalIgnoreCase));
        result.Columns.UnionWith(table.Header.Where(known.Contains));

        TestCaseDraft? current = null;
        var seenCodes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        for (var i = 0; i < table.Rows.Count; i++)
        {
            if (table.IsBlank(i))
            {
                continue;
            }

            var row = table.RowNumber(i);
            var code = Text(table.Cell(i, Code));

            if (code.Length == 0 && current == null)
            {
                var orphan = new TestCaseDraft(row, string.Empty);
                orphan.Errors.Add(messages.Get("Import:CodeMissing"));
                result.Drafts.Add(orphan);
                continue;
            }

            var continues = code.Length == 0 || (current != null && string.Equals(current.Code, code, StringComparison.OrdinalIgnoreCase));
            if (continues)
            {
                CheckSameAsFirstRow(table, i, current!, messages);
            }
            else
            {
                current = new TestCaseDraft(row, code);
                result.Drafts.Add(current);

                if (!seenCodes.Add(code))
                {
                    current.Errors.Add(messages.Get("Import:CodeRepeated", ("Code", code)));
                }

                ReadTestCase(table, i, current, messages);
            }

            ReadStep(table, i, row, current!, messages);
        }

        return result;
    }

    private static void ReadTestCase(Table table, int i, TestCaseDraft draft, TransferMessages messages)
    {
        draft.SuitePath = Text(table.Cell(i, Suite));
        draft.Title = Text(table.Cell(i, Title));
        draft.Description = Optional(table.Cell(i, Description));
        draft.Preconditions = Optional(table.Cell(i, Preconditions));
        draft.Postconditions = Optional(table.Cell(i, Postconditions));
        draft.AutomationId = Optional(table.Cell(i, AutomationId));

        CheckLength(draft, Code, draft.Code, TestCaseConsts.MaxCodeLength, messages);
        CheckLength(draft, Title, draft.Title, TestCaseConsts.MaxTitleLength, messages);
        CheckLength(draft, Description, draft.Description, TestCaseConsts.MaxTextLength, messages);
        CheckLength(draft, Preconditions, draft.Preconditions, TestCaseConsts.MaxTextLength, messages);
        CheckLength(draft, Postconditions, draft.Postconditions, TestCaseConsts.MaxTextLength, messages);
        CheckLength(draft, AutomationId, draft.AutomationId, TestCaseConsts.MaxAutomationIdLength, messages);

        draft.Priority = ReadEnum(table, i, Priority, PriorityLevel.Medium, draft, messages);
        draft.Severity = ReadEnum(table, i, Severity, SeverityLevel.Medium, draft, messages);
        draft.Kind = ReadEnum(table, i, Kind, TestKind.Functional, draft, messages);
        draft.Layer = ReadEnum(table, i, Layer, TestLayer.Acceptance, draft, messages);
        draft.ExecutionType = ReadEnum(table, i, ExecutionType, Enums.ExecutionType.Manual, draft, messages);
        draft.IsFlaky = ReadBool(table, i, Flaky, draft, messages);
    }

    /// <summary>A continuation row may leave the test case columns blank, but may not contradict the first row.</summary>
    private static void CheckSameAsFirstRow(Table table, int i, TestCaseDraft draft, TransferMessages messages)
    {
        foreach (var column in TestCaseColumns.Where(table.HasColumn))
        {
            var value = Text(table.Cell(i, column));
            if (value.Length == 0)
            {
                continue;
            }

            var first = column switch
            {
                Suite => draft.SuitePath,
                Title => draft.Title,
                Description => draft.Description,
                Preconditions => draft.Preconditions,
                Postconditions => draft.Postconditions,
                AutomationId => draft.AutomationId,
                Priority => draft.Priority.ToString(),
                Severity => draft.Severity.ToString(),
                Kind => draft.Kind.ToString(),
                Layer => draft.Layer.ToString(),
                ExecutionType => draft.ExecutionType.ToString(),
                _ => draft.IsFlaky ? "true" : "false",
            } ?? string.Empty;

            var isText = column is Suite or Title or Description or Preconditions or Postconditions or AutomationId;
            var same = string.Equals(first, isText ? Normalize(value) : value, isText ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase)
                       || (!isText && SameEnumOrBool(column, first, value));

            if (!same)
            {
                draft.Errors.Add(messages.Get("Import:Conflict", ("Column", column), ("Code", draft.Code)));
            }
        }
    }

    private static bool SameEnumOrBool(string column, string first, string value)
    {
        // "high" and "High", "Yes" and "true", or the number of an enum member all name the same value.
        return column == Flaky
            ? TryParseBool(value, out var parsed) && parsed == (first == "true")
            : string.Equals(first, value, StringComparison.OrdinalIgnoreCase) || (int.TryParse(value, out var number) && IsNumberOf(column, first, number));
    }

    private static bool IsNumberOf(string column, string name, int number)
    {
        return column switch
        {
            Priority => Enum.TryParse<PriorityLevel>(name, out var v) && (int)v == number,
            Severity => Enum.TryParse<SeverityLevel>(name, out var v) && (int)v == number,
            Kind => Enum.TryParse<TestKind>(name, out var v) && (int)v == number,
            Layer => Enum.TryParse<TestLayer>(name, out var v) && (int)v == number,
            _ => Enum.TryParse<ExecutionType>(name, out var v) && (int)v == number,
        };
    }

    private static void ReadStep(Table table, int i, int row, TestCaseDraft draft, TransferMessages messages)
    {
        var action = Normalize(table.Cell(i, Action));
        var expected = Normalize(table.Cell(i, ExpectedResult));
        var data = Optional(table.Cell(i, TestData));

        if (action.Length == 0 && expected.Length == 0 && data == null)
        {
            return;
        }

        if (action.Length == 0 || expected.Length == 0)
        {
            draft.Errors.Add(messages.Get("Import:StepIncomplete", ("Row", row)));
            return;
        }

        CheckLength(draft, Action, action, TestStepConsts.MaxTextLength, messages);
        CheckLength(draft, ExpectedResult, expected, TestStepConsts.MaxTextLength, messages);
        CheckLength(draft, TestData, data, TestStepConsts.MaxTextLength, messages);
        draft.Steps.Add(new DraftStep(action, expected, data));
    }

    private static T ReadEnum<T>(Table table, int i, string column, T fallback, TestCaseDraft draft, TransferMessages messages)
        where T : struct, Enum
    {
        var raw = Text(table.Cell(i, column));
        if (raw.Length == 0)
        {
            return fallback;
        }

        if (TryParseEnum<T>(raw, out var value))
        {
            return value;
        }

        draft.Errors.Add(messages.Get(
            "Import:InvalidValue", ("Column", column), ("Value", raw), ("Allowed", string.Join(", ", Enum.GetNames<T>()))));
        return fallback;
    }

    private static bool ReadBool(Table table, int i, string column, TestCaseDraft draft, TransferMessages messages)
    {
        var raw = Text(table.Cell(i, column));
        if (raw.Length == 0)
        {
            return false;
        }

        if (TryParseBool(raw, out var value))
        {
            return value;
        }

        draft.Errors.Add(messages.Get("Import:InvalidValue", ("Column", column), ("Value", raw), ("Allowed", "true, false")));
        return false;
    }

    /// <summary>An enum member by name (any case) or by its number.</summary>
    internal static bool TryParseEnum<T>(string raw, out T value)
        where T : struct, Enum
    {
        if (int.TryParse(raw, NumberStyles.None, CultureInfo.InvariantCulture, out var number))
        {
            value = (T)(object)number;
            return Enum.IsDefined(value);
        }

        return Enum.TryParse(raw, ignoreCase: true, out value) && Enum.IsDefined(value) && !int.TryParse(raw, out _);
    }

    internal static bool TryParseBool(string raw, out bool value)
    {
        switch (raw.Trim().ToLowerInvariant())
        {
            case "true" or "yes" or "y" or "1":
                value = true;
                return true;
            case "false" or "no" or "n" or "0":
                value = false;
                return true;
            default:
                value = false;
                return false;
        }
    }

    private static void CheckLength(TestCaseDraft draft, string column, string? value, int max, TransferMessages messages)
    {
        if (value != null && value.Length > max)
        {
            draft.Errors.Add(messages.Get("Import:TooLong", ("Column", column), ("Max", max)));
        }
    }

    /// <summary>Text of a cell: trimmed, line breaks as a single "\n" (a CSV file has "\r\n" inside a quoted field).</summary>
    internal static string Normalize(string value) => value.Replace("\r\n", "\n").Replace('\r', '\n').Trim();

    private static string Text(string value) => Normalize(value);

    private static string? Optional(string value)
    {
        var text = Normalize(value);
        return text.Length == 0 ? null : text;
    }
}

/// <summary>What <see cref="TestCaseSheet.Parse"/> found in a table.</summary>
internal sealed class ParsedSheet
{
    public List<TestCaseDraft> Drafts { get; } = new();

    public List<string> FileErrors { get; } = new();

    public List<string> IgnoredColumns { get; } = new();

    /// <summary>The known columns that the file has. A column that is absent leaves an existing test case unchanged.</summary>
    public HashSet<string> Columns { get; } = new(StringComparer.OrdinalIgnoreCase);
}

internal sealed record DraftStep(string Action, string ExpectedResult, string? TestData);

/// <summary>One test case as the file describes it, before it is checked against the library.</summary>
internal sealed class TestCaseDraft
{
    public TestCaseDraft(int row, string code)
    {
        Row = row;
        Code = code;
    }

    public int Row { get; }

    public string Code { get; }

    public string SuitePath { get; set; } = string.Empty;

    public string Title { get; set; } = string.Empty;

    public string? Description { get; set; }

    public string? Preconditions { get; set; }

    public string? Postconditions { get; set; }

    public PriorityLevel Priority { get; set; } = PriorityLevel.Medium;

    public SeverityLevel Severity { get; set; } = SeverityLevel.Medium;

    public TestKind Kind { get; set; } = TestKind.Functional;

    public TestLayer Layer { get; set; } = TestLayer.Acceptance;

    public ExecutionType ExecutionType { get; set; } = Enums.ExecutionType.Manual;

    public string? AutomationId { get; set; }

    public bool IsFlaky { get; set; }

    public List<DraftStep> Steps { get; } = new();

    public List<string> Errors { get; } = new();
}
