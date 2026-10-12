namespace Acme.TestCaseManagement.Enums;

/// <summary>What happened to a test case, as the history of the test case tells it.</summary>
public enum TestCaseHistoryType
{
    /// <summary>The test case was made.</summary>
    Created = 0,

    /// <summary>Its content was edited; the note lists the fields that changed.</summary>
    Edited = 1,

    /// <summary>Its status changed; the values are the old and the new status.</summary>
    StatusChanged = 2,

    /// <summary>A version was published when it was approved.</summary>
    VersionPublished = 3,

    /// <summary>Its tags were replaced; the note lists them.</summary>
    TagsChanged = 4,

    /// <summary>The order of its steps changed.</summary>
    StepsReordered = 5,

    /// <summary>A group of shared steps was inserted, refreshed or detached; the note says which.</summary>
    SharedStepsChanged = 6,
}
