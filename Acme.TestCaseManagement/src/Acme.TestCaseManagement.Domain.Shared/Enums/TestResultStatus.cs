namespace Acme.TestCaseManagement.Enums;

/// <summary>Outcome of a test run item or execution attempt.</summary>
public enum TestResultStatus
{
    Untested = 0,
    Passed = 1,
    Failed = 2,
    Blocked = 3,
    Skipped = 4,
}

