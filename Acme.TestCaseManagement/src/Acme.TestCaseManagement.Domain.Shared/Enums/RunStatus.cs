namespace Acme.TestCaseManagement.Enums;

/// <summary>Lifecycle status of a test run.</summary>
public enum RunStatus
{
    /// <summary>Created, no attempt recorded yet.</summary>
    Planned = 0,

    /// <summary>At least one execution attempt exists and the run has not been closed.</summary>
    InProgress = 1,

    /// <summary>Closed by a lead; no more items or attempts are accepted.</summary>
    Completed = 2,
}
