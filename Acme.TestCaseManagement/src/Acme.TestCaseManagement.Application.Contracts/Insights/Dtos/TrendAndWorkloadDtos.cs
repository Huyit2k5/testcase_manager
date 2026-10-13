using System.ComponentModel.DataAnnotations;
using Acme.TestCaseManagement.Enums;

namespace Acme.TestCaseManagement.Insights.Dtos;

public class GetSprintTrendInput
{
    /// <summary>Only the plans of this project. Leave it out for every project.</summary>
    public Guid? ProjectId { get; set; }

    /// <summary>How many plans, the latest ones that have run items.</summary>
    [Range(2, 20)]
    public int Sprints { get; set; } = 8;
}

/// <summary>The figures of one test plan (a "sprint") in the trend.</summary>
public class SprintTrendPointDto
{
    public Guid PlanId { get; set; }

    public string PlanName { get; set; } = string.Empty;

    public PlanStatus Status { get; set; }

    public Guid? MilestoneId { get; set; }

    public DateTime? StartDate { get; set; }

    public DateTime? EndDate { get; set; }

    public int RunCount { get; set; }

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

    public int Defects { get; set; }

    public int OpenDefects { get; set; }

    public decimal? DefectsPer100Executed { get; set; }
}

public class SprintTrendDto
{
    public DateTime GeneratedAt { get; set; }

    /// <summary>The plans in time order, oldest first (by start date, or by creation when a plan has no start date).</summary>
    public List<SprintTrendPointDto> Points { get; set; } = new();

    /// <summary>The pass rate of the last plan minus the one before it, in points; null when either has no value.</summary>
    public decimal? PassRateChange { get; set; }

    public decimal? FirstTimePassRateChange { get; set; }

    /// <summary>The change of the defects per 100 executed tests; a lower number is better.</summary>
    public decimal? DefectDensityChange { get; set; }

    /// <summary>The mean of the pass rates that have a value.</summary>
    public decimal? AveragePassRate { get; set; }
}

public class GetWorkloadInput
{
    public Guid? ProjectId { get; set; }

    /// <summary>Limits the figures to the runs of one plan. Empty: every run.</summary>
    public Guid? TestPlanId { get; set; }

    /// <summary>The days, ending today, whose attempts are counted.</summary>
    [Range(7, 90)]
    public int Days { get; set; } = 14;
}

/// <summary>What one person has: the run items given to them (as they are now) and the attempts they made inside the window.</summary>
public class TesterWorkloadDto
{
    /// <summary>The person; null for attempts whose creator is not known (results sent by a pipeline).</summary>
    public Guid? UserId { get; set; }

    public int AssignedItems { get; set; }

    public int AssignedDone { get; set; }

    public int AssignedPassed { get; set; }

    public int AssignedFailed { get; set; }

    public int Attempts { get; set; }

    public int Passed { get; set; }

    public int Failed { get; set; }

    public int Blocked { get; set; }

    public int Skipped { get; set; }

    /// <summary>Run items that this person made at least one attempt on, inside the window.</summary>
    public int ItemsExecuted { get; set; }

    public int DurationSeconds { get; set; }

    public int ActiveDays { get; set; }

    public decimal AttemptsPerDay { get; set; }

    /// <summary>Passed attempts over those not skipped; null when there is none.</summary>
    public decimal? PassRate { get; set; }
}

public class WorkloadDto
{
    public Guid? TestPlanId { get; set; }

    public string? TestPlanName { get; set; }

    public int Days { get; set; }

    public DateTime GeneratedAt { get; set; }

    /// <summary>Most attempts first.</summary>
    public List<TesterWorkloadDto> Testers { get; set; } = new();

    /// <summary>Run items given to nobody, and how many of those still have no attempt.</summary>
    public int UnassignedItems { get; set; }

    public int UnassignedUntested { get; set; }

    public int TotalItems { get; set; }

    public int TotalAttempts { get; set; }
}
