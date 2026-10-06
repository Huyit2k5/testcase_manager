using System.ComponentModel.DataAnnotations;
using Acme.TestCaseManagement.Enums;

namespace Acme.TestCaseManagement.Rtm.Dtos;

public class GetRtmInput
{
    /// <summary>Only requirements of this milestone.</summary>
    public Guid? MilestoneId { get; set; }

    /// <summary>Matches requirement code or title (case-insensitive).</summary>
    public string? Filter { get; set; }

    /// <summary>Count only results from runs of this test plan.</summary>
    public Guid? TestPlanId { get; set; }

    /// <summary>Count only results from runs on this environment (case-insensitive).</summary>
    public string? Environment { get; set; }

    /// <summary>Show only requirements in this status. The summary is not affected.</summary>
    public RequirementCoverageStatus? Status { get; set; }

    [Range(0, int.MaxValue)]
    public int SkipCount { get; set; }

    [Range(1, 1000)]
    public int MaxResultCount { get; set; } = 100;
}
