namespace Acme.TestCaseManagement.Enums;

/// <summary>What an import did (or, in a dry run, would do) with one test case or one result row.</summary>
public enum ImportOutcome
{
    /// <summary>A new test case was added.</summary>
    Created = 0,

    /// <summary>An existing test case was changed.</summary>
    Updated = 1,

    /// <summary>Nothing was done: the test case exists and is not to be updated, or the file does not change it.</summary>
    Skipped = 2,

    /// <summary>A result was recorded as a new attempt.</summary>
    Recorded = 3,

    /// <summary>The row has an error. Nothing is imported while any row is invalid.</summary>
    Invalid = 4,
}
