using System.ComponentModel.DataAnnotations;

namespace Acme.TestCaseManagement.Insights.Dtos;

public class GetDashboardInput
{
    /// <summary>Only what belongs to this project. Leave it out to see every project.</summary>
    public Guid? ProjectId { get; set; }

    /// <summary>Limits the figures to the runs of one plan. Empty: every run.</summary>
    public Guid? TestPlanId { get; set; }

    /// <summary>Days shown by the velocity chart (and, with no plan, the start of the burn-down).</summary>
    [Range(7, 90)]
    public int Days { get; set; } = 14;
}

public class DashboardProgressDto
{
    public int TotalItems { get; set; }

    public int Passed { get; set; }

    public int Failed { get; set; }

    public int Blocked { get; set; }

    public int Skipped { get; set; }

    public int Untested { get; set; }

    public decimal CompletionPercentage { get; set; }

    /// <summary>Passed over items that are not skipped, as in the quality gate; null when nothing applies.</summary>
    public decimal? PassRate { get; set; }

    public decimal? FirstTimePassRate { get; set; }
}

public class VelocityPointDto
{
    public DateTime Date { get; set; }

    public int Attempts { get; set; }

    public int ItemsCompleted { get; set; }

    public int Passed { get; set; }

    public int Failed { get; set; }
}

public class VelocityDto
{
    public List<VelocityPointDto> Points { get; set; } = new();

    public int TotalAttempts { get; set; }

    public decimal AveragePerDay { get; set; }

    public decimal Last7DaysAverage { get; set; }

    /// <summary>Percent change of the last 7 days against the 7 before; null when there are less than 14 days or none before.</summary>
    public decimal? TrendPercent { get; set; }
}

public class BurnDownPointDto
{
    public DateTime Date { get; set; }

    /// <summary>Items without any attempt at the end of the day; null for a day to come.</summary>
    public int? Remaining { get; set; }

    public decimal Ideal { get; set; }
}

public class BurnDownDto
{
    public DateTime Start { get; set; }

    public DateTime End { get; set; }

    public int TotalItems { get; set; }

    public int RemainingAtStart { get; set; }

    public int RemainingNow { get; set; }

    public List<BurnDownPointDto> Points { get; set; } = new();

    public decimal ItemsPerDay { get; set; }

    public DateTime? ProjectedFinish { get; set; }

    public bool? OnTrack { get; set; }
}

public class DefectDensityDto
{
    public int Defects { get; set; }

    public int OpenDefects { get; set; }

    public int ResolvedDefects { get; set; }

    public int ExecutedTests { get; set; }

    public decimal? DefectsPer100Executed { get; set; }

    public int TestsWithDefects { get; set; }

    public decimal? TestsWithDefectsPercent { get; set; }

    public int OpenCritical { get; set; }

    public int OpenHigh { get; set; }

    public int OpenMedium { get; set; }

    public int OpenLow { get; set; }
}

public class FlakySummaryDto
{
    public int Flaky { get; set; }

    public int Watch { get; set; }

    /// <summary>Test cases with enough outcomes to be scored.</summary>
    public int Scored { get; set; }
}

public class DashboardDto
{
    public Guid? TestPlanId { get; set; }

    public string? TestPlanName { get; set; }

    public int Days { get; set; }

    public DateTime GeneratedAt { get; set; }

    public int RunCount { get; set; }

    public DashboardProgressDto Progress { get; set; } = new();

    public VelocityDto Velocity { get; set; } = new();

    public BurnDownDto BurnDown { get; set; } = new();

    public DefectDensityDto DefectDensity { get; set; } = new();

    public FlakySummaryDto Flaky { get; set; } = new();
}
