namespace Acme.TestCaseManagement.Enums;

/// <summary>What publishing did with one result of an automated run.</summary>
public enum AutomationOutcome
{
    /// <summary>A new attempt was recorded for the matching test case.</summary>
    Recorded = 0,

    /// <summary>No test case has this Automation ID. Results of tests that the library does not track are normal in CI.</summary>
    Unmatched = 1,

    /// <summary>More than one test case has this Automation ID, so the result cannot be placed.</summary>
    Ambiguous = 2,

    /// <summary>The test case exists but is not Approved, so it cannot be scheduled in a run.</summary>
    NotApproved = 3,

    /// <summary>The test case is not in the run, and the request did not ask to add missing ones.</summary>
    NotInRun = 4,
}
