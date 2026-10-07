using Acme.TestCaseManagement.Insights;

namespace Acme.TestCaseManagement.Insights;

/// <summary>
/// Settings of flaky detection. Change them in the host with <c>Configure&lt;TestCaseManagementInsightsOptions&gt;</c>.
/// </summary>
public class TestCaseManagementInsightsOptions
{
    /// <summary>How many of the latest Passed or Failed outcomes of a test are scored. Default 20.</summary>
    public int WindowSize { get; set; } = 20;

    /// <summary>A test with fewer outcomes than this is not scored. Default 5.</summary>
    public int MinimumObservations { get; set; } = 5;

    /// <summary>A transition score from this on is Watch. Default 0.15 (3 changes in 20 outcomes).</summary>
    public decimal WatchScore { get; set; } = 0.15m;

    /// <summary>A transition score from this on is Flaky. Default 0.30 (6 changes in 20 outcomes).</summary>
    public decimal FlakyScore { get; set; } = 0.30m;

    /// <summary>Only attempts of the last this many days are read. Default 90.</summary>
    public int LookbackDays { get; set; } = 90;

    internal FlakinessSettings ToSettings() => new()
    {
        WindowSize = WindowSize,
        MinimumObservations = MinimumObservations,
        WatchScore = WatchScore,
        FlakyScore = FlakyScore,
    };
}
