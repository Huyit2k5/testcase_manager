using Acme.TestCaseManagement.Enums;
using Volo.Abp.Application.Dtos;

namespace Acme.TestCaseManagement.Requirements.Dtos;

public class RequirementDto : AuditedEntityDto<Guid>
{
    public Guid ProjectId { get; set; }

    public string Code { get; set; } = string.Empty;

    public string Title { get; set; } = string.Empty;

    public string? Description { get; set; }

    public string? AcceptanceCriteria { get; set; }

    public PriorityLevel Priority { get; set; }

    public Guid? MilestoneId { get; set; }

    /// <summary>
    /// How many of its linked test cases (not deprecated) are to be checked against it because it changed and nobody has confirmed
    /// them yet. Filled by the service, not part of the requirement.
    /// </summary>
    public int SuspectTestCaseCount { get; set; }
}

/// <summary>A requirement that a test case is linked to, and whether the test case is to be checked against it.</summary>
public class RequirementLinkDto
{
    public Guid RequirementId { get; set; }

    public string Code { get; set; } = string.Empty;

    public string Title { get; set; } = string.Empty;

    /// <summary>True when the requirement changed since and nobody has confirmed that this test case still matches it.</summary>
    public bool IsSuspect { get; set; }

    public DateTime? SuspectSince { get; set; }

    /// <summary>The fields that changed since, as names ("Title, AcceptanceCriteria").</summary>
    public string? SuspectReason { get; set; }
}

public class RequirementReviewResultDto
{
    /// <summary>How many test cases were confirmed.</summary>
    public int Reviewed { get; set; }
}
