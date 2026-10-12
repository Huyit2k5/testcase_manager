using Acme.TestCaseManagement.Enums;

namespace Acme.TestCaseManagement.TestCases;

/// <summary>
/// The content of a test case at one moment, taken before an edit so that the edit can say what it changed. It is not stored.
/// </summary>
public record TestCaseContentSnapshot(
    string Code,
    Guid SuiteId,
    string Title,
    string? Description,
    string? Preconditions,
    string? Postconditions,
    PriorityLevel Priority,
    SeverityLevel Severity,
    TestKind Kind,
    TestLayer Layer,
    ExecutionType ExecutionType,
    string? AutomationId,
    bool IsFlaky,
    IReadOnlyList<string> Steps,
    IReadOnlyList<string> Tags)
{
    public static TestCaseContentSnapshot Of(TestCase testCase) => new(
        testCase.Code,
        testCase.SuiteId,
        testCase.Title,
        Clean(testCase.Description),
        Clean(testCase.Preconditions),
        Clean(testCase.Postconditions),
        testCase.Priority,
        testCase.Severity,
        testCase.Kind,
        testCase.Layer,
        testCase.ExecutionType,
        Clean(testCase.AutomationId),
        testCase.IsFlaky,
        testCase.Steps.OrderBy(s => s.StepOrder).Select(s => $"{s.Action}\u001f{s.ExpectedResult}\u001f{Clean(s.TestData)}").ToList(),
        testCase.Tags.Select(t => t.NormalizedName).OrderBy(n => n, StringComparer.Ordinal).ToList());

    /// <summary>The names of the fields that differ, in the order a person reads a test case. Empty when nothing changed.</summary>
    public IReadOnlyList<string> ChangedFields(TestCaseContentSnapshot after)
    {
        var changed = new List<string>();

        void Check(string name, bool differs)
        {
            if (differs)
            {
                changed.Add(name);
            }
        }

        Check("Code", Code != after.Code);
        Check("Suite", SuiteId != after.SuiteId);
        Check("Title", Title != after.Title);
        Check("Description", Description != after.Description);
        Check("Preconditions", Preconditions != after.Preconditions);
        Check("Postconditions", Postconditions != after.Postconditions);
        Check("Priority", Priority != after.Priority);
        Check("Severity", Severity != after.Severity);
        Check("Kind", Kind != after.Kind);
        Check("Layer", Layer != after.Layer);
        Check("ExecutionType", ExecutionType != after.ExecutionType);
        Check("AutomationId", AutomationId != after.AutomationId);
        Check("Flaky", IsFlaky != after.IsFlaky);
        Check("Steps", !Steps.SequenceEqual(after.Steps));
        Check("Tags", !Tags.SequenceEqual(after.Tags));
        return changed;
    }

    private static string? Clean(string? text) => string.IsNullOrWhiteSpace(text) ? null : text.Trim();
}
