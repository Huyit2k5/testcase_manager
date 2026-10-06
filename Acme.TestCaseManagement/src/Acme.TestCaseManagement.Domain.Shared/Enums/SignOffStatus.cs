namespace Acme.TestCaseManagement.Enums;

/// <summary>Lifecycle of a sign-off report. See the plan (ADR 4.5).</summary>
public enum SignOffStatus
{
    /// <summary>Gate passed and the snapshot is frozen, but fewer than the required approvals were given.</summary>
    Pending = 0,

    /// <summary>The required number of distinct approvers signed. Final and never changed again.</summary>
    Approved = 1,

    /// <summary>A newer sign-off for the same scope replaced this pending one. Kept for history.</summary>
    Superseded = 2,
}
