namespace Acme.TestCaseManagement.Enums;

/// <summary>How unstable a test is, from the transition score of its recent outcomes.</summary>
public enum FlakinessLevel
{
    /// <summary>Too few Passed or Failed outcomes in the window to say anything.</summary>
    Insufficient = 0,

    /// <summary>The outcomes hardly change: always passing, always failing, or one real change (a regression or a fix).</summary>
    Stable = 1,

    /// <summary>The outcomes change now and then; worth watching.</summary>
    Watch = 2,

    /// <summary>The outcomes flip back and forth often.</summary>
    Flaky = 3,
}
