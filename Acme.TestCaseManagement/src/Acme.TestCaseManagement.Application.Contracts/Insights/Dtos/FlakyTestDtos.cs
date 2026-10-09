using System.ComponentModel.DataAnnotations;
using Acme.TestCaseManagement.Enums;

namespace Acme.TestCaseManagement.Insights.Dtos;

public class GetFlakyTestsInput
{
    /// <summary>Only what belongs to this project. Leave it out to see every project.</summary>
    public Guid? ProjectId { get; set; }

    /// <summary>Tests below this level are left out. Default Watch; Insufficient lists everything that has attempts.</summary>
    public FlakinessLevel MinimumLevel { get; set; } = FlakinessLevel.Watch;

    /// <summary>Part of the code, title or automation id.</summary>
    public string? Filter { get; set; }

    [Range(1, 200)]
    public int MaxResultCount { get; set; } = 100;
}

public class FlakyTestDto
{
    public Guid TestCaseId { get; set; }

    public string Code { get; set; } = string.Empty;

    public string Title { get; set; } = string.Empty;

    public Guid SuiteId { get; set; }

    public string? AutomationId { get; set; }

    /// <summary>Passed and Failed outcomes in the window.</summary>
    public int Observations { get; set; }

    public int Passes { get; set; }

    public int Failures { get; set; }

    /// <summary>Times two neighbouring outcomes differ.</summary>
    public int Flips { get; set; }

    /// <summary>Flips divided by (Observations - 1), 0 to 1.</summary>
    public decimal Score { get; set; }

    public FlakinessLevel Level { get; set; }

    /// <summary>The Flaky flag the test case has now (set by a runner, by a person, or by Apply).</summary>
    public bool IsFlagged { get; set; }

    public DateTime? LastResultAt { get; set; }

    public DateTime? LastFailedAt { get; set; }
}

public class FlakySettingsDto
{
    public int WindowSize { get; set; }

    public int MinimumObservations { get; set; }

    public decimal WatchScore { get; set; }

    public decimal FlakyScore { get; set; }

    public int LookbackDays { get; set; }
}

public class FlakyTestListDto
{
    public List<FlakyTestDto> Items { get; set; } = new();

    /// <summary>How many test cases reached the level, before the list was cut at MaxResultCount.</summary>
    public int TotalCount { get; set; }

    public int FlakyCount { get; set; }

    public int WatchCount { get; set; }

    public FlakySettingsDto Settings { get; set; } = new();
}

public class ApplyFlakyFlagsInput
{
    /// <summary>Only the test cases of this project. Leave it out for every project.</summary>
    public Guid? ProjectId { get; set; }

    /// <summary>Also remove the flag from flagged test cases that now score Stable.</summary>
    public bool ClearRecovered { get; set; }
}

public class ApplyFlakyFlagsResultDto
{
    public int Flagged { get; set; }

    public int Cleared { get; set; }

    public List<string> FlaggedCodes { get; set; } = new();

    public List<string> ClearedCodes { get; set; } = new();
}
