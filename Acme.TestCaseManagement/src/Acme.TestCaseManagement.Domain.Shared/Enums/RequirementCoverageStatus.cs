namespace Acme.TestCaseManagement.Enums;

/// <summary>
/// Verification state of a requirement, derived from the results of its linked test cases.
/// See the plan (ADR 4.4) for the exact rules.
/// </summary>
public enum RequirementCoverageStatus
{
    /// <summary>No linked, non-deprecated test case.</summary>
    Uncovered = 0,

    /// <summary>Covered, but some linked test case has no executed result yet (or all were skipped).</summary>
    NotRun = 1,

    /// <summary>Every linked test case passed (skipped ones are neutral).</summary>
    Passed = 2,

    /// <summary>At least one linked test case failed.</summary>
    Failed = 3,

    /// <summary>No failure, everything executed, but at least one linked test case is blocked.</summary>
    Blocked = 4,
}
