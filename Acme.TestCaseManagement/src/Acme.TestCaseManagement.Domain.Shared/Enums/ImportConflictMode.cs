namespace Acme.TestCaseManagement.Enums;

/// <summary>What an import does with a test case whose code already exists in the library.</summary>
public enum ImportConflictMode
{
    /// <summary>The existing test case is left as it is.</summary>
    Skip = 0,

    /// <summary>The existing test case is updated from the file. An unchanged one is left alone.</summary>
    Update = 1,
}
